using System.Globalization;
using System.Text;

namespace ProjectLauncher.Core;

public readonly record struct TerminalCell(string Text, int Foreground = -1, int Background = -1,
    bool Bold = false, bool Underline = false, bool Inverse = false, bool Continuation = false)
{
    public static TerminalCell Empty => new(" ");
}

/// <summary>
/// Bounded VT screen model, independent of WPF and ConPTY. Implements the common VT/ANSI subset.
/// No OSC clipboard, hyperlink launch, file transfer, or shell execution is performed here.
/// </summary>
public sealed class TerminalScreen
{
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool ApplicationCursor { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool AlternateScreen { get; private set; }
    public int HistoryCount => AlternateScreen ? 0 : _history.Count;
    public string Title { get; private set; } = "";
    public event Action<string>? ResponseRequested;
    private TerminalCell[][] _screen;
    private readonly List<TerminalCell[]> _history = [];
    private readonly int _historyLimit;
    private int _foreground = -1, _background = -1, _top, _bottom;
    private bool _bold, _underline, _inverse, _origin, _wrap = true, _wrapPending;
    private int _savedX, _savedY;
    private int _mode; // 0=text, 1=ESC, 2=CSI, 3=OSC, 4=OSC ESC, 5=DCS, 6=DCS ESC, 7=charset
    private readonly StringBuilder _sequence = new();
    private char? _highSurrogate;
    private MainState? _main;
    private sealed record MainState(TerminalCell[][] Cells, int X, int Y, int Top, int Bottom, int Fg, int Bg, bool Bold, bool Underline, bool Inverse, bool Origin);

    public TerminalScreen(int columns = 100, int rows = 30, int historyLimit = 2000)
    {
        Columns = Math.Clamp(columns, 2, 500); Rows = Math.Clamp(rows, 2, 300); _historyLimit = Math.Clamp(historyLimit, 0, 10000);
        _screen = NewGrid(Columns, Rows); _bottom = Rows - 1;
    }
    private static TerminalCell[][] NewGrid(int columns, int rows) => Enumerable.Range(0, rows).Select(_ => Enumerable.Repeat(TerminalCell.Empty, columns).ToArray()).ToArray();
    private TerminalCell Blank => new(" ", _foreground, _background, _bold, _underline, _inverse);
    public TerminalCell GetCell(int x, int y) => _screen[Math.Clamp(y, 0, Rows - 1)][Math.Clamp(x, 0, Columns - 1)];

    public void Feed(string text)
    {
        foreach (var c in text)
        {
            if (_mode != 0) { EscapeCharacter(c); continue; }
            if (c == '\u001b') { FlushSurrogate(); _mode = 1; continue; }
            if (c < ' ' || c == '\u007f')
            {
                FlushSurrogate();
                switch (c)
                {
                    case '\r': CursorX = 0; _wrapPending = false; break;
                    case '\n': case '\v': case '\f': LineFeed(); break;
                    case '\b': CursorX = Math.Max(0, CursorX - 1); _wrapPending = false; break;
                    case '\t': CursorX = Math.Min(Columns - 1, (CursorX / 8 + 1) * 8); _wrapPending = false; break;
                }
                continue;
            }
            if (char.IsHighSurrogate(c)) { FlushSurrogate(); _highSurrogate = c; continue; }
            if (char.IsLowSurrogate(c) && _highSurrogate is { } high)
            {
                Put(new Rune(high, c)); _highSurrogate = null; continue;
            }
            FlushSurrogate();
            Put(char.IsSurrogate(c) ? Rune.ReplacementChar : new Rune(c));
        }
    }
    private void FlushSurrogate() { if (_highSurrogate is not null) { Put(Rune.ReplacementChar); _highSurrogate = null; } }

    private void EscapeCharacter(char c)
    {
        if (_mode == 1)
        {
            _sequence.Clear();
            switch (c)
            {
                case '[': _mode = 2; return;
                case ']': _mode = 3; return;
                case 'P': case '^': case '_': _mode = 5; return;
                case '(': case ')': case '*': case '+': _mode = 7; return;
                case '7': _savedX = CursorX; _savedY = CursorY; break;
                case '8': SetCursor(_savedX, _savedY); break;
                case 'D': LineFeed(); break;
                case 'E': CursorX = 0; LineFeed(); break;
                case 'M': ReverseIndex(); break;
                case 'c': Reset(); break;
            }
            _mode = 0; return;
        }
        if (_mode == 7) { _mode = 0; return; } // Charset selection; Unicode renderer keeps Unicode glyphs.
        if (_mode == 2)
        {
            if (c is >= '@' and <= '~') { DispatchCsi(_sequence.ToString(), c); _mode = 0; _sequence.Clear(); return; }
            if (c == '\u001b') { _mode = 1; _sequence.Clear(); return; }
            if (_sequence.Length < 4096) _sequence.Append(c); else { _mode = 0; _sequence.Clear(); }
            return;
        }
        if (_mode == 3)
        {
            if (c == '\a') { EndOsc(); return; }
            if (c == '\u001b') { _mode = 4; return; }
            if (_sequence.Length < 4096) _sequence.Append(c);
            return;
        }
        if (_mode == 4)
        {
            if (c == '\\') EndOsc(); else _mode = c == '\u001b' ? 4 : 3;
            return;
        }
        if (_mode == 5) { if (c == '\u001b') _mode = 6; return; }
        if (_mode == 6) _mode = c == '\\' ? 0 : c == '\u001b' ? 6 : 5;
    }
    private void EndOsc()
    {
        var text = _sequence.ToString(); var split = text.IndexOf(';');
        if (split > 0 && text[..split] is "0" or "2") Title = text[(split + 1)..];
        _sequence.Clear(); _mode = 0;
    }

    private void DispatchCsi(string sequence, char command)
    {
        bool privateMode = sequence.StartsWith('?');
        bool secondary = sequence.StartsWith('>');
        var clean = sequence.TrimStart('?', '>').TrimEnd(' ', '!', '"');
        var numbers = clean.Split(';').Select(s => int.TryParse(s, out var n) ? Math.Clamp(n, 0, 1_000_000) : 0).ToArray();
        int At(int index, int fallback = 1) => index >= numbers.Length || numbers[index] == 0 ? fallback : numbers[index];
        int n0 = numbers.Length > 0 ? numbers[0] : 0;
        int n = Math.Min(10000, At(0));
        switch (command)
        {
            case 'A': SetCursor(CursorX, Math.Max(_origin ? _top : 0, CursorY - n)); break;
            case 'B': case 'e': SetCursor(CursorX, Math.Min(_origin ? _bottom : Rows - 1, CursorY + n)); break;
            case 'C': case 'a': SetCursor(CursorX + n, CursorY); break;
            case 'D': SetCursor(CursorX - n, CursorY); break;
            case 'E': SetCursor(0, CursorY + n); break;
            case 'F': SetCursor(0, CursorY - n); break;
            case 'G': case '`': SetCursor(At(0) - 1, CursorY); break;
            case 'd': SetCursor(CursorX, At(0) - 1 + (_origin ? _top : 0)); break;
            case 'H': case 'f': SetCursor(At(1) - 1, Math.Min(_origin ? _bottom : Rows - 1, At(0) - 1 + (_origin ? _top : 0))); break;
            case 'J': EraseDisplay(n0); break;
            case 'K': EraseLine(n0); break;
            case 'm': Sgr(numbers); break;
            case 's': _savedX = CursorX; _savedY = CursorY; break;
            case 'u': SetCursor(_savedX, _savedY); break;
            case 'r':
                int top = Math.Clamp(At(0) - 1, 0, Rows - 1), bottom = Math.Clamp(At(1, Rows) - 1, 0, Rows - 1);
                if (top < bottom) { _top = top; _bottom = bottom; SetCursor(0, _origin ? _top : 0); }
                break;
            case 'S': ScrollUp(Math.Min(n, Rows)); break;
            case 'T': ScrollDown(Math.Min(n, Rows)); break;
            case 'L': InsertLines(Math.Min(n, Rows)); break;
            case 'M': DeleteLines(Math.Min(n, Rows)); break;
            case '@': InsertCells(Math.Min(n, Columns)); break;
            case 'P': DeleteCells(Math.Min(n, Columns)); break;
            case 'X': Array.Fill(_screen[CursorY], Blank, CursorX, Math.Min(n, Columns - CursorX)); break;
            case 'h': case 'l':
                bool enabled = command == 'h';
                if (privateMode)
                {
                    foreach (var mode in numbers)
                    {
                        switch (mode)
                        {
                            case 1: ApplicationCursor = enabled; break;
                            case 6: _origin = enabled; SetCursor(0, enabled ? _top : 0); break;
                            case 7: _wrap = enabled; _wrapPending = false; break;
                            case 25: CursorVisible = enabled; break;
                            case 47: case 1047: case 1049: SetAlternate(enabled); break;
                            case 2004: BracketedPaste = enabled; break;
                        }
                    }
                }
                break;
            case 'n':
                if (n0 == 6) ResponseRequested?.Invoke($"\u001b[{CursorY + 1};{CursorX + 1}R");
                else if (n0 == 5) ResponseRequested?.Invoke("\u001b[0n");
                break;
            case 'c': ResponseRequested?.Invoke(secondary ? "\u001b[>0;1;0c" : "\u001b[?1;2c"); break;
        }
    }

    private void Put(Rune rune)
    {
        int width = CharacterWidth(rune);
        if (width == 0)
        {
            int x = _wrapPending ? CursorX : CursorX - 1;
            if (x >= 0)
            {
                if (_screen[CursorY][x].Continuation && x > 0) x--;
                var cell = _screen[CursorY][x];
                if (cell.Text.Length < 32) _screen[CursorY][x] = cell with { Text = cell.Text + rune.ToString() };
            }
            return;
        }
        if (_wrapPending || width == 2 && CursorX == Columns - 1)
        {
            if (_wrap) { CursorX = 0; LineFeed(); }
            _wrapPending = false;
        }
        if (_screen[CursorY][CursorX].Continuation && CursorX > 0) _screen[CursorY][CursorX - 1] = Blank;
        if (CursorX + 1 < Columns && _screen[CursorY][CursorX + 1].Continuation) _screen[CursorY][CursorX + 1] = Blank;
        _screen[CursorY][CursorX] = new(rune.ToString(), _foreground, _background, _bold, _underline, _inverse);
        if (width == 2 && CursorX + 1 < Columns) _screen[CursorY][CursorX + 1] = new("", _foreground, _background, _bold, _underline, _inverse, true);
        if (CursorX + width >= Columns) { CursorX = Columns - 1; _wrapPending = _wrap; }
        else CursorX += width;
    }

    public static int CharacterWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        int v = rune.Value;
        if (v is >= 0x1100 and <= 0x115f or >= 0x2329 and <= 0x232a or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3
            or >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe19 or >= 0xfe30 and <= 0xfe6f or >= 0xff00 and <= 0xff60
            or >= 0xffe0 and <= 0xffe6 or >= 0x1f300 and <= 0x1faff or >= 0x20000 and <= 0x3fffd) return 2;
        return 1;
    }

