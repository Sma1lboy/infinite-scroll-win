using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    private ScreenBuffer? _screen;
    private VtParser? _vtParser;

    // Brush cache — frozen brushes are thread-safe and faster
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    // Font metrics
    private double _cellWidth = 8;
    private double _cellHeight = 16;

    // Synchronized update (DEC ?2026): defer rendering while active
    private bool _syncUpdating;

    // Cursor blink
    private readonly Rectangle _cursorRect = new();
    private readonly DispatcherTimer _cursorTimer = new();
    private bool _cursorBlinkOn = true;

    // Dirty flag — skip render if nothing changed (used by sync update)
    #pragma warning disable CS0414
    private bool _dirty;
    #pragma warning restore CS0414

    public TerminalControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Cursor appearance
        _cursorRect.Width = 2;
        _cursorRect.Fill = GetBrush(ScreenCell.DefaultFg);
        _cursorRect.IsHitTestVisible = false;
        CursorCanvas.Children.Add(_cursorRect);

        // Blink timer
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(530);
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorBlinkOn = !_cursorBlinkOn;
            _cursorRect.Visibility = _cursorBlinkOn ? Visibility.Visible : Visibility.Hidden;
        };
    }

    private void MeasureFontMetrics()
    {
        var fontSize = TerminalOutput.FontSize;
        var fontFamily = TerminalOutput.FontFamily;
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var ft = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _cellWidth = ft.WidthIncludingTrailingWhitespace;
        _cellHeight = ft.Height;
    }

    private (int cols, int rows) CalculateTerminalSize()
    {
        var usableWidth = Math.Max(0, ActualWidth - 16); // RichTextBox padding
        var usableHeight = Math.Max(0, ActualHeight);
        var cols = Math.Max(80, (int)(usableWidth / _cellWidth));
        var rows = Math.Max(24, (int)(usableHeight / _cellHeight));
        return (cols, rows);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_terminal != null) return;

        MeasureFontMetrics();

        // Configure RichTextBox for exact grid rendering
        TerminalOutput.Document.PagePadding = new Thickness(0);
        TerminalOutput.Document.LineHeight = _cellHeight;
        TerminalOutput.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

        var (cols, rows) = CalculateTerminalSize();

        _screen = new ScreenBuffer(rows, cols);
        _vtParser = new VtParser(_screen);

        _terminal = new ConPtyTerminal(InitialDirectory);
        _terminal.DataReceived += OnDataReceived;
        _terminal.ProcessExited += OnProcessExited;
        _terminal.Start(cols, rows);

        _cursorTimer.Start();
        Keyboard.Focus(this);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cursorTimer.Stop();
        _terminal?.Dispose();
        _terminal = null;
    }

    // ─── Data handling ───────────────────────────────────────

    private void OnDataReceived(byte[] data)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_vtParser == null || _screen == null) return;

            _vtParser.Parse(data);
            _dirty = true;

            // Check synchronized update state
            _syncUpdating = _vtParser.SyncUpdating;

            // Only render if not inside a synchronized update frame
            if (!_syncUpdating)
            {
                RenderScreen();
                _dirty = false;
            }
        });
    }

    // ─── Rendering ───────────────────────────────────────────

    private void RenderScreen()
    {
        if (_screen == null) return;

        var doc = TerminalOutput.Document;
        doc.Blocks.Clear();

        // ── Render scrollback history above the active viewport ──
        var scrollback = _screen.Scrollback;
        for (int i = 0; i < scrollback.Count; i++)
        {
            var line = scrollback[i];
            doc.Blocks.Add(BuildScrollbackParagraph(line));
        }

        // ── Render active screen buffer ──
        for (int row = 0; row < _screen.Rows; row++)
        {
            doc.Blocks.Add(BuildScreenParagraph(row));
        }

        UpdateCursorPosition();
    }

    private Paragraph BuildScrollbackParagraph(ScreenCell[] line)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = _cellHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        int col = 0;
        while (col < line.Length)
        {
            var cell = line[col];
            var fg = cell.Fg;
            var bg = cell.Bg;
            var bold = cell.Bold;
            var underline = cell.Underline;

            var start = col;
            while (col < line.Length)
            {
                var c = line[col];
                if (c.Fg != fg || c.Bg != bg || c.Bold != bold || c.Underline != underline)
                    break;
                col++;
            }

            var chars = new char[col - start];
            for (int i = start; i < col; i++)
                chars[i - start] = line[i].Char;

            var text = new string(chars);
            if (col == line.Length)
                text = text.TrimEnd();
            if (text.Length == 0) continue;

            var run = new Run(text) { Foreground = GetBrush(fg) };
            if (bold) run.FontWeight = FontWeights.Bold;
            if (underline) run.TextDecorations = TextDecorations.Underline;
            if (bg != ScreenCell.DefaultBg) run.Background = GetBrush(bg);

            para.Inlines.Add(run);
        }

        return para;
    }

    private Paragraph BuildScreenParagraph(int row)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = _cellHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        int col = 0;
        while (col < _screen!.Cols)
        {
            var cell = _screen.GetCell(row, col);
            var fg = cell.Fg;
            var bg = cell.Bg;
            var bold = cell.Bold;
            var underline = cell.Underline;

            var start = col;
            while (col < _screen.Cols)
            {
                var c = _screen.GetCell(row, col);
                if (c.Fg != fg || c.Bg != bg || c.Bold != bold || c.Underline != underline)
                    break;
                col++;
            }

            var chars = new char[col - start];
            for (int i = start; i < col; i++)
                chars[i - start] = _screen.GetCell(row, i).Char;

            var text = new string(chars);
            if (col == _screen.Cols)
                text = text.TrimEnd();
            if (text.Length == 0) continue;

            var run = new Run(text) { Foreground = GetBrush(fg) };
            if (bold) run.FontWeight = FontWeights.Bold;
            if (underline) run.TextDecorations = TextDecorations.Underline;
            if (bg != ScreenCell.DefaultBg) run.Background = GetBrush(bg);

            para.Inlines.Add(run);
        }

        return para;
    }

    private void UpdateCursorPosition()
    {
        if (_screen == null) return;

        var scrollbackLines = _screen.Scrollback.Count;

        // Cursor is inside ScrollViewer, so position in document coordinates
        double x = 8 + _screen.CursorCol * _cellWidth;  // 8 = RichTextBox padding
        double y = 8 + (scrollbackLines + _screen.CursorRow) * _cellHeight;

        Canvas.SetLeft(_cursorRect, x);
        Canvas.SetTop(_cursorRect, y);
        _cursorRect.Height = _cellHeight;

        var visible = _screen.CursorVisible && IsKeyboardFocusWithin;
        _cursorRect.Visibility = visible
            ? (_cursorBlinkOn ? Visibility.Visible : Visibility.Hidden)
            : Visibility.Hidden;

        _cursorBlinkOn = true;
        _cursorTimer.Stop();
        _cursorTimer.Start();

        // Always scroll to bottom — same as Windows Terminal behavior
        OutputScroller.ScrollToEnd();
    }

    private static SolidColorBrush GetBrush(Color color)
    {
        if (BrushCache.TryGetValue(color, out var cached))
            return cached;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        BrushCache[color] = brush;
        return brush;
    }

    // ─── Process lifecycle ───────────────────────────────────

    private void OnProcessExited(int exitCode)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (DataContext is CellModel cell)
                cell.IsRunning = false;
        });
    }

    // ─── Keyboard input ──────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_terminal == null || !_terminal.IsRunning) return;

        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool alt = mods.HasFlag(ModifierKeys.Alt);

        // ── App-level shortcuts (pass through to MainWindow) ──
        if (ctrl && !alt)
        {
            if (shift && e.Key == Key.Down) return;
            if (!shift && (e.Key == Key.Up || e.Key == Key.Down ||
                           e.Key == Key.Left || e.Key == Key.Right)) return;
            if (!shift && (e.Key == Key.W || e.Key == Key.D ||
                           e.Key == Key.OemPlus || e.Key == Key.OemMinus ||
                           e.Key == Key.OemQuestion)) return;

            // Ctrl+Shift+C: always copy
            if (shift && e.Key == Key.C)
            {
                CopySelection();
                e.Handled = true;
                return;
            }

            // Ctrl+Shift+V: always paste
            if (shift && e.Key == Key.V)
            {
                PasteClipboard();
                e.Handled = true;
                return;
            }

            // Ctrl+V: paste from clipboard
            if (!shift && e.Key == Key.V)
            {
                PasteClipboard();
                e.Handled = true;
                return;
            }

            // Ctrl+C: copy if selected, else SIGINT
            if (!shift && e.Key == Key.C)
            {
                var selection = TerminalOutput.Selection;
                if (selection != null && !selection.IsEmpty)
                {
                    Clipboard.SetText(selection.Text);
                    e.Handled = true;
                    return;
                }
                // Fall through to send SIGINT (0x03)
            }
        }

        // ── Terminal keys ──
        byte[]? data = null;

        switch (e.Key)
        {
            case Key.Enter:
                data = "\r"u8.ToArray();
                break;
            case Key.Back:
                data = ctrl ? [(byte)0x17] : [(byte)0x7f];
                break;
            case Key.Tab:
                data = "\t"u8.ToArray();
                break;
            case Key.Escape:
                data = [(byte)0x1b];
                break;
            case Key.Up:    data = "\x1b[A"u8.ToArray(); break;
            case Key.Down:  data = "\x1b[B"u8.ToArray(); break;
            case Key.Right: data = "\x1b[C"u8.ToArray(); break;
            case Key.Left:  data = "\x1b[D"u8.ToArray(); break;
            case Key.Home:  data = "\x1b[H"u8.ToArray(); break;
            case Key.End:   data = "\x1b[F"u8.ToArray(); break;
            case Key.Delete:   data = "\x1b[3~"u8.ToArray(); break;
            case Key.PageUp:   data = "\x1b[5~"u8.ToArray(); break;
            case Key.PageDown: data = "\x1b[6~"u8.ToArray(); break;
            default:
                if (ctrl && !alt)
                {
                    var key = e.Key == Key.System ? e.SystemKey : e.Key;
                    if (key >= Key.A && key <= Key.Z)
                    {
                        _terminal.SendInput([(byte)(key - Key.A + 1)]);
                        e.Handled = true;
                    }
                }
                return;
        }

        if (data != null)
        {
            _terminal.SendInput(data);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e) { }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_terminal == null || !_terminal.IsRunning) return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            _terminal.SendInput(e.Text);
            e.Handled = true;
        }
    }

    // ─── Mouse input ─────────────────────────────────────────

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
    }

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Right-click paste (Windows Terminal convention)
        PasteClipboard();
        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        OutputScroller.ScrollToVerticalOffset(
            OutputScroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // ─── Clipboard helpers ───────────────────────────────────

    private void PasteClipboard()
    {
        if (_terminal == null || !_terminal.IsRunning) return;
        if (!Clipboard.ContainsText()) return;

        var text = Clipboard.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            // Normalize line endings for terminal
            text = text.Replace("\r\n", "\r").Replace("\n", "\r");
            _terminal.SendInput(text);
        }
    }

    private void CopySelection()
    {
        var selection = TerminalOutput.Selection;
        if (selection != null && !selection.IsEmpty)
            Clipboard.SetText(selection.Text);
    }

    // ─── Focus ───────────────────────────────────────────────

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is CellModel cell)
        {
            var window = Window.GetWindow(this);
            if (window?.DataContext is PanelStoreViewModel vm)
                vm.SetFocusFromCell(cell.Id);
        }
        // Show cursor when we get focus
        _cursorRect.Visibility = Visibility.Visible;
        _cursorBlinkOn = true;
    }

    // ─── Resize ──────────────────────────────────────────────

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_terminal != null && ActualWidth > 0 && ActualHeight > 0)
        {
            MeasureFontMetrics();
            TerminalOutput.Document.LineHeight = _cellHeight;

            var (cols, rows) = CalculateTerminalSize();
            _screen?.Resize(rows, cols);
            _terminal.Resize(cols, rows);
        }
    }
}
