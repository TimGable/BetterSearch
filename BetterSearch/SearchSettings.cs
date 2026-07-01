namespace BetterSearch;

public enum SearchScope
{
    FullPath,
    FileName
}

public sealed class SearchSettings
{
    public bool CaseSensitive { get; set; }

    public bool IncludeFiles { get; set; } = true;

    public bool IncludeFolders { get; set; } = true;

    public SearchScope Scope { get; set; } = SearchScope.FullPath;

    public int MaxResults { get; set; } = 1000;

    public string IncludeExtensions { get; set; } = string.Empty;

    public string ExcludeExtensions { get; set; } = string.Empty;

    public HashSet<string> SelectedDriveRoots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SearchSettings Clone()
    {
        return new SearchSettings
        {
            CaseSensitive = CaseSensitive,
            IncludeFiles = IncludeFiles,
            IncludeFolders = IncludeFolders,
            Scope = Scope,
            MaxResults = MaxResults,
            IncludeExtensions = IncludeExtensions,
            ExcludeExtensions = ExcludeExtensions,
            SelectedDriveRoots = new HashSet<string>(SelectedDriveRoots, StringComparer.OrdinalIgnoreCase)
        };
    }
}
