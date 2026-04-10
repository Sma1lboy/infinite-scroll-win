using System.Windows.Media;

namespace InfiniteScroll.Services;

public struct ScreenCell
{
    public char Char;
    public Color Fg;
    public Color Bg;
    public bool Bold;
    public bool Underline;

    public static readonly Color DefaultFg = Color.FromRgb(0xD9, 0xD9, 0xE0);
    public static readonly Color DefaultBg = Color.FromRgb(0x00, 0x00, 0x00);

    public static ScreenCell Empty => new()
    {
        Char = ' ',
        Fg = DefaultFg,
        Bg = DefaultBg,
    };
}

public class ScreenBuffer
{
    private ScreenCell[,] _cells;
    private readonly List<ScreenCell[]> _scrollback = new();
    private const int MaxScrollback = 5000;

    // Alternate screen
    private ScreenCell[,]? _altCells;
    private int _altCursorRow, _altCursorCol;
    private bool _useAltScreen;

    // Scroll region (0-based, inclusive)
    private int _scrollTop;
    private int _scrollBottom;

    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public bool CursorVisible { get; set; } = true;

    // Current text attributes
    public Color CurrentFg { get; set; } = ScreenCell.DefaultFg;
    public Color CurrentBg { get; set; } = ScreenCell.DefaultBg;
    public bool CurrentBold { get; set; }
    public bool CurrentUnderline { get; set; }

    public IReadOnlyList<ScreenCell[]> Scrollback => _scrollback;

    public ScreenBuffer(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        _cells = new ScreenCell[rows, cols];
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        Clear();
    }

    public ScreenCell GetCell(int row, int col)
    {
        if (row >= 0 && row < Rows && col >= 0 && col < Cols)
            return _cells[row, col];
        return ScreenCell.Empty;
    }

    public void Write(char ch)
    {
        if (CursorCol >= Cols)
        {
            // Auto-wrap
            CursorCol = 0;
            LineFeed();
        }

        _cells[CursorRow, CursorCol] = new ScreenCell
        {
            Char = ch,
            Fg = CurrentFg,
            Bg = CurrentBg,
            Bold = CurrentBold,
            Underline = CurrentUnderline,
        };
        CursorCol++;
    }

    public void CarriageReturn() => CursorCol = 0;

    public void LineFeed()
    {
        if (CursorRow == _scrollBottom)
        {
            ScrollUp(1);
        }
        else if (CursorRow < Rows - 1)
        {
            CursorRow++;
        }
    }

    public void ReverseLineFeed()
    {
        if (CursorRow == _scrollTop)
        {
            ScrollDown(1);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
        }
    }

    public void Backspace()
    {
        if (CursorCol > 0)
            CursorCol--;
    }

    public void Tab()
    {
        // Move to next tab stop (every 8 columns)
        CursorCol = Math.Min(Cols - 1, (CursorCol / 8 + 1) * 8);
    }

    public void SetCursorPosition(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorCol = Math.Clamp(col, 0, Cols - 1);
    }

    public void MoveCursorUp(int n) =>
        CursorRow = Math.Max(_scrollTop, CursorRow - Math.Max(1, n));

    public void MoveCursorDown(int n) =>
        CursorRow = Math.Min(_scrollBottom, CursorRow + Math.Max(1, n));

    public void MoveCursorForward(int n) =>
        CursorCol = Math.Min(Cols - 1, CursorCol + Math.Max(1, n));

    public void MoveCursorBack(int n) =>
        CursorCol = Math.Max(0, CursorCol - Math.Max(1, n));

