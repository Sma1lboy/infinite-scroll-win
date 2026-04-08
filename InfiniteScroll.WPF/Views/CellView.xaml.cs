using System.Windows;
using System.Windows.Controls;

namespace InfiniteScroll.Views;

public partial class CellView : UserControl
{
    public static readonly DependencyProperty TerminalFontSizeProperty =
        DependencyProperty.Register(nameof(TerminalFontSize), typeof(double), typeof(CellView),
            new PropertyMetadata(16.0));

    public double TerminalFontSize
    {
        get => (double)GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public CellView()
    {
        InitializeComponent();
    }
}
