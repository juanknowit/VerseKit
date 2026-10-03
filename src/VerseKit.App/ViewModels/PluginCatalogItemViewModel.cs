using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VerseKit.App.Services;

namespace VerseKit.App.ViewModels;

/// <summary>An Available row in the Plugins manager: a registry tool that isn't
/// installed. Its switch installs it (downloaded from the GitHub release).</summary>
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

    /// <summary>True for a built-in (bundled) tool the user switched off: turning
    /// it back on restores the copy shipped with the app.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Invoked when the user switches the tool on (parent installs it).</summary>
    public System.Action<PluginCatalogItemViewModel>? TurnedOn { get; set; }

    /// <summary>Available rows start off; switching on requests an install.</summary>
    [ObservableProperty]
    private bool _isOn;

    partial void OnIsOnChanged(bool value)
    {
        if (value) TurnedOn?.Invoke(this);
    }

    /// <summary>True while this tool is being downloaded/installed.</summary>
    [ObservableProperty]
    private bool _isBusy;
}