    public void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // Erase below (including cursor)
                EraseLine(0);
                for (int r = CursorRow + 1; r < Rows; r++)
                    EraseRowWithBg(r);
                break;
            case 1: // Erase above (including cursor)
                EraseLine(1);
                for (int r = 0; r < CursorRow; r++)
                    EraseRowWithBg(r);
                break;
            case 2: // Erase all
            case 3: // Erase all + scrollback
                for (int r = 0; r < Rows; r++)
                    EraseRowWithBg(r);
                if (mode == 3)
                    _scrollback.Clear();
                break;
        }
    }

    public void EraseLine(int mode)
    {
        var blank = BlankCell();
        switch (mode)
        {
            case 0: // Erase right (including cursor)
                for (int c = CursorCol; c < Cols; c++)
                    _cells[CursorRow, c] = blank;
                break;
            case 1: // Erase left (including cursor)
                for (int c = 0; c <= CursorCol && c < Cols; c++)
                    _cells[CursorRow, c] = blank;
                break;
            case 2: // Erase whole line
                EraseRowWithBg(CursorRow);
                break;
        }
    }

    public void EraseCharacters(int n)
    {
        var blank = BlankCell();
        for (int c = CursorCol; c < Math.Min(CursorCol + n, Cols); c++)
            _cells[CursorRow, c] = blank;
    }

    /// <summary>
    /// Create a blank cell using the CURRENT background color (per spec,
    /// erase operations fill with the current SGR background).
    /// </summary>
    private ScreenCell BlankCell() => new()
    {
        Char = ' ',
        Fg = CurrentFg,
        Bg = CurrentBg,
    };

    public void DeleteCharacters(int n)
    {
        n = Math.Max(1, n);
        for (int c = CursorCol; c < Cols; c++)
        {
            _cells[CursorRow, c] = c + n < Cols
                ? _cells[CursorRow, c + n]
                : ScreenCell.Empty;
        }
    }

    public void InsertCharacters(int n)
    {
        n = Math.Max(1, n);
        for (int c = Cols - 1; c >= CursorCol; c--)
        {
            _cells[CursorRow, c] = c - n >= CursorCol
                ? _cells[CursorRow, c - n]
                : ScreenCell.Empty;
        }
    }

    public void InsertLines(int n)
    {
        n = Math.Max(1, n);
        for (int r = _scrollBottom; r >= CursorRow + n; r--)
            CopyRow(r - n, r);
        for (int r = CursorRow; r < Math.Min(CursorRow + n, _scrollBottom + 1); r++)
            EraseRow(r);
    }

    public void DeleteLines(int n)
    {
        n = Math.Max(1, n);
        for (int r = CursorRow; r <= _scrollBottom - n; r++)
            CopyRow(r + n, r);
        for (int r = Math.Max(CursorRow, _scrollBottom - n + 1); r <= _scrollBottom; r++)
            EraseRow(r);
    }

    public void ScrollUp(int n)
    {
        for (int i = 0; i < n; i++)
        {
            // Save top line to scrollback (only if scrolling the main screen, not alt)
            if (!_useAltScreen && _scrollTop == 0)
            {
                var line = new ScreenCell[Cols];
                for (int c = 0; c < Cols; c++)
                    line[c] = _cells[_scrollTop, c];
                _scrollback.Add(line);
                if (_scrollback.Count > MaxScrollback)
                    _scrollback.RemoveAt(0);
            }

            for (int r = _scrollTop; r < _scrollBottom; r++)
                CopyRow(r + 1, r);
            EraseRow(_scrollBottom);
        }
    }

    public void ScrollDown(int n)
    {
        for (int i = 0; i < n; i++)
        {
            for (int r = _scrollBottom; r > _scrollTop; r--)
                CopyRow(r - 1, r);
            EraseRow(_scrollTop);
        }
    }

    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top, 0, Rows - 1);
        _scrollBottom = Math.Clamp(bottom, _scrollTop, Rows - 1);
    }

    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
    }

    public void EnterAltScreen()
    {
        if (_useAltScreen) return;
        _useAltScreen = true;
        _altCursorRow = CursorRow;
        _altCursorCol = CursorCol;
        _altCells = _cells;
        _cells = new ScreenCell[Rows, Cols];
        Clear();
        CursorRow = 0;
        CursorCol = 0;
    }

    public void ExitAltScreen()
    {
        if (!_useAltScreen) return;
        _useAltScreen = false;
        if (_altCells != null)
        {
            _cells = _altCells;
            _altCells = null;
        }
        CursorRow = _altCursorRow;
        CursorCol = _altCursorCol;
    }

    public void Resize(int newRows, int newCols)
    {
        var newCells = new ScreenCell[newRows, newCols];
        var copyRows = Math.Min(Rows, newRows);
        var copyCols = Math.Min(Cols, newCols);

        for (int r = 0; r < copyRows; r++)
        for (int c = 0; c < copyCols; c++)
            newCells[r, c] = _cells[r, c];

        // Fill new cells with empty
        for (int r = 0; r < newRows; r++)
        for (int c = copyCols; c < newCols; c++)
            newCells[r, c] = ScreenCell.Empty;
        for (int r = copyRows; r < newRows; r++)
        for (int c = 0; c < newCols; c++)
            newCells[r, c] = ScreenCell.Empty;

        _cells = newCells;
        Rows = newRows;
        Cols = newCols;
        _scrollBottom = newRows - 1;
        CursorRow = Math.Min(CursorRow, Rows - 1);
        CursorCol = Math.Min(CursorCol, Cols - 1);
    }

    public void ResetAttributes()
    {
        CurrentFg = ScreenCell.DefaultFg;
        CurrentBg = ScreenCell.DefaultBg;
        CurrentBold = false;
        CurrentUnderline = false;
    }

    private void Clear()
    {
        for (int r = 0; r < Rows; r++)
            EraseRow(r);
    }

    private void EraseRow(int row)
    {
        for (int c = 0; c < Cols; c++)
            _cells[row, c] = ScreenCell.Empty;
    }

    private void EraseRowWithBg(int row)
    {
        var blank = BlankCell();
        for (int c = 0; c < Cols; c++)
            _cells[row, c] = blank;
    }

    private void CopyRow(int src, int dst)
    {
        for (int c = 0; c < Cols; c++)
            _cells[dst, c] = _cells[src, c];
    }
}
