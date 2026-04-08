using System.Text;
using System.Windows.Media;

namespace InfiniteScroll.Services;

/// <summary>
/// Parses VT100/xterm escape sequences and produces styled text segments.
/// </summary>
public class VtParser
{
    public record TextSegment(string Text, Color Foreground, Color Background, bool Bold, bool Underline);

    // Standard ANSI 16 colors
    private static readonly Color[] AnsiColors =
    [
        Color.FromRgb(0x00, 0x00, 0x00), // 0  Black
        Color.FromRgb(0xCC, 0x44, 0x44), // 1  Red
        Color.FromRgb(0x44, 0xCC, 0x44), // 2  Green
        Color.FromRgb(0xCC, 0xCC, 0x44), // 3  Yellow
        Color.FromRgb(0x44, 0x44, 0xCC), // 4  Blue
        Color.FromRgb(0xCC, 0x44, 0xCC), // 5  Magenta
        Color.FromRgb(0x44, 0xCC, 0xCC), // 6  Cyan
        Color.FromRgb(0xCC, 0xCC, 0xCC), // 7  White
        // Bright variants
        Color.FromRgb(0x66, 0x66, 0x66), // 8  Bright Black
        Color.FromRgb(0xFF, 0x66, 0x66), // 9  Bright Red
        Color.FromRgb(0x66, 0xFF, 0x66), // 10 Bright Green
        Color.FromRgb(0xFF, 0xFF, 0x66), // 11 Bright Yellow
        Color.FromRgb(0x66, 0x66, 0xFF), // 12 Bright Blue
        Color.FromRgb(0xFF, 0x66, 0xFF), // 13 Bright Magenta
        Color.FromRgb(0x66, 0xFF, 0xFF), // 14 Bright Cyan
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 15 Bright White
    ];

    private static readonly Color DefaultFg = Color.FromRgb(0xD9, 0xD9, 0xE0);
    private static readonly Color DefaultBg = Color.FromRgb(0x1A, 0x1A, 0x1F);

    private Color _currentFg = DefaultFg;
    private Color _currentBg = DefaultBg;
    private bool _bold;
    private bool _underline;

    private enum State { Normal, Escape, Csi, Osc }
    private State _state = State.Normal;
    private readonly StringBuilder _paramBuf = new();
    private readonly StringBuilder _textBuf = new();

    public List<TextSegment> Parse(byte[] data)
    {
        var segments = new List<TextSegment>();
        var text = Encoding.UTF8.GetString(data);

        foreach (var ch in text)
        {
            switch (_state)
            {
                case State.Normal:
                    if (ch == '\x1b')
                    {
                        FlushText(segments);
                        _state = State.Escape;
                    }
                    else
                    {
                        _textBuf.Append(ch);
                    }
                    break;

                case State.Escape:
                    if (ch == '[')
                    {
                        _state = State.Csi;
                        _paramBuf.Clear();
                    }
                    else if (ch == ']')
                    {
                        _state = State.Osc;
                        _paramBuf.Clear();
                    }
                    else
                    {
                        // Unknown escape — discard
                        _state = State.Normal;
                    }
                    break;

                case State.Csi:
                    if (ch >= 0x40 && ch <= 0x7E) // final byte
                    {
                        ProcessCsi(ch);
                        _state = State.Normal;
                    }
                    else
                    {
                        _paramBuf.Append(ch);
                    }
                    break;

                case State.Osc:
                    if (ch == '\x07' || ch == '\x1b') // BEL or ESC terminates OSC
                    {
                        _state = State.Normal;
                    }
                    break;
            }
        }

        FlushText(segments);
        return segments;
    }

    private void FlushText(List<TextSegment> segments)
    {
        if (_textBuf.Length > 0)
        {
            segments.Add(new TextSegment(_textBuf.ToString(), _currentFg, _currentBg, _bold, _underline));
            _textBuf.Clear();
        }
    }

    private void ProcessCsi(char finalByte)
    {
        if (finalByte != 'm') return; // Only handle SGR (Select Graphic Rendition)

        var paramStr = _paramBuf.ToString();
        if (string.IsNullOrEmpty(paramStr))
        {
            ResetAttributes();
            return;
        }

        var parts = paramStr.Split(';');
        var i = 0;
        while (i < parts.Length)
        {
            if (!int.TryParse(parts[i], out var code))
            {
                i++;
                continue;
            }

            switch (code)
            {
                case 0: ResetAttributes(); break;
                case 1: _bold = true; break;
                case 4: _underline = true; break;
                case 22: _bold = false; break;
                case 24: _underline = false; break;

                // Foreground colors (30-37, 90-97)
                case >= 30 and <= 37:
                    _currentFg = AnsiColors[code - 30];
                    break;
                case >= 90 and <= 97:
                    _currentFg = AnsiColors[code - 90 + 8];
                    break;
                case 39:
                    _currentFg = DefaultFg;
                    break;

                // Background colors (40-47, 100-107)
                case >= 40 and <= 47:
                    _currentBg = AnsiColors[code - 40];
                    break;
                case >= 100 and <= 107:
                    _currentBg = AnsiColors[code - 100 + 8];
                    break;
                case 49:
                    _currentBg = DefaultBg;
                    break;

                // 256-color mode: 38;5;n (fg), 48;5;n (bg)
                case 38:
                    if (i + 2 < parts.Length && parts[i + 1] == "5" &&
                        int.TryParse(parts[i + 2], out var fg256))
                    {
                        _currentFg = Color256(fg256);
                        i += 2;
                    }
                    break;
                case 48:
                    if (i + 2 < parts.Length && parts[i + 1] == "5" &&
                        int.TryParse(parts[i + 2], out var bg256))
                    {
                        _currentBg = Color256(bg256);
                        i += 2;
                    }
                    break;
            }
            i++;
        }
    }

    private void ResetAttributes()
    {
        _currentFg = DefaultFg;
        _currentBg = DefaultBg;
        _bold = false;
        _underline = false;
    }

    private static Color Color256(int n)
    {
        if (n < 16) return AnsiColors[n];
        if (n < 232)
        {
            // 6x6x6 color cube
            n -= 16;
            var b = n % 6;
            n /= 6;
            var g = n % 6;
            var r = n / 6;
            return Color.FromRgb(
                (byte)(r == 0 ? 0 : 55 + 40 * r),
                (byte)(g == 0 ? 0 : 55 + 40 * g),
                (byte)(b == 0 ? 0 : 55 + 40 * b));
        }
        // Grayscale ramp 232-255
        var gray = (byte)(8 + (n - 232) * 10);
        return Color.FromRgb(gray, gray, gray);
    }
}
