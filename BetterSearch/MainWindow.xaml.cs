using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BetterSearch;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly FileIndexService _indexService = new();
    private DispatcherTimer? _searchTimer;
    private CancellationTokenSource? _indexCancellation;
    private string _statusText = "Index has not been built yet.";
    private string _resultCountText = "Type to search";
    private string _indexSummaryText = "No index loaded.";
    private bool _isIndexing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Searching on every keystroke gets noisy with a large index, so wait for a brief pause.
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RunSearch();
        };

        Loaded += MainWindow_Loaded;
        KeyDown += MainWindow_KeyDown;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FileSearchResult> Results { get; } = [];

    public ObservableCollection<DriveSelection> Drives { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ResultCountText
    {
        get => _resultCountText;
        private set => SetField(ref _resultCountText, value);
    }

    public string IndexSummaryText
    {
        get => _indexSummaryText;
        private set => SetField(ref _indexSummaryText, value);
    }

    public bool IsIndexing
    {
        get => _isIndexing;
        private set => SetField(ref _isIndexing, value);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDriveList();
        SearchBox.Focus();
        await RebuildIndexAsync();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            _ = RebuildIndexAsync();
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelectedPaths();
            e.Handled = true;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RebuildIndexAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _indexCancellation?.Cancel();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        QueueSearch();
    }

    private void SearchOptionChanged(object sender, RoutedEventArgs e)
    {
        QueueSearch();
    }

    private void CaseSensitiveCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        CaseSensitiveMenuItem.IsChecked = CaseSensitiveCheckBox.IsChecked == true;
        QueueSearch();
    }

    private void DriveSelectionChanged(object sender, RoutedEventArgs e)
    {
        IndexSummaryText = "Drive selection changed. Rebuild the index to apply it.";
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelected();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelected();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedFolder();
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSelected();
    }

    private void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedFolder();
    }

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedPaths();
    }

    private void CopyNameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedNames();
    }

    private void SelectAllResultsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ResultsGrid.SelectAll();
    }

    private void ShowOptionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OptionsPanel.Visibility = ShowOptionsMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshDrivesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RefreshDriveList();
        IndexSummaryText = "Drive list refreshed. Rebuild the index to apply changes.";
    }

    private void FocusSearchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CaseSensitiveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CaseSensitiveCheckBox.IsChecked = CaseSensitiveMenuItem.IsChecked;
        QueueSearch();
    }

    private void NamesOnlyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ScopeComboBox.SelectedIndex = NamesOnlyMenuItem.IsChecked ? 1 : 0;
        QueueSearch();
    }

    private void SelectAllDrivesButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var drive in Drives)
        {
            drive.IsSelected = true;
        }

        IndexSummaryText = "All drives selected. Rebuild the index to apply it.";
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task RebuildIndexAsync()
    {
        _indexCancellation?.Cancel();
        _indexCancellation = new CancellationTokenSource();
        var token = _indexCancellation.Token;
        var settings = BuildSearchSettings();
        if (settings.SelectedDriveRoots.Count == 0)
        {
            StatusText = "Select at least one drive before rebuilding the index.";
            return;
        }

        IsIndexing = true;
        RefreshButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        Results.Clear();
        ResultCountText = "Indexing";

        var started = DateTimeOffset.Now;
        var progress = new Progress<IndexProgress>(p =>
        {
            var driveText = p.TotalDrives > 0 ? $" Drive {p.DrivesComplete}/{p.TotalDrives}." : string.Empty;
            StatusText = $"{p.ItemsIndexed:N0} items indexed.{driveText} {p.CurrentLocation}";
            IndexSummaryText = $"Indexed {p.ItemsIndexed:N0} items";
        });

        try
        {
            var count = await _indexService.RebuildAsync(settings, progress, token);
            var elapsed = DateTimeOffset.Now - started;
            StatusText = $"{count:N0} items indexed in {elapsed.TotalSeconds:N1}s. Search is ready.";
            IndexSummaryText = $"{count:N0} indexed items across {settings.SelectedDriveRoots.Count} selected drive(s).";
            RunSearch();
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Indexing canceled. {_indexService.Count:N0} previously indexed items remain searchable.";
            IndexSummaryText = "Indexing canceled.";
        }
        finally
        {
            IsIndexing = false;
            RefreshButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void RefreshDriveList()
    {
        var selected = Drives.Where(d => d.IsSelected).Select(d => d.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Drives.Clear();

        foreach (var drive in _indexService.GetSearchableDrives())
        {
            var option = new DriveSelection(drive);
            if (selected.Count > 0)
            {
                option.IsSelected = selected.Contains(option.Root);
            }

            Drives.Add(option);
        }
    }

    private void QueueSearch()
    {
        if (_searchTimer is null)
        {
            // Some XAML events fire while the window is still being built.
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RunSearch()
    {
        NamesOnlyMenuItem.IsChecked = ScopeComboBox.SelectedIndex == 1;

        var query = SearchBox.Text.Trim();
        Results.Clear();

        if (query.Length == 0)
        {
            ResultCountText = "Type to search";
            return;
        }

        var settings = BuildSearchSettings();
        var matches = _indexService.Search(query, settings);
        foreach (var match in matches)
        {
            Results.Add(match);
        }

        ResultCountText = matches.Count >= settings.MaxResults
            ? $"Showing first {settings.MaxResults:N0}"
            : $"{matches.Count:N0} results";
    }

    private SearchSettings BuildSearchSettings()
    {
        return new SearchSettings
        {
            CaseSensitive = CaseSensitiveCheckBox.IsChecked == true,
            IncludeFiles = IncludeFilesCheckBox.IsChecked == true,
            IncludeFolders = IncludeFoldersCheckBox.IsChecked == true,
            Scope = ScopeComboBox.SelectedIndex == 1 ? SearchScope.FileName : SearchScope.FullPath,
            IncludeExtensions = IncludeExtensionsBox.Text,
            ExcludeExtensions = ExcludeExtensionsBox.Text,
            MaxResults = GetSelectedResultLimit(),
            SelectedDriveRoots = Drives.Where(d => d.IsSelected).Select(d => d.Root).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }

    private int GetSelectedResultLimit()
    {
        if (ResultLimitComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var value))
        {
            return value;
        }

        return 1000;
    }

    private void OpenSelected()
    {
        if (ResultsGrid.SelectedItem is not FileSearchResult result)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = result.Path,
                UseShellExecute = true
            });
        }
        catch
        {
            OpenFolder(result);
        }
    }

    private void OpenSelectedFolder()
    {
        if (ResultsGrid.SelectedItem is FileSearchResult result)
        {
            OpenFolder(result);
        }
    }

    private void CopySelectedPaths()
    {
        var paths = ResultsGrid.SelectedItems.OfType<FileSearchResult>().Select(r => r.Path).ToArray();
        if (paths.Length > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }
    }

    private void CopySelectedNames()
    {
        var names = ResultsGrid.SelectedItems.OfType<FileSearchResult>().Select(r => r.Name).ToArray();
        if (names.Length > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, names));
        }
    }

    private static void OpenFolder(FileSearchResult result)
    {
        var path = result.IsDirectory ? result.Path : result.Folder;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = result.IsDirectory ? $"\"{path}\"" : $"/select,\"{result.Path}\"",
            UseShellExecute = true
        });
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
