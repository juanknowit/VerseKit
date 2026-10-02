using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TableBrowser.ViewModels;

namespace TableBrowser.Views;

public partial class TableBrowserView : UserControl
{
    public TableBrowserView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is TableBrowserViewModel vm)
                vm.PickSavePathAsync = PickSavePathAsync;
        };
    }

    /// <summary>Shows the native save dialog and returns the chosen path, or null if cancelled.</summary>
    private async Task<string?> PickSavePathAsync(string suggestedName)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to CSV",
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV") { Patterns = ["*.csv"] }
            ]
        });

        return file?.Path.LocalPath;
    }
}
