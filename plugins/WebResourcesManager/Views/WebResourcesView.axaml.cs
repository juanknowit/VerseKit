using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using WebResourcesManager.Models;
using WebResourcesManager.ViewModels;

namespace WebResourcesManager.Views;

public partial class WebResourcesView : UserControl
{
    // AvaloniaEdit's TextEditor has no bindable Text property, so the
    // editor ↔ ViewModel sync lives here. This is pure UI glue — all
    // behaviour (dirty tracking, save, publish) stays in the ViewModel.
    private readonly TextMate.Installation _textMate;
    private readonly RegistryOptions _registryOptions;
    private WebResourcesViewModel? _vm;
    private bool _syncing;

    public WebResourcesView()
    {
        InitializeComponent();

        _registryOptions = new RegistryOptions(ThemeName.LightPlus);
        _textMate = CodeEditor.InstallTextMate(_registryOptions);
        ApplyGrammar(WebResourceType.Script);

        CodeEditor.TextChanged += (_, _) =>
        {
            if (_syncing || _vm is null) return;
            _syncing = true;
            _vm.EditorText = CodeEditor.Text;
            _syncing = false;
        };

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as WebResourcesViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                _vm.ConfirmAsync = ConfirmAsync;
                _vm.PromptNewResourceAsync = PromptNewResourceAsync;
            }
        };
    }

    /// <summary>Prompts for a new web resource: name (prefixed) + type.</summary>
    private async Task<(string Name, WebResourceType Type)?> PromptNewResourceAsync(string prefix)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return null;

        var nameBox = new TextBox { PlaceholderText = "name", Width = 240 };
        var typeBox = new ComboBox
        {
            Width = 240,
            SelectedIndex = 0,
            ItemsSource = new[]
            {
                WebResourceType.Script,
                WebResourceType.WebPage,
                WebResourceType.CssStylesheet,
                WebResourceType.Data
            }
        };

        var create = new Button { Content = "Create", Classes = { "Primary" } };
        var cancel = new Button { Content = "Cancel" };
        (string, WebResourceType)? result = null;

        var dialog = new Window
        {
            Title = "New Web Resource",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "New Web Resource", FontSize = 16,
                                    FontWeight = FontWeight.SemiBold,
                                    Foreground = new SolidColorBrush(Color.Parse("#1C1C1E")) },
                    new TextBlock { Text = $"Name (will be prefixed with \"{prefix}_\")",
                                    FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#6E6E73")) },
                    nameBox,
                    new TextBlock { Text = "Type", FontSize = 12,
                                    Foreground = new SolidColorBrush(Color.Parse("#6E6E73")) },
                    typeBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, create }
                    }
                }
            }
        };

        create.Click += (_, _) =>
        {
            var n = nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(n)) { nameBox.Focus(); return; }
            result = (n, (WebResourceType)typeBox.SelectedItem!);
            dialog.Close();
        };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Shows a modal confirm dialog over the host window.</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return true; // headless — don't block

        var result = false;

        var ok = new Button
        {
            Content = confirmLabel,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (destructive)
            ok.Foreground = new SolidColorBrush(Color.Parse("#FF3B30"));
        else
            ok.Classes.Add("Success");

        var cancel = new Button { Content = "Cancel" };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold,
                                    Foreground = new SolidColorBrush(Color.Parse("#1C1C1E")) },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13,
                                    Foreground = new SolidColorBrush(Color.Parse("#3C3C43")) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        ok.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => { result = false; dialog.Close(); };

        await dialog.ShowDialog(owner);
        return result;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;

        switch (e.PropertyName)
        {
            case nameof(WebResourcesViewModel.EditorText):
                if (!_syncing && CodeEditor.Text != _vm.EditorText)
                {
                    _syncing = true;
                    CodeEditor.Text = _vm.EditorText;
                    _syncing = false;
                }
                break;

            case nameof(WebResourcesViewModel.SelectedResource):
                if (_vm.SelectedResource is { } resource)
                    ApplyGrammar(resource.ResourceType);
                break;
        }
    }

    private void ApplyGrammar(WebResourceType type)
    {
        var languageId = type switch
        {
            WebResourceType.Script => "javascript",
            WebResourceType.CssStylesheet => "css",
            WebResourceType.WebPage => "html",
            WebResourceType.Data => "xml",
            WebResourceType.Xsl => "xml",
            WebResourceType.Resx => "xml",
            _ => "javascript"
        };
        _textMate.SetGrammar(_registryOptions.GetScopeByLanguageId(languageId));
    }
}
