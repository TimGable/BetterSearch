using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace BetterSearch;

public sealed class DriveSelection : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public DriveSelection(DriveInfo drive)
    {
        // Store the root separately because it is the value passed back into the indexer.
        Root = drive.RootDirectory.FullName;
        Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? Root : $"{Root} {drive.VolumeLabel}";
        Description = $"{drive.DriveType} drive";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Root { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            // The checkbox binding listens for this so drive changes update without rebuilding the list.
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
