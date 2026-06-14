using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SolutionExplorer.ViewModels;

namespace SolutionExplorer.Views;

public partial class SolutionExplorerView : UserControl
{
    public SolutionExplorerView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is SolutionExplorerViewModel vm)
                vm.PickSavePathAsync = PickSavePathAsync;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Native save dialog for the exported solution .zip.</summary>
    private async Task<string?> PickSavePathAsync(string suggestedName)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export solution",
            SuggestedFileName = suggestedName,
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("Solution package (.zip)") { Patterns = ["*.zip"] }
            ]
        });

        return file?.Path.LocalPath;
    }
}
