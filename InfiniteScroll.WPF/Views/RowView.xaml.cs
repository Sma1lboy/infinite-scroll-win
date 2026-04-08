using System.Windows;
using System.Windows.Controls;
using InfiniteScroll.Models;
using InfiniteScroll.ViewModels;

namespace InfiniteScroll.Views;

public partial class RowView : UserControl
{
    public static readonly DependencyProperty FocusedCellIdProperty =
        DependencyProperty.Register(nameof(FocusedCellId), typeof(Guid?), typeof(RowView));

    public Guid? FocusedCellId
    {
        get => (Guid?)GetValue(FocusedCellIdProperty);
        set => SetValue(FocusedCellIdProperty, value);
    }

    public RowView()
    {
        InitializeComponent();
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
