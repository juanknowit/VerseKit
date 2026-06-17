using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using AccessChecker.ViewModels;

namespace AccessChecker.Views;

public partial class AccessCheckerView : UserControl
{
    public AccessCheckerView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is AccessCheckerViewModel vm)
                vm.PickSavePathAsync = PickSavePathAsync;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string?> PickSavePathAsync(string suggestedName, string extension)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export comparison",
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(extension.ToUpperInvariant() + " file") { Patterns = ["*." + extension] }
            ]
        });

        return file?.Path.LocalPath;
    }
}
