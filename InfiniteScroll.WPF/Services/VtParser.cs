using System.Text;
using System.Windows.Media;

namespace InfiniteScroll.Services;

/// <summary>
/// Parses VT100/xterm escape sequences and drives a <see cref="ScreenBuffer"/>.
/// Replaces the old linear-append parser with proper cursor and screen support.
/// </summary>
public class VtParser
{
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

    private readonly ScreenBuffer _screen;

    private enum State { Normal, Escape, Csi, Osc, EscHash, EscSpace, StringSeq }
    private State _state = State.Normal;
    private readonly StringBuilder _paramBuf = new();
    private bool _csiPrivate; // CSI sequence has '?' prefix
    private bool _reversed;   // SGR 7 reverse video active

    /// <summary>
    /// DEC synchronized update (?2026): when true, the terminal should
    /// defer rendering until this becomes false again.
    /// </summary>
    public bool SyncUpdating { get; private set; }

    public VtParser(ScreenBuffer screen)
    {
        _screen = screen;
    }

    public void Parse(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);

        foreach (var ch in text)
        {
            switch (_state)
            {
                case State.Normal:
                    HandleNormal(ch);
                    break;

                case State.Escape:
                    HandleEscape(ch);
                    break;

                case State.Csi:
                    HandleCsi(ch);
                    break;

                case State.Osc:
                    if (ch == '\x07' || ch == '\x1b') // BEL or ST
                        _state = State.Normal;
                    break;

                case State.StringSeq:
                    // DCS / APC / PM strings: absorb until ST (\e\\) or BEL
                    if (ch == '\x1b' || ch == '\x07')
                        _state = State.Normal;
                    break;

                case State.EscHash:
                case State.EscSpace:
                    // Absorb one char and return to normal
                    _state = State.Normal;
                    break;
            }
        }
    }

    private void HandleNormal(char ch)
    {
        switch (ch)
        {
            case '\x1b':
                _state = State.Escape;
                break;
            case '\r':
                _screen.CarriageReturn();
                break;
            case '\n':
            case '\x0b': // VT
            case '\x0c': // FF
                _screen.LineFeed();
                break;
            case '\b':
                _screen.Backspace();
                break;
            case '\t':
                _screen.Tab();
                break;
            case '\x07': // BEL — ignore
                break;
            case '\x0e': // SO — shift out (ignore)
            case '\x0f': // SI — shift in (ignore)
                break;
            default:
                if (ch >= ' ') // printable
                    _screen.Write(ch);
                break;
        }
    }

    private void HandleEscape(char ch)
    {
        switch (ch)
        {
            case '[':
                _state = State.Csi;
                _paramBuf.Clear();
                _csiPrivate = false;
                break;
            case ']':
                _state = State.Osc;
                _paramBuf.Clear();
                break;
            case '#':
                _state = State.EscHash;
                break;
            case ' ':
                _state = State.EscSpace;
                break;
            case 'M': // Reverse Index (scroll down / cursor up)
                _screen.ReverseLineFeed();
                _state = State.Normal;
                break;
            case 'D': // Index (scroll up / cursor down)
                _screen.LineFeed();
                _state = State.Normal;
                break;
            case 'E': // Next Line
                _screen.CarriageReturn();
                _screen.LineFeed();
                _state = State.Normal;
                break;
            case '7': // DECSC — Save cursor
                _savedRow = _screen.CursorRow;
                _savedCol = _screen.CursorCol;
                _state = State.Normal;
                break;
            case '8': // DECRC — Restore cursor
                _screen.SetCursorPosition(_savedRow, _savedCol);
                _state = State.Normal;
                break;
            case 'c': // RIS — Full reset
                _screen.ResetAttributes();
                _screen.ResetScrollRegion();
                _screen.EraseDisplay(2);
                _screen.SetCursorPosition(0, 0);
                _state = State.Normal;
                break;
            case '=': // DECKPAM — keypad application mode (ignore)
            case '>': // DECKPNM — keypad numeric mode (ignore)
            case 'H': // HTS — set tab stop (ignore for now)
                _state = State.Normal;
                break;
            case 'P': // DCS — Device Control String
            case '_': // APC — Application Program Command
            case '^': // PM — Privacy Message
            case 'X': // SOS — Start of String
                _state = State.StringSeq; // absorb until ST
                break;
            case '(':
            case ')':
            case '*':
            case '+':
                // Designate character set — absorb the next char
                _state = State.EscSpace; // reuse: absorb 1 char then return
                break;
            default:
                // Unknown escape — ignore
                _state = State.Normal;
                break;
        }
    }

    private int _savedRow, _savedCol;

    private void HandleCsi(char ch)
    {
        // Check for '?' prefix (private mode)
        if (ch == '?' && _paramBuf.Length == 0)
        {
            _csiPrivate = true;
            return;
        }

        // Parameter / intermediate bytes
        if ((ch >= '0' && ch <= '9') || ch == ';' || ch == ':')
        {
            _paramBuf.Append(ch);
            return;
        }

        // Intermediate bytes like '>' or '!' — absorb
        if (ch >= 0x20 && ch <= 0x2F)
        {
            _paramBuf.Append(ch);
            return;
        }

        // Final byte
        _state = State.Normal;

        var paramStr = _paramBuf.ToString();
        var ps = ParseParams(paramStr);

        if (_csiPrivate)
        {
            HandleCsiPrivate(ch, ps);
            return;
        }

        switch (ch)
        {
            case 'm': // SGR — Select Graphic Rendition
                ProcessSgr(ps);
                break;

            case 'H': // CUP — Cursor Position
            case 'f': // HVP — same as CUP
                _screen.SetCursorPosition(
                    (ps.Length > 0 ? ps[0] : 1) - 1,
                    (ps.Length > 1 ? ps[1] : 1) - 1);
                break;

            case 'A': // CUU — Cursor Up
                _screen.MoveCursorUp(ps.Length > 0 ? ps[0] : 1);
                break;
            case 'B': // CUD — Cursor Down
                _screen.MoveCursorDown(ps.Length > 0 ? ps[0] : 1);
                break;
            case 'C': // CUF — Cursor Forward
                _screen.MoveCursorForward(ps.Length > 0 ? ps[0] : 1);
                break;
            case 'D': // CUB — Cursor Back
                _screen.MoveCursorBack(ps.Length > 0 ? ps[0] : 1);
                break;

            case 'E': // CNL — Cursor Next Line
                _screen.MoveCursorDown(ps.Length > 0 ? ps[0] : 1);
                _screen.CarriageReturn();
                break;
            case 'F': // CPL — Cursor Previous Line
                _screen.MoveCursorUp(ps.Length > 0 ? ps[0] : 1);
                _screen.CarriageReturn();
                break;

            case 'G': // CHA — Cursor Character Absolute
                _screen.CursorCol = Math.Clamp((ps.Length > 0 ? ps[0] : 1) - 1, 0, _screen.Cols - 1);
                break;

            case 'd': // VPA — Line Position Absolute
                _screen.CursorRow = Math.Clamp((ps.Length > 0 ? ps[0] : 1) - 1, 0, _screen.Rows - 1);
                break;

            case 'J': // ED — Erase in Display
                _screen.EraseDisplay(ps.Length > 0 ? ps[0] : 0);
                break;

            case 'K': // EL — Erase in Line
                _screen.EraseLine(ps.Length > 0 ? ps[0] : 0);
                break;

            case 'X': // ECH — Erase Characters
                _screen.EraseCharacters(ps.Length > 0 ? ps[0] : 1);
                break;

            case 'P': // DCH — Delete Characters
                _screen.DeleteCharacters(ps.Length > 0 ? ps[0] : 1);
                break;

            case '@': // ICH — Insert Characters
                _screen.InsertCharacters(ps.Length > 0 ? ps[0] : 1);
                break;

            case 'L': // IL — Insert Lines
                _screen.InsertLines(ps.Length > 0 ? ps[0] : 1);
                break;
            case 'M': // DL — Delete Lines
                _screen.DeleteLines(ps.Length > 0 ? ps[0] : 1);
                break;

            case 'S': // SU — Scroll Up
                _screen.ScrollUp(ps.Length > 0 ? ps[0] : 1);
                break;
            case 'T': // SD — Scroll Down
                _screen.ScrollDown(ps.Length > 0 ? ps[0] : 1);
                break;

            case 'r': // DECSTBM — Set Scroll Region
                var top = (ps.Length > 0 ? ps[0] : 1) - 1;
                var bot = (ps.Length > 1 ? ps[1] : _screen.Rows) - 1;
                _screen.SetScrollRegion(top, bot);
                _screen.SetCursorPosition(0, 0);
                break;

            case 's': // SCP — Save Cursor Position
                _savedRow = _screen.CursorRow;
                _savedCol = _screen.CursorCol;
                break;
            case 'u': // RCP — Restore Cursor Position
                _screen.SetCursorPosition(_savedRow, _savedCol);
                break;

            case 'n': // DSR — Device Status Report
                // We don't send responses back, but apps query this
                break;

            case 'c': // DA — Device Attributes
                // Ignore — would need to write back to PTY
                break;

            case 't': // Window manipulation — mostly ignore
                break;

            case 'b': // REP — Repeat preceding graphic char
                // Ignore for now
                break;

            case 'h': // SM — Set Mode
            case 'l': // RM — Reset Mode
                // Non-private modes — mostly ignore
                break;
        }
    }

    private void HandleCsiPrivate(char ch, int[] ps)
    {
        switch (ch)
        {
            case 'h': // DECSET
                foreach (var p in ps)
                {
                    switch (p)
                    {
                        case 1: break; // DECCKM — application cursor keys (ignore for now)
                        case 25: _screen.CursorVisible = true; break;
                        case 1049: // Alternate screen buffer + save cursor
                            _savedRow = _screen.CursorRow;
                            _savedCol = _screen.CursorCol;
                            _screen.EnterAltScreen();
                            break;
                        case 2004: break; // Bracketed paste — ignore
                        case 1004: break; // Focus events — ignore
                        case 2026: SyncUpdating = true; break; // Synchronized update start
                        case 9001: break; // Win32 input mode — ignore
                    }
                }
                break;

            case 'l': // DECRST
                foreach (var p in ps)
                {
                    switch (p)
                    {
                        case 1: break;
                        case 25: _screen.CursorVisible = false; break;
                        case 1049: // Exit alternate screen + restore cursor
                            _screen.ExitAltScreen();
                            _screen.SetCursorPosition(_savedRow, _savedCol);
                            break;
                        case 2004: break;
                        case 1004: break;
                        case 2026: SyncUpdating = false; break; // Synchronized update end
                        case 9001: break;
                    }
                }
                break;
        }
    }

    private void ProcessSgr(int[] ps)
    {
        if (ps.Length == 0)
        {
            _screen.ResetAttributes();
            return;
        }

        var i = 0;
        while (i < ps.Length)
        {
            var code = ps[i];
            switch (code)
            {
                case 0: _screen.ResetAttributes(); _reversed = false; break;
                case 1: _screen.CurrentBold = true; break;
                case 4: _screen.CurrentUnderline = true; break;
                case 7: // Reverse video
                    if (!_reversed)
                    {
                        (_screen.CurrentFg, _screen.CurrentBg) = (_screen.CurrentBg, _screen.CurrentFg);
                        _reversed = true;
                    }
                    break;
                case 22: _screen.CurrentBold = false; break;
                case 24: _screen.CurrentUnderline = false; break;
                case 27: // Reverse off
                    if (_reversed)
                    {
                        (_screen.CurrentFg, _screen.CurrentBg) = (_screen.CurrentBg, _screen.CurrentFg);
                        _reversed = false;
                    }
                    break;

                // Foreground colors (30-37, 90-97)
                case >= 30 and <= 37:
                    _screen.CurrentFg = AnsiColors[code - 30];
                    break;
                case >= 90 and <= 97:
                    _screen.CurrentFg = AnsiColors[code - 90 + 8];
                    break;
                case 39:
                    _screen.CurrentFg = ScreenCell.DefaultFg;
                    break;

                // Background colors (40-47, 100-107)
                case >= 40 and <= 47:
                    _screen.CurrentBg = AnsiColors[code - 40];
                    break;
                case >= 100 and <= 107:
                    _screen.CurrentBg = AnsiColors[code - 100 + 8];
                    break;
                case 49:
                    _screen.CurrentBg = ScreenCell.DefaultBg;
                    break;

                // 256-color mode: 38;5;n (fg), 48;5;n (bg)
                case 38:
                    if (i + 1 < ps.Length && ps[i + 1] == 5 && i + 2 < ps.Length)
                    {
                        _screen.CurrentFg = Color256(ps[i + 2]);
                        i += 2;
                    }
                    else if (i + 1 < ps.Length && ps[i + 1] == 2 && i + 4 < ps.Length)
                    {
                        // 24-bit: 38;2;r;g;b
                        _screen.CurrentFg = Color.FromRgb(
                            (byte)Math.Clamp(ps[i + 2], 0, 255),
                            (byte)Math.Clamp(ps[i + 3], 0, 255),
                            (byte)Math.Clamp(ps[i + 4], 0, 255));
                        i += 4;
                    }
                    break;
                case 48:
                    if (i + 1 < ps.Length && ps[i + 1] == 5 && i + 2 < ps.Length)
                    {
                        _screen.CurrentBg = Color256(ps[i + 2]);
                        i += 2;
                    }
                    else if (i + 1 < ps.Length && ps[i + 1] == 2 && i + 4 < ps.Length)
                    {
                        _screen.CurrentBg = Color.FromRgb(
                            (byte)Math.Clamp(ps[i + 2], 0, 255),
                            (byte)Math.Clamp(ps[i + 3], 0, 255),
                            (byte)Math.Clamp(ps[i + 4], 0, 255));
                        i += 4;
                    }
                    break;
            }
            i++;
        }
    }

    private static int[] ParseParams(string paramStr)
    {
        if (string.IsNullOrEmpty(paramStr))
            return [];

        // Strip intermediate bytes (> ! etc.)
        var clean = new StringBuilder();
        foreach (var c in paramStr)
        {
            if ((c >= '0' && c <= '9') || c == ';')
                clean.Append(c);
        }

        var str = clean.ToString();
        if (string.IsNullOrEmpty(str))
            return [];

        var parts = str.Split(';');
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            int.TryParse(parts[i], out result[i]);
        }
        return result;
    }

    private static Color Color256(int n)
    {
        if (n < 0) n = 0;
        if (n < 16) return AnsiColors[Math.Min(n, 15)];
        if (n < 232)
        {
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
        var gray = (byte)(8 + Math.Min(n - 232, 23) * 10);
        return Color.FromRgb(gray, gray, gray);
    }
}
