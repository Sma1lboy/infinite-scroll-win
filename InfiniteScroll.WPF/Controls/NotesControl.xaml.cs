using System.Windows;
using System.Windows.Controls;
using InfiniteScroll.Models;
using InfiniteScroll.ViewModels;

namespace InfiniteScroll.Controls;

public partial class NotesControl : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(NotesControl),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public NotesControl()
    {
        InitializeComponent();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is CellModel cell)
        {
            var window = Window.GetWindow(this);
            if (window?.DataContext is PanelStoreViewModel vm)
            {
                vm.SetFocusFromCell(cell.Id);
            }
        }
    }
}
