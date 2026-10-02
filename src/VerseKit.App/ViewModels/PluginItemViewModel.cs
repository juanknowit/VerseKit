using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VerseKit.App.Services;
using VerseKit.Core.Models;

namespace VerseKit.App.ViewModels;

/// <summary>An installed row in the Plugins manager. Its switch is the install
/// state: turning it off uninstalls the tool (it moves to Available).</summary>
public sealed partial class PluginItemViewModel : ObservableObject
{
    public PluginItemViewModel(PluginEntry entry) => Entry = entry;

    public PluginEntry Entry { get; }

    public string Name => Entry.Plugin.Name;
    public string Version => $"v{Entry.Plugin.Version}";
    public string Description => Entry.Plugin.Description;
    public bool IsBundled => Entry.Origin == PluginOrigin.Bundled;
    public string OriginLabel => IsBundled ? "Bundled" : "Installed";
    public IBrush IconBrush => PluginColor.For(Entry.Plugin.PluginId.ToString());

    /// <summary>Up to two uppercase initials from the plugin name, for the row icon.</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var letters = words.Select(w => char.ToUpperInvariant(w[0])).Take(2).ToArray();
            return letters.Length > 0 ? new string(letters) : "?";
        }
    }

    /// <summary>Invoked when the user switches the tool off (parent uninstalls it).</summary>
    public System.Action<PluginItemViewModel>? TurnedOff { get; set; }

    /// <summary>Installed rows start on; switching off requests an uninstall.</summary>
    [ObservableProperty]
    private bool _isOn = true;

    partial void OnIsOnChanged(bool value)
    {
        if (!value) TurnedOff?.Invoke(this);
    }

    /// <summary>A newer registry version of this tool, if any (set by the host).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateLabel))]
    private PluginRegistryEntry? _updateEntry;

    public bool HasUpdate => UpdateEntry is not null;
    public string UpdateLabel => UpdateEntry is { } u ? $"Update to v{u.Version}" : "";

    /// <summary>True if the registry marks this plugin as beta (set by the host).</summary>
    [ObservableProperty]
    private bool _isBeta;
}
