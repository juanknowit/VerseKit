using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using QueryRunner.ViewModels;

namespace QueryRunner.Views;

public partial class QueryRunnerView : UserControl
{
    private QueryRunnerViewModel? _vm;

    public QueryRunnerView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.ResultsReady -= RebuildColumns;
            if (DataContext is QueryRunnerViewModel vm)
            {
                _vm = vm;
                vm.PickSavePathAsync = PickSavePathAsync;
                vm.ResultsReady += RebuildColumns;
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Rebuilds the grid's columns to match the latest result set.</summary>
    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null || _vm is null) return;

        grid.Columns.Clear();
        for (var i = 0; i < _vm.ResultColumns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.ResultColumns[i],
                Binding = new Binding($"Cells[{i}]"),
                IsReadOnly = true
            });
        }
    }

    private async Task<string?> PickSavePathAsync(string suggestedName, string extension)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export results",
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
