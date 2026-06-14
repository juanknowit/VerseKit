using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SecurityRoles.ViewModels;

namespace SecurityRoles.Views;

public partial class SecurityRolesView : UserControl
{
    public SecurityRolesView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is SecurityRolesViewModel vm)
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
            Title = "Export to Excel",
            SuggestedFileName = suggestedName,
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel Workbook") { Patterns = ["*.xlsx"] }
            ]
        });

        return file?.Path.LocalPath;
    }
}
