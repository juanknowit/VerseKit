using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace TableBrowser.Views;

/// <summary>
/// Copies its parameter (text, or e.g. a Guid, as text) to the clipboard — used by row context menus
/// and ⌘C. A static instance, so menus inside item templates need no ancestor
/// bindings. Kept in the plugin (not the SDK) so it works on any host version.
/// </summary>
public sealed class CopyCommand : ICommand
{
    public static CopyCommand Instance { get; } = new();

    // Availability depends only on the parameter; controls re-query
    // CanExecute whenever their CommandParameter changes.
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => !string.IsNullOrEmpty(parameter?.ToString());

    public void Execute(object? parameter)
    {
        if (parameter?.ToString() is { Length: > 0 } text
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard })
            _ = clipboard.SetTextAsync(text);
    }
}
