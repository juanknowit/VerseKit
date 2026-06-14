using CommunityToolkit.Mvvm.ComponentModel;
using VerseKit.App.Theming;

namespace VerseKit.App.ViewModels;

/// <summary>A selectable background style shown in the Settings picker.</summary>
public sealed partial class BackgroundOptionItem : ObservableObject
{
    public BackgroundOptionItem(BackgroundOption option, bool isSelected)
    {
        Id = option.Id;
        Name = option.Name;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}
