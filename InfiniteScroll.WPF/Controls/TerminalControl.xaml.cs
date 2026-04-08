using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using InfiniteScroll.Models;
using InfiniteScroll.Services;
using InfiniteScroll.ViewModels;

namespace InfiniteScroll.Controls;

public partial class TerminalControl : UserControl
{
    public static readonly DependencyProperty InitialDirectoryProperty =
        DependencyProperty.Register(nameof(InitialDirectory), typeof(string), typeof(TerminalControl),
            new PropertyMetadata(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    public string InitialDirectory
    {
        get => (string)GetValue(InitialDirectoryProperty);
        set => SetValue(InitialDirectoryProperty, value);
    }

    private ConPtyTerminal? _terminal;

    public TerminalControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_terminal != null) return;

        _terminal = new ConPtyTerminal(InitialDirectory);
        _terminal.OutputReceived += OnOutputReceived;
        _terminal.ProcessExited += OnProcessExited;
        _terminal.Start();

        TerminalInput.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _terminal?.Dispose();
        _terminal = null;
    }

    private void OnOutputReceived(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var paragraph = TerminalOutput.Document.Blocks.LastBlock as Paragraph
                            ?? new Paragraph();
            if (TerminalOutput.Document.Blocks.Count == 0)
                TerminalOutput.Document.Blocks.Add(paragraph);

            paragraph.Inlines.Add(new Run(text)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE0))
            });

            TerminalOutput.ScrollToEnd();
        });
    }

    private void OnProcessExited(int exitCode)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (DataContext is CellModel cell)
                cell.IsRunning = false;
        });
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var command = TerminalInput.Text;
            TerminalInput.Clear();
            _terminal?.SendLine(command);

            // Echo the command
            OnOutputReceived($"> {command}\n");
            e.Handled = true;
        }
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