    private void SetCursor(int x, int y) { CursorX = Math.Clamp(x, 0, Columns - 1); CursorY = Math.Clamp(y, 0, Rows - 1); _wrapPending = false; }
    private void LineFeed() { _wrapPending = false; if (CursorY == _bottom) ScrollUp(1); else CursorY = Math.Min(Rows - 1, CursorY + 1); }
    private void ReverseIndex() { _wrapPending = false; if (CursorY == _top) ScrollDown(1); else CursorY = Math.Max(0, CursorY - 1); }
    private void ScrollUp(int count)
    {
        for (int i = 0; i < Math.Min(count, _bottom - _top + 1); i++)
        {
            if (!AlternateScreen && _top == 0 && _bottom == Rows - 1 && _historyLimit > 0)
            {
                _history.Add((TerminalCell[])_screen[_top].Clone());
                if (_history.Count > _historyLimit) _history.RemoveAt(0);
            }
            for (int y = _top; y < _bottom; y++) _screen[y] = _screen[y + 1];
            _screen[_bottom] = Enumerable.Repeat(Blank, Columns).ToArray();
        }
    }
    private void ScrollDown(int count)
    {
        for (int i = 0; i < Math.Min(count, _bottom - _top + 1); i++)
        {
            for (int y = _bottom; y > _top; y--) _screen[y] = _screen[y - 1];
            _screen[_top] = Enumerable.Repeat(Blank, Columns).ToArray();
        }
    }
    private void InsertLines(int count)
    {
        if (CursorY < _top || CursorY > _bottom) return;
        int old = _top; _top = CursorY; ScrollDown(count); _top = old;
    }
    private void DeleteLines(int count)
    {
        if (CursorY < _top || CursorY > _bottom) return;
        int old = _top; _top = CursorY; ScrollUp(count); _top = old;
    }
    private void InsertCells(int count)
    {
        var row = _screen[CursorY]; int n = Math.Min(count, Columns - CursorX);
        Array.Copy(row, CursorX, row, CursorX + n, Columns - CursorX - n); Array.Fill(row, Blank, CursorX, n);
    }
    private void DeleteCells(int count)
    {
        var row = _screen[CursorY]; int n = Math.Min(count, Columns - CursorX);
        Array.Copy(row, CursorX + n, row, CursorX, Columns - CursorX - n); Array.Fill(row, Blank, Columns - n, n);
    }
    private void EraseLine(int mode)
    {
        if (mode == 0) Array.Fill(_screen[CursorY], Blank, CursorX, Columns - CursorX);
        else if (mode == 1) Array.Fill(_screen[CursorY], Blank, 0, CursorX + 1);
        else if (mode == 2) Array.Fill(_screen[CursorY], Blank);
    }
    private void EraseDisplay(int mode)
    {
        if (mode is 2 or 3) { foreach (var line in _screen) Array.Fill(line, Blank); if (mode == 3) _history.Clear(); }
        else if (mode == 0) { EraseLine(0); for (int y = CursorY + 1; y < Rows; y++) Array.Fill(_screen[y], Blank); }
        else if (mode == 1) { EraseLine(1); for (int y = 0; y < CursorY; y++) Array.Fill(_screen[y], Blank); }
    }

