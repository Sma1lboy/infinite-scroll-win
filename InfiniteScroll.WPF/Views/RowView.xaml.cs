using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InfiniteScroll.Models;
using InfiniteScroll.ViewModels;

namespace InfiniteScroll.Views;

public partial class RowView : UserControl
{
    public static readonly DependencyProperty FocusedCellIdProperty =
        DependencyProperty.Register(nameof(FocusedCellId), typeof(Guid?), typeof(RowView),
            new PropertyMetadata(null, OnFocusedCellIdChanged));

    public static readonly DependencyProperty TerminalFontSizeProperty =
        DependencyProperty.Register(nameof(TerminalFontSize), typeof(double), typeof(RowView),
            new PropertyMetadata(16.0));

    public Guid? FocusedCellId
    {
        get => (Guid?)GetValue(FocusedCellIdProperty);
        set => SetValue(FocusedCellIdProperty, value);
    }

    public double TerminalFontSize
    {
        get => (double)GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x66, 0x99, 0xFF));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public RowView()
    {
        InitializeComponent();
    }

    private static void OnFocusedCellIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RowView view)
            view.UpdateFocusBorders();
    }

    private void UpdateFocusBorders()
    {
        if (DataContext is not PanelModel panel) return;

        for (var i = 0; i < panel.Cells.Count; i++)
        {
            var container = CellsContainer.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container == null) continue;

            // Find the FocusHighlight border within the template
            var focusBorder = FindChild<System.Windows.Controls.Border>(container, "FocusHighlight");
            if (focusBorder != null)
            {
                focusBorder.BorderBrush = panel.Cells[i].Id == FocusedCellId
                    ? FocusBrush
                    : TransparentBrush;
            }
        }
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            var result = FindChild<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PanelModel panel)
        {
            var vm = FindViewModel();
            vm?.RemovePanel(panel.Id);
        }
    }

    private void NotesToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PanelModel panel)
        {
            panel.ToggleNotes();
        }
    }

    private PanelStoreViewModel? FindViewModel()
    {
        var window = Window.GetWindow(this);
        return window?.DataContext as PanelStoreViewModel;
    }
}
