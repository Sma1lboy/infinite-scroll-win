using System.Text;
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
    private readonly VtParser _vtParser = new();
    private Paragraph? _currentParagraph;

    public TerminalControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_terminal != null) return;

        // Initialize document
        TerminalOutput.Document.Blocks.Clear();
        _currentParagraph = new Paragraph { Margin = new Thickness(0) };
        TerminalOutput.Document.Blocks.Add(_currentParagraph);

        _terminal = new ConPtyTerminal(InitialDirectory);
        _terminal.DataReceived += OnDataReceived;
        _terminal.ProcessExited += OnProcessExited;

        // Calculate initial terminal size from control dimensions
        var cols = Math.Max(80, (int)(ActualWidth / 8));
        var rows = Math.Max(24, (int)(ActualHeight / 16));
        _terminal.Start(cols, rows);

        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _terminal?.Dispose();
        _terminal = null;
    }

    private void OnDataReceived(byte[] data)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var segments = _vtParser.Parse(data);

            foreach (var seg in segments)
            {
                if (_currentParagraph == null)
                {
                    _currentParagraph = new Paragraph { Margin = new Thickness(0) };
                    TerminalOutput.Document.Blocks.Add(_currentParagraph);
                }

                // Split on newlines to create proper line breaks
                var lines = seg.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        // New paragraph for new line
                        _currentParagraph = new Paragraph { Margin = new Thickness(0) };
                        TerminalOutput.Document.Blocks.Add(_currentParagraph);
                    }

                    var line = lines[i].Replace("\r", "");
                    if (line.Length == 0) continue;

                    var run = new Run(line)
                    {
                        Foreground = new SolidColorBrush(seg.Foreground),
                    };

                    if (seg.Bold)
                        run.FontWeight = FontWeights.Bold;
                    if (seg.Underline)
                        run.TextDecorations = TextDecorations.Underline;

                    // Only set background if non-default
                    if (seg.Background != Color.FromRgb(0x1A, 0x1A, 0x1F))
                        run.Background = new SolidColorBrush(seg.Background);

                    _currentParagraph.Inlines.Add(run);
                }
            }

            // Limit scrollback to ~5000 paragraphs
            while (TerminalOutput.Document.Blocks.Count > 5000)
                TerminalOutput.Document.Blocks.Remove(TerminalOutput.Document.Blocks.FirstBlock);

            OutputScroller.ScrollToEnd();
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_terminal == null || !_terminal.IsRunning) return;

        // Handle special keys that TextInput doesn't capture
        byte[]? data = null;

        switch (e.Key)
        {
            case Key.Enter:
                data = "\r"u8.ToArray();
                break;
            case Key.Back:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Ctrl+Backspace → Ctrl+W (delete word)
                    data = [(byte)0x17];
                }
                else
                {
                    data = [(byte)0x7f]; // DEL
                }
                break;
            case Key.Tab:
                data = "\t"u8.ToArray();
                break;
            case Key.Escape:
                data = [(byte)0x1b];
                break;
            case Key.Up:
                data = "\x1b[A"u8.ToArray();
                break;
            case Key.Down:
                data = "\x1b[B"u8.ToArray();
                break;
            case Key.Right:
                data = "\x1b[C"u8.ToArray();
                break;
            case Key.Left:
                data = "\x1b[D"u8.ToArray();
                break;
            case Key.Home:
                data = "\x1b[H"u8.ToArray();
                break;
            case Key.End:
                data = "\x1b[F"u8.ToArray();
                break;
            case Key.Delete:
                data = "\x1b[3~"u8.ToArray();
                break;
            case Key.PageUp:
                data = "\x1b[5~"u8.ToArray();
                break;
            case Key.PageDown:
                data = "\x1b[6~"u8.ToArray();
                break;
            default:
                // Handle Ctrl+letter combinations
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    var key = e.Key == Key.System ? e.SystemKey : e.Key;
                    if (key >= Key.A && key <= Key.Z)
                    {
                        data = [(byte)(key - Key.A + 1)]; // Ctrl+A = 0x01, etc.
                    }
                }
                return; // Let TextInput handle regular characters
        }

        if (data != null)
        {
            _terminal.SendInput(data);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Handled in PreviewKeyDown
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_terminal == null || !_terminal.IsRunning) return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            _terminal.SendInput(e.Text);
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

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_terminal != null && ActualWidth > 0 && ActualHeight > 0)
        {
            var cols = Math.Max(80, (int)(ActualWidth / 8));
            var rows = Math.Max(24, (int)(ActualHeight / 16));
            _terminal.Resize(cols, rows);
        }
    }
}
