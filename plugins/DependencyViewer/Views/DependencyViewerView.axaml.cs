using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DependencyViewer.Views;

public partial class DependencyViewerView : UserControl
{
    public DependencyViewerView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
