using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VerseKit.Core.Models;

namespace VerseKit.App.ViewModels;

/// <summary>A row in the Plugins manager: wraps a <see cref="PluginEntry"/> with
/// live enabled state and presentation helpers.</summary>
public sealed partial class PluginItemViewModel : ObservableObject
{
    public PluginItemViewModel(PluginEntry entry, bool isEnabled)
    {
        Entry = entry;
        _isEnabled = isEnabled;
    }

    public PluginEntry Entry { get; }

    public string Name => Entry.Plugin.Name;
    public string Version => $"v{Entry.Plugin.Version}";
    public string Description => Entry.Plugin.Description;
    public bool IsBundled => Entry.Origin == PluginOrigin.Bundled;
    public bool IsRemovable => Entry.Origin == PluginOrigin.User;
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

    /// <summary>Invoked when the user flips the toggle (parent persists + refilters).</summary>
    public System.Action<PluginItemViewModel>? EnabledChanged { get; set; }

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this);
}
