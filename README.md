# BetterSearch

A Windows desktop file search application built with WPF and .NET 8.

## Features

- WPF desktop interface with `File`, `Edit`, `View`, and `Search` menus.
- Scans selected ready fixed, removable, and network drives.
- Builds an in-memory index of files and folders using parallel directory workers for faster indexing.
- Searches instantly after the index is built.
- Supports multi-word matching against full paths or file names only.
- Supports case-sensitive matching.
- Filters by included or excluded file extensions.
- Filters results to files, folders, or both.
- Lets users control the maximum number of displayed results.
- Opens a result directly, reveals it in File Explorer, or copies selected paths/names.
- Handles access-denied folders without stopping the scan.
- Lets you cancel and rebuild the index.

## Run

```powershell
dotnet run --project .\BetterSearch\BetterSearch.csproj
```

## Standalone Launch

The standalone Windows build is published to:

```powershell
.\publish\BetterSearch-win-x64\BetterSearch.exe
```

If startup fails, the app writes a log next to the executable:

```powershell
.\publish\BetterSearch-win-x64\BetterSearch.log
```

## Build

```powershell
dotnet build .\BetterSearch.sln
```

## Publish

```powershell
dotnet publish .\BetterSearch\BetterSearch.csproj -c Release -r win-x64 --self-contained true -o .\publish\BetterSearch-win-x64
```

## Notes

This version uses normal Windows directory enumeration with parallel workers, so it does not require administrator access or a background service. It is functionally similar to Everything in workflow, but it is not yet using the NTFS MFT/USN journal, which is how Everything achieves near-instant indexing on very large NTFS drives.
