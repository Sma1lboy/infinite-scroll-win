using System.Windows;
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

        // Ctrl+Scroll for window scrolling (like Cmd+Scroll on macOS)
        MainScrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl+Scroll: scroll the outer window
            MainScrollViewer.ScrollToVerticalOffset(
                MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void HelpOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Focus handling for help overlay
    }
}
