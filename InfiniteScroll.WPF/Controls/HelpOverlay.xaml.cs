using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InfiniteScroll.ViewModels;

namespace InfiniteScroll.Controls;

public partial class HelpOverlay : UserControl
{
    private record ShortcutEntry(string Key, string Description);

    private static readonly ShortcutEntry[] Shortcuts =
    [
        new("Ctrl+W", "Close current cell"),
        new("Ctrl+D", "Duplicate current cell"),
        new("Ctrl+Shift+↓", "New row below"),
        new("Ctrl+=", "Zoom in"),
        new("Ctrl+-", "Zoom out"),
        new("Ctrl+↑", "Focus row above"),
        new("Ctrl+↓", "Focus row below"),
        new("Ctrl+←", "Focus left"),
        new("Ctrl+→", "Focus right"),
        new("Ctrl+Scroll", "Scroll between rows"),
        new("Ctrl+Backspace", "Delete to start of line"),
        new("Ctrl+/", "Toggle this help"),
    ];

    public HelpOverlay()
    {
        InitializeComponent();
        ShortcutList.ItemsSource = Shortcuts;
    }

    private void Background_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is PanelStoreViewModel vm)
            vm.ShowHelp = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is PanelStoreViewModel vm)
            vm.ShowHelp = false;
    }
}
