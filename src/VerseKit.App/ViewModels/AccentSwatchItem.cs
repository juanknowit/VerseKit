using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VerseKit.App.Theming;

namespace VerseKit.App.ViewModels;

/// <summary>A selectable accent swatch shown in the Settings theme picker.</summary>
public sealed partial class AccentSwatchItem : ObservableObject
{
    public AccentSwatchItem(AccentPreset preset, bool isSelected)
    {
        Id = preset.Id;
        Name = preset.Name;
        SwatchBrush = preset.SwatchBrush;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Name { get; }
    public IBrush SwatchBrush { get; }

    [ObservableProperty]
    private bool _isSelected;
}