    private static readonly int[] Palette = [0x0C0C0C, 0xCD3131, 0x0DBC79, 0xE5E510, 0x2472C8, 0xBC3FBC, 0x11A8CD, 0xE5E5E5,
        0x666666, 0xF14C4C, 0x23D18B, 0xF5F543, 0x3B8EEA, 0xD670D6, 0x29B8DB, 0xFFFFFF];
    private static int IndexedColor(int index)
    {
        index = Math.Clamp(index, 0, 255); if (index < 16) return Palette[index];
        if (index >= 232) { int g = 8 + (index - 232) * 10; return g << 16 | g << 8 | g; }
        index -= 16;
        int Component(int x) => x == 0 ? 0 : 55 + 40 * x;
        return Component(index / 36) << 16 | Component(index / 6 % 6) << 8 | Component(index % 6);
    }
    private void Sgr(int[] codes)
    {
        for (int i = 0; i < codes.Length; i++)
        {
            int c = codes[i];
            switch (c)
            {
                case 0: _foreground = _background = -1; _bold = _underline = _inverse = false; break;
                case 1: _bold = true; break;
                case 4: _underline = true; break;
                case 7: _inverse = true; break;
                case 22: _bold = false; break;
                case 24: _underline = false; break;
                case 27: _inverse = false; break;
                case 39: _foreground = -1; break;
                case 49: _background = -1; break;
                case >= 30 and <= 37: _foreground = Palette[c - 30]; break;
                case >= 40 and <= 47: _background = Palette[c - 40]; break;
                case >= 90 and <= 97: _foreground = Palette[c - 90 + 8]; break;
                case >= 100 and <= 107: _background = Palette[c - 100 + 8]; break;
                case 38: case 48:
                    int color = -1;
                    if (i + 2 < codes.Length && codes[i + 1] == 5) { color = IndexedColor(codes[i + 2]); i += 2; }
                    else if (i + 4 < codes.Length && codes[i + 1] == 2)
                    { color = Math.Clamp(codes[i + 2], 0, 255) << 16 | Math.Clamp(codes[i + 3], 0, 255) << 8 | Math.Clamp(codes[i + 4], 0, 255); i += 4; }
                    if (color >= 0) { if (c == 38) _foreground = color; else _background = color; }
                    break;
            }
        }
    }

