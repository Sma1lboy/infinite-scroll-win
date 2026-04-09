using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InfiniteScroll.ViewModels;

namespace InfiniteScroll.Views;

public partial class MainWindow : Window
{
    public PanelStoreViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new PanelStoreViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        Closing += (_, _) => ViewModel.Save();

        PreviewKeyDown += (_, e) =>
        {
            // Run app shortcuts in the tunneling phase, before ItemsControl /
            // KeyboardNavigation default behavior eats the arrow keys (which
            // would otherwise prevent the Window.InputBindings KeyBindings
            // from ever firing).
            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control);
            bool shift = mods.HasFlag(ModifierKeys.Shift);
            bool alt = mods.HasFlag(ModifierKeys.Alt);
            if (!ctrl || alt) return;

            System.Windows.Input.ICommand? cmd = (e.Key, shift) switch
            {
                (Key.Down, true)  => ViewModel.AddPanelCommand,
                (Key.W, false)    => ViewModel.CloseCurrentCellCommand,
                (Key.D, false)    => ViewModel.DuplicateCurrentCellCommand,
                (Key.Up, false)   => ViewModel.FocusUpCommand,
                (Key.Down, false) => ViewModel.FocusDownCommand,
                (Key.Left, false) => ViewModel.FocusLeftCommand,
                (Key.Right,false) => ViewModel.FocusRightCommand,
                (Key.OemPlus, false)     => ViewModel.ZoomInCommand,
                (Key.OemMinus, false)    => ViewModel.ZoomOutCommand,
                (Key.OemQuestion, false) => ViewModel.ToggleHelpCommand,
                _ => null,
            };
            if (cmd != null && cmd.CanExecute(null))
            {
                cmd.Execute(null);
                e.Handled = true;
            }
        };

        // Scroll focused row into view when focus changes
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelStoreViewModel.FocusedCellId))
                ScrollFocusedRowIntoView();
        };
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+Wheel: let the inner terminal ScrollViewer handle its own history
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        // Plain wheel: scroll the outer window. We must intercept here in the
        // tunneling PreviewMouseWheel phase, otherwise the nested ScrollViewer
        // inside each TerminalControl swallows the event and the main window
        // never scrolls.
        MainScrollViewer.ScrollToVerticalOffset(
            MainScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void ScrollFocusedRowIntoView()
    {
        if (ViewModel.FocusedCellId == null) return;

        // Find the focused panel and its visual container
        var focusedCellId = ViewModel.FocusedCellId.Value;
        for (var i = 0; i < ViewModel.Panels.Count; i++)
        {
            var panel = ViewModel.Panels[i];
            if (panel.Cells.Any(c => c.Id == focusedCellId))
            {
                // Get the container element for this panel
                var container = PanelList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                container?.BringIntoView();
                break;
            }
        }
    }
}
