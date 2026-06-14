using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VerseKit.App.Services;

namespace VerseKit.App.ViewModels;

/// <summary>A row in the "Available plugins" list: one registry entry plus its
/// install state relative to what's currently installed.</summary>
public sealed partial class PluginCatalogItemViewModel : ObservableObject
{
    public PluginCatalogItemViewModel(PluginRegistryEntry entry) => Entry = entry;

    public PluginRegistryEntry Entry { get; }

    public string Name => Entry.Name;
    public string Version => $"v{Entry.Version}";
    public string Description => Entry.Description;
    public string Author => string.IsNullOrWhiteSpace(Entry.Author) ? "Unknown" : Entry.Author!;
    public bool IsBeta => Entry.Beta;
    public IBrush IconBrush => PluginColor.For(Entry.Id);

    public string Initials
    {
        get
        {
            var words = Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var letters = words.Select(w => char.ToUpperInvariant(w[0])).Take(2).ToArray();
            return letters.Length > 0 ? new string(letters) : "?";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(CanInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(CanInstall))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(CanInstall))]
    private bool _isBusy;

    public string ActionLabel =>
        IsBusy ? "Installing…" :
        IsUpdateAvailable ? "Update" :
        IsInstalled ? "Installed" : "Install";

    public bool CanInstall => !IsBusy && (!IsInstalled || IsUpdateAvailable);
}
