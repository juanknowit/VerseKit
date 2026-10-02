using Avalonia;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FlowRuns.Models;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FlowRuns.ViewModels;

namespace FlowRuns.Views;

public partial class FlowRunsView : UserControl
{
    public FlowRunsView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is FlowRunsViewModel vm)
                vm.PickSavePathAsync = PickSavePathAsync;
        };

        // Double-clicking a run opens its details (the row's checkbox and
        // Details button handle their own clicks).
        this.FindControl<DataGrid>("RunsGrid")!.DoubleTapped += OnRunDoubleTapped;
    }

    private void OnRunDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<CheckBox>(includeSelf: true) is null
            && source.FindAncestorOfType<Button>(includeSelf: true) is null
            && source.FindAncestorOfType<DataGridRow>()?.DataContext is FlowRunItem run
            && DataContext is FlowRunsViewModel vm)
            vm.InspectCommand.Execute(run);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string?> PickSavePathAsync(string suggestedName, string extension)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export flow runs",
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
