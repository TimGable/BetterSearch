using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace BetterSearch;

public sealed class FileIndexService
{
    // Each directory is scanned one level at a time so the worker queue controls the traversal.
    private static readonly EnumerationOptions FastEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    private readonly object _syncRoot = new();
    private List<FileSearchResult> _items = [];

    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.Count;
            }
        }
    }

    public IReadOnlyList<DriveInfo> GetSearchableDrives()
    {
        // Skip drives Windows reports as unavailable so indexing does not hang on empty card readers or offline paths.
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady && IsSearchableDrive(d.DriveType))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<int> RebuildAsync(SearchSettings settings, IProgress<IndexProgress> progress, CancellationToken cancellationToken)
    {
        // Build a fresh snapshot first, then swap it in once scanning is complete.
        // That keeps the old index usable if the user cancels halfway through.
        var drives = GetSearchableDrives()
            .Where(d => settings.SelectedDriveRoots.Count == 0 || settings.SelectedDriveRoots.Contains(d.RootDirectory.FullName))
            .ToArray();

        var found = new ConcurrentQueue<FileSearchResult>();
        var counters = new IndexCounters();
        var completedDrives = 0;

        // Drive scans run in parallel, while each drive also manages its own queue of folders.
        await Task.Run(() =>
        {
            Parallel.ForEach(
                drives,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    // A little parallelism helps a lot here, but too many drive walkers can make the machine feel busy.
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6)
                },
                drive =>
                {
                    progress.Report(new IndexProgress(Volatile.Read(ref counters.IndexedCount), Volatile.Read(ref completedDrives), drives.Length, $"Scanning {drive.Name}"));
                    ScanDrive(drive.RootDirectory.FullName, settings, found, counters, progress, cancellationToken);
                    var complete = Interlocked.Increment(ref completedDrives);
                    progress.Report(new IndexProgress(Volatile.Read(ref counters.IndexedCount), complete, drives.Length, $"Finished {drive.Name}"));
                });
        }, cancellationToken);

        var snapshot = found.ToList();
        lock (_syncRoot)
        {
            _items = snapshot;
        }

        progress.Report(new IndexProgress(snapshot.Count, completedDrives, drives.Length, "Index ready"));
        return snapshot.Count;
    }

    public IReadOnlyList<FileSearchResult> Search(string query, SearchSettings settings)
    {
        // Treat spaces as separate required terms, which makes broad path searches feel predictable.
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return [];
        }

        List<FileSearchResult> snapshot;
        lock (_syncRoot)
        {
            // Search against a stable reference so the UI is not reading a list while it is being replaced.
            snapshot = _items;
        }

        var comparison = settings.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var maxResults = Math.Clamp(settings.MaxResults, 100, 25000);

        return snapshot
            .Where(item => MatchesKind(item, settings))
            .Where(item => MatchesExtensions(item, settings))
            .Where(item =>
            {
                var searchable = settings.Scope == SearchScope.FileName ? item.Name : item.Path;
                return terms.All(term => searchable.Contains(term, comparison));
            })
            .Take(maxResults)
            .ToArray();
    }

    private static void ScanDrive(
        string root,
        SearchSettings settings,
        ConcurrentQueue<FileSearchResult> found,
        IndexCounters counters,
        IProgress<IndexProgress> progress,
        CancellationToken cancellationToken)
    {
        var pending = new ConcurrentQueue<string>();
        pending.Enqueue(root);
        var activeDirectories = 0;
        var workers = Math.Clamp(Environment.ProcessorCount, 2, 10);

        // Workers share one folder queue so slow or protected branches do not hold up the whole scan.
        Parallel.For(
            0,
            workers,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = workers },
            _ =>
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!pending.TryDequeue(out var current))
                    {
                        // The queue can go empty for a moment while another worker is still adding child folders.
                        if (Volatile.Read(ref activeDirectories) == 0 && pending.IsEmpty)
                        {
                            return;
                        }

                        Thread.Yield();
                        continue;
                    }

                    Interlocked.Increment(ref activeDirectories);
                    try
                    {
                        ScanDirectory(current, pending, found, counters, progress, cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeDirectories);
                    }
                }
            });
    }

    private static void ScanDirectory(
        string current,
        ConcurrentQueue<string> pending,
        ConcurrentQueue<FileSearchResult> found,
        IndexCounters counters,
        IProgress<IndexProgress> progress,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> directories;

        try
        {
            directories = Directory.EnumerateDirectories(current, "*", FastEnumerationOptions);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            // Normal Windows folders can disappear or deny access while indexing. Just skip them and keep going.
            return;
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending.Enqueue(directory);

            found.Enqueue(new FileSearchResult(directory, isDirectory: true));
            ReportOccasionally(counters, directory, progress);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(current, "*", FastEnumerationOptions);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            // File enumeration has the same access issues as folders, especially under system directories.
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            found.Enqueue(new FileSearchResult(file, isDirectory: false));
            ReportOccasionally(counters, current, progress);
        }
    }

    private static void ReportOccasionally(
        IndexCounters counters,
        string currentLocation,
        IProgress<IndexProgress> progress)
    {
        var count = Interlocked.Increment(ref counters.IndexedCount);
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref counters.LastProgressTicks);
        if (count % 2500 != 0 && now - previous < 250)
        {
            return;
        }

        // Progress updates are throttled so indexing does not spend its time repainting the status bar.
        Interlocked.Exchange(ref counters.LastProgressTicks, now);
        progress.Report(new IndexProgress(count, 0, 0, currentLocation));
    }

    private static bool MatchesKind(FileSearchResult item, SearchSettings settings)
    {
        return item.IsDirectory ? settings.IncludeFolders : settings.IncludeFiles;
    }

    private static bool MatchesExtensions(FileSearchResult item, SearchSettings settings)
    {
        if (item.IsDirectory)
        {
            return true;
        }

        var include = ParseExtensions(settings.IncludeExtensions);
        if (include.Count > 0 && !include.Contains(item.Extension))
        {
            return false;
        }

        var exclude = ParseExtensions(settings.ExcludeExtensions);
        return exclude.Count == 0 || !exclude.Contains(item.Extension);
    }

    private static HashSet<string> ParseExtensions(string value)
    {
        // Accept the common ways people type extension lists: "pdf docx", "pdf,docx", or ".pdf; .docx".
        return value
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ext => ext.TrimStart('.'))
            .Where(ext => ext.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSearchableDrive(DriveType driveType)
    {
        return driveType is DriveType.Fixed or DriveType.Removable or DriveType.Network;
    }

    private sealed class IndexCounters
    {
        public int IndexedCount;

        public long LastProgressTicks;
    }
}

public readonly record struct IndexProgress(int ItemsIndexed, int DrivesComplete, int TotalDrives, string CurrentLocation);
