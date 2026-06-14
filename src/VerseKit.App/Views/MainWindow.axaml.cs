using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using VerseKit.App.ViewModels;

namespace VerseKit.App.Views;

public partial class MainWindow : Window
{
    private SavedConnectionItem? _pendingDrag;
    private Point _dragStart;
    private bool _dragging;
    private ConnectionFolder? _hoverFolder;

    public MainWindow()
    {
        InitializeComponent();

        // Dialogs/pickers are view/TopLevel concerns; the view-models just
        // invoke these callbacks.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConnectionForm.PickCertificateAsync = PickCertificateAsync;
                vm.PromptTextAsync = PromptTextAsync;
                vm.ConfirmAsync = ConfirmAsync;
                vm.PickFolderAsync = PickFolderAsync;
            }
        };

        // ── Drag-and-drop: move a connection onto a folder ──────────
        // Self-contained pointer drag (no OS data-transfer) since this is an
        // in-app move within one tree. Capture on threshold; resolve the drop
        // target by hit-testing on release.
        ConnectionsTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        ConnectionsTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        ConnectionsTree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Record a possible drag source without handling the event, so a
        // plain click still connects via the button.
        _pendingDrag = FindDataContext<SavedConnectionItem>(e.Source as Control);
        _dragStart = e.GetPosition(ConnectionsTree);
        _dragging = false;
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDrag is null) return;
        if (!e.GetCurrentPoint(ConnectionsTree).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        var pos = e.GetPosition(ConnectionsTree);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _dragStart.X) < 5 && Math.Abs(pos.Y - _dragStart.Y) < 5) return;

            // Past the threshold — begin dragging: capture the pointer so the
            // connection button won't fire a click on release, and dim the row.
            _dragging = true;
            _pendingDrag.IsDragging = true;
            e.Pointer.Capture(ConnectionsTree);
            Cursor = new Cursor(StandardCursorType.DragMove);
        }

        // Live highlight of the folder under the pointer.
        var hit = ConnectionsTree.InputHitTest(pos) as Control;
        SetHoverFolder(FindDataContext<ConnectionFolder>(hit));
    }

    private async void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging || _pendingDrag is null)
        {
            EndDrag();
            return;
        }

        var item = _pendingDrag;
        // Drop target: the folder under the pointer (resolved from a folder
        // header or any connection inside it); empty → ungroup.
        var hit = ConnectionsTree.InputHitTest(e.GetPosition(ConnectionsTree)) as Control;
        var folder = FindDataContext<ConnectionFolder>(hit)?.Name;

        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;

        if (string.Equals(item.Folder, folder, StringComparison.OrdinalIgnoreCase)) return;
        if (DataContext is MainWindowViewModel vm)
            await vm.MoveConnectionAsync(item, folder);
    }

    private void SetHoverFolder(ConnectionFolder? folder)
    {
        if (ReferenceEquals(_hoverFolder, folder)) return;
        if (_hoverFolder is not null) _hoverFolder.IsDropTarget = false;
        _hoverFolder = folder;
        if (_hoverFolder is not null) _hoverFolder.IsDropTarget = true;
    }

    private void EndDrag()
    {
        if (_pendingDrag is not null) _pendingDrag.IsDragging = false;
        SetHoverFolder(null);
        _pendingDrag = null;
        _dragging = false;
        Cursor = Cursor.Default;
    }

    private static T? FindDataContext<T>(Control? c) where T : class
    {
        while (c is not null)
        {
            if (c.DataContext is T match) return match;
            c = c.GetVisualParent() as Control;
        }
        return null;
    }

    /// <summary>Modal single-line text prompt. Returns the text, or null if cancelled.</summary>
    private async Task<string?> PromptTextAsync(string title, string label, string? initial)
    {
        var box = new TextBox { Text = initial ?? "", PlaceholderText = label, Width = 300 };
        var ok = new Button { Content = "OK", Classes = { "Primary" } };
        var cancel = new Button { Content = "Cancel" };
        string? result = null;

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold,
                                    Foreground = new SolidColorBrush(Color.Parse("#1C1C1E")) },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        ok.Click += (_, _) => { result = box.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };
        box.Loaded += (_, _) => box.Focus();

        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>Modal yes/no confirmation. Returns true to proceed. The confirm
    /// button shows <paramref name="confirmLabel"/>, tinted red for destructive verbs.</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var destructive = confirmLabel is "Delete" or "Remove";
        var ok = new Button { Content = confirmLabel };
        if (destructive)
            ok.Foreground = new SolidColorBrush(Color.Parse("#D70015"));
        else
            ok.Classes.Add("Primary");
        var cancel = new Button { Content = "Cancel" };
        var result = false;

        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold,
                                    Foreground = new SolidColorBrush(Color.Parse("#1C1C1E")) },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13,
                                    Foreground = new SolidColorBrush(Color.Parse("#3C3C43")) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok }
                    }
                }
            }
        };

        ok.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => { result = false; dialog.Close(); };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a plugin folder",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickCertificateAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select certificate",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Certificate (.pfx, .p12)")
                {
                    Patterns = ["*.pfx", "*.p12"]
                }
            ]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
