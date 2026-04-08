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

        // Scroll focused row into view when focus changes
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelStoreViewModel.FocusedCellId))
                ScrollFocusedRowIntoView();
        };
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl+Scroll: scroll the outer container (like Cmd+Scroll on macOS)
            MainScrollViewer.ScrollToVerticalOffset(
                MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
        // Without Ctrl: let the event propagate to the terminal control
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