    private void SetAlternate(bool enabled)
    {
        if (enabled == AlternateScreen) return;
        if (enabled)
        {
            _main = new(_screen, CursorX, CursorY, _top, _bottom, _foreground, _background, _bold, _underline, _inverse, _origin);
            _screen = NewGrid(Columns, Rows); _top = 0; _bottom = Rows - 1; SetCursor(0, 0);
        }
        else if (_main is { } main)
        {
            _screen = ResizeGrid(main.Cells, Columns, Rows); _top = Math.Min(main.Top, Rows - 1); _bottom = Math.Min(main.Bottom, Rows - 1);
            _foreground = main.Fg; _background = main.Bg; _bold = main.Bold; _underline = main.Underline; _inverse = main.Inverse; _origin = main.Origin;
            SetCursor(main.X, main.Y); _main = null;
        }
        AlternateScreen = enabled;
    }
    public void Resize(int columns, int rows)
    {
        columns = Math.Clamp(columns, 2, 500); rows = Math.Clamp(rows, 2, 300);
        if (columns == Columns && rows == Rows) return;
        _screen = ResizeGrid(_screen, columns, rows); Columns = columns; Rows = rows;
        _top = 0; _bottom = rows - 1; SetCursor(CursorX, CursorY);
    }
    private static TerminalCell[][] ResizeGrid(TerminalCell[][] old, int cols, int rows)
    {
        var next = NewGrid(cols, rows);
        for (int y = 0; y < Math.Min(rows, old.Length); y++) Array.Copy(old[y], next[y], Math.Min(cols, old[y].Length));
        return next;
    }
    public IReadOnlyList<TerminalCell[]> ViewLines(int scrollOffset = 0)
    {
        int history = HistoryCount; scrollOffset = Math.Clamp(scrollOffset, 0, history);
        int start = history - scrollOffset;
        var result = new List<TerminalCell[]>(Rows);
        for (int y = 0; y < Rows; y++)
        {
            int index = start + y;
            result.Add(index < history ? _history[index] : _screen[Math.Min(Rows - 1, index - history)]);
        }
        return result;
    }
    public string PlainText(bool includeHistory = false)
    {
        IEnumerable<TerminalCell[]> lines = includeHistory && !AlternateScreen ? _history.Concat(_screen) : _screen;
        return string.Join("\n", lines.Select(LineText));
    }
    public static string LineText(TerminalCell[] line) => string.Concat(line.Where(c => !c.Continuation).Select(c => c.Text)).TrimEnd();
    public void Reset()
    {
        _screen = NewGrid(Columns, Rows); _history.Clear(); _main = null; _foreground = _background = -1;
        _bold = _underline = _inverse = _origin = _wrapPending = false; _wrap = true;
        _top = 0; _bottom = Rows - 1; CursorX = CursorY = 0; CursorVisible = true;
        ApplicationCursor = BracketedPaste = AlternateScreen = false; _mode = 0; _sequence.Clear();
    }
}
