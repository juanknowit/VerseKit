using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AccessChecker.Views;

public partial class AccessCheckerView : UserControl
{
    public AccessCheckerView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
