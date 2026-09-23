using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;

namespace ProjectLauncher.Desktop.Controls;

public sealed class TerminalControl : FrameworkElement
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(TerminalControl), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(TerminalControl), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TerminalScreen Screen { get; } = new(100, 30);
    public event Action<string>? Input;
    public event Action<int, int>? TerminalResized;
    public event Action<string>? Error;
    public Func<string, string>? PathQuoter { get; set; }
    private const double PaddingSize = 12;
    private double _fontSize = 14, _cellWidth = 8.6, _cellHeight = 21;
    private readonly Typeface _face = new(new FontFamily("Cascadia Mono,Consolas,Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Dictionary<(string, int, bool, double), FormattedText> _glyphs = new();
    private readonly Dictionary<int, SolidColorBrush> _brushes = new();
    private readonly DispatcherTimer _blink;
    private bool _cursorLit = true;
    private int _scrollOffset;
    private (int X, int Y)? _selectionStart, _selectionEnd;
    public TerminalControl()
    {
        Focusable = true; Cursor = Cursors.IBeam; AllowDrop = true; ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "TerminalBackground"); SetResourceReference(ForegroundProperty, "TerminalForeground");
        InputMethod.SetIsInputMethodEnabled(this, true);
        Screen.ResponseRequested += text => Send(text);
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blink.Tick += (_, _) => { _cursorLit = !_cursorLit; InvalidateVisual(); };
        Loaded += (_, _) => { UpdateMetrics(); _blink.Start(); };
        Unloaded += (_, _) => _blink.Stop();
        SizeChanged += (_, _) => UpdateMetrics();
        GotKeyboardFocus += (_, _) => { _cursorLit = true; InvalidateVisual(); };
        LostKeyboardFocus += (_, _) => InvalidateVisual();
        var menu = new System.Windows.Controls.ContextMenu();
        AddMenu(menu, "复制选中内容 / 当前屏幕", Copy);
        AddMenu(menu, "粘贴", Paste);
        AddMenu(menu, "回到最新输出", () => { _scrollOffset = 0; InvalidateVisual(); });
        System.Windows.Controls.ContextMenuService.SetContextMenu(this, menu);
    }
    private static void AddMenu(System.Windows.Controls.ContextMenu menu, string text, Action action)
    { var item = new System.Windows.Controls.MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    public void Feed(string text)
    {
        int oldHistory = Screen.HistoryCount; Screen.Feed(text);
        if (_scrollOffset > 0) _scrollOffset = Math.Clamp(_scrollOffset + Screen.HistoryCount - oldHistory, 0, Screen.HistoryCount);
        InvalidateVisual();
    }
    public void SetFontSize(double value) { _fontSize = Math.Clamp(value, 10, 24); _glyphs.Clear(); UpdateMetrics(); }
    public void ResetScreen() { Screen.Reset(); _scrollOffset = 0; _selectionStart = _selectionEnd = null; InvalidateVisual(); }
    private void UpdateMetrics()
    {
        var sample = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, _fontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _cellWidth = Math.Max(5, sample.WidthIncludingTrailingWhitespace); _cellHeight = Math.Ceiling(_fontSize * 1.5);
        if (ActualWidth < 40 || ActualHeight < 40) return;
        int columns = Math.Clamp((int)((ActualWidth - PaddingSize * 2) / _cellWidth), 2, 500);
        int rows = Math.Clamp((int)((ActualHeight - PaddingSize * 2) / _cellHeight), 2, 300);
        if (columns != Screen.Columns || rows != Screen.Rows) { Screen.Resize(columns, rows); TerminalResized?.Invoke(columns, rows); }
        _glyphs.Clear(); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Background, null, new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        dc.PushClip(new RectangleGeometry(new Rect(PaddingSize, PaddingSize, Math.Max(0, ActualWidth - PaddingSize * 2), Math.Max(0, ActualHeight - PaddingSize * 2))));
        var lines = Screen.ViewLines(_scrollOffset);
        int defaultFg = BrushRgb(Foreground, 0xDCE3F2), defaultBg = BrushRgb(Background, 0x0C0F16);
        for (int y = 0; y < Math.Min(Screen.Rows, lines.Count); y++)
        {
            var row = lines[y];
            for (int x = 0; x < Math.Min(Screen.Columns, row.Length); x++)
            {
                var cell = row[x]; int fg = cell.Foreground < 0 ? defaultFg : cell.Foreground; int bg = cell.Background < 0 ? defaultBg : cell.Background;
                if (cell.Inverse) (fg, bg) = (bg, fg);
                var rect = new Rect(PaddingSize + x * _cellWidth, PaddingSize + y * _cellHeight, _cellWidth + 0.1, _cellHeight);
                if (bg != defaultBg) dc.DrawRectangle(BrushFor(bg), null, rect);
                if (IsSelected(x, y)) { dc.DrawRectangle(BrushFor(0x484876), null, rect); fg = 0xFFFFFF; }
                if (cell.Continuation || string.IsNullOrEmpty(cell.Text) || cell.Text == " ") continue;
                var glyph = Glyph(cell.Text, fg, cell.Bold);
                dc.DrawText(glyph, new Point(rect.X, rect.Y + (_cellHeight - glyph.Height) / 2));
                if (cell.Underline) dc.DrawLine(new Pen(BrushFor(fg), 1), new Point(rect.X, rect.Bottom - 2), new Point(rect.Right, rect.Bottom - 2));
            }
        }
        if (_scrollOffset == 0 && Screen.CursorVisible)
        {
            var cursor = new Rect(PaddingSize + Screen.CursorX * _cellWidth, PaddingSize + Screen.CursorY * _cellHeight, _cellWidth, _cellHeight);
            if (IsKeyboardFocusWithin && _cursorLit) dc.DrawRectangle(BrushFor(0xA19AFF), null, new Rect(cursor.X, cursor.Bottom - 2, cursor.Width, 2));
            else if (!IsKeyboardFocusWithin) dc.DrawRectangle(null, new Pen(BrushFor(0x646C83), 1), cursor);
        }
        dc.Pop();
    }
    private FormattedText Glyph(string text, int color, bool bold)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var key = (text, color, bold, dpi);
        if (_glyphs.TryGetValue(key, out var result)) return result;
        if (_glyphs.Count > 6000) _glyphs.Clear();
        var face = bold ? new Typeface(_face.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : _face;
        result = new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, _fontSize, BrushFor(color), dpi);
        _glyphs[key] = result; return result;
    }
    private SolidColorBrush BrushFor(int rgb)
    {
        if (_brushes.TryGetValue(rgb, out var brush)) return brush;
        if (_brushes.Count > 4096) _brushes.Clear();
        brush = new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); brush.Freeze(); _brushes[rgb] = brush; return brush;
    }
    private static int BrushRgb(Brush brush, int fallback) => brush is SolidColorBrush b ? b.Color.R << 16 | b.Color.G << 8 | b.Color.B : fallback;
    private (int X, int Y) CellAt(Point point) => (Math.Clamp((int)((point.X - PaddingSize) / _cellWidth), 0, Screen.Columns - 1), Math.Clamp((int)((point.Y - PaddingSize) / _cellHeight), 0, Screen.Rows - 1));
    private bool IsSelected(int x, int y)
    {
        if (_selectionStart is not { } start || _selectionEnd is not { } end) return false;
        int a = start.Y * Screen.Columns + start.X, b = end.Y * Screen.Columns + end.X, n = y * Screen.Columns + x;
        return n >= Math.Min(a, b) && n <= Math.Max(a, b);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); _selectionStart = _selectionEnd = CellAt(e.GetPosition(this)); CaptureMouse(); e.Handled = true; InvalidateVisual(); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) { _selectionEnd = CellAt(e.GetPosition(this)); InvalidateVisual(); } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (IsMouseCaptured) ReleaseMouseCapture(); if (_selectionStart == _selectionEnd) _selectionStart = _selectionEnd = null; InvalidateVisual(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) SetFontSize(_fontSize + (e.Delta > 0 ? 1 : -1));
        else { _scrollOffset = Math.Clamp(_scrollOffset + (e.Delta > 0 ? 3 : -3), 0, Screen.HistoryCount); _selectionStart = _selectionEnd = null; InvalidateVisual(); }
        e.Handled = true;
    }
    public void Copy()
    {
        try
        {
            var lines = Screen.ViewLines(_scrollOffset); var parts = new List<string>();
            for (int y = 0; y < lines.Count; y++)
            {
                var line = new StringBuilder(); bool any = false;
                for (int x = 0; x < Math.Min(lines[y].Length, Screen.Columns); x++) if (_selectionStart is null || IsSelected(x, y)) { any = true; if (!lines[y][x].Continuation) line.Append(lines[y][x].Text); }
                if (any) parts.Add(line.ToString().TrimEnd());
            }
            Clipboard.SetText(string.Join(Environment.NewLine, parts).TrimEnd());
        }
        catch (Exception e) { Error?.Invoke(e.Message); }
    }
    public void Paste()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            string text = Clipboard.GetText();
            if (text.Length > 1_048_576) throw new ConfigException("粘贴内容超过 1 MB。");
            if (text.Contains('\n') || text.Contains('\r'))
                if (!Dialogs.Confirm(Window.GetWindow(this), "粘贴多行终端输入？", "多行文本可能包含可立即执行的命令。请确认剪贴板内容可信。", "粘贴")) return;
            text = new string(text.Where(c => c is '\r' or '\n' or '\t' || c >= ' ' && c != '\u007f').ToArray());
            text = text.Replace("\r\n", "\n");
            Send(Screen.BracketedPaste ? "\u001b[200~" + text + "\u001b[201~" : text.Replace('\n', '\r'));
        }
        catch (Exception e) { Error?.Invoke(e.Message); }
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var modifiers = Keyboard.Modifiers; var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool control = modifiers.HasFlag(ModifierKeys.Control), shift = modifiers.HasFlag(ModifierKeys.Shift), alt = modifiers.HasFlag(ModifierKeys.Alt);
        if (control && shift && key == Key.C) { Copy(); e.Handled = true; return; }
        if (control && shift && key == Key.V || shift && key == Key.Insert) { Paste(); e.Handled = true; return; }
        if (control && key == Key.C && _selectionStart is not null) { Copy(); e.Handled = true; return; }
        if (shift && (key is Key.PageUp or Key.PageDown))
        { _scrollOffset = Math.Clamp(_scrollOffset + (key == Key.PageUp ? Screen.Rows - 1 : 1 - Screen.Rows), 0, Screen.HistoryCount); e.Handled = true; InvalidateVisual(); return; }
        string? sequence = null;
        if (control && (int)key >= (int)Key.A && (int)key <= (int)Key.Z) sequence = ((char)((int)key - (int)Key.A + 1)).ToString();
        else if (control && key == Key.Space) sequence = "\0";
        else
        {
            string arrowPrefix = Screen.ApplicationCursor ? "\u001bO" : "\u001b[";
            sequence = key switch
            {
                Key.Enter => "\r", Key.Back => control ? "\u0017" : "\u007f", Key.Tab => shift ? "\u001b[Z" : "\t", Key.Escape => "\u001b",
                Key.Up => arrowPrefix + "A", Key.Down => arrowPrefix + "B", Key.Right => control ? "\u001b[1;5C" : arrowPrefix + "C", Key.Left => control ? "\u001b[1;5D" : arrowPrefix + "D",
                Key.Home => "\u001b[H", Key.End => "\u001b[F", Key.Insert => "\u001b[2~", Key.Delete => "\u001b[3~", Key.PageUp => "\u001b[5~", Key.PageDown => "\u001b[6~",
                Key.F1 => "\u001bOP", Key.F2 => "\u001bOQ", Key.F3 => "\u001bOR", Key.F4 => "\u001bOS", Key.F5 => "\u001b[15~", Key.F6 => "\u001b[17~", Key.F7 => "\u001b[18~", Key.F8 => "\u001b[19~", Key.F9 => "\u001b[20~", Key.F10 => "\u001b[21~", Key.F11 => "\u001b[23~", Key.F12 => "\u001b[24~",
                _ => null
            };
        }
        if (sequence is not null) { Send(alt ? "\u001b" + sequence : sequence); e.Handled = true; }
    }
    protected override void OnTextInput(TextCompositionEventArgs e) { base.OnTextInput(e); if (e.Text.Length > 0) { Send(e.Text); e.Handled = true; } }
    private void Send(string text)
    {
        _scrollOffset = 0; _selectionStart = _selectionEnd = null; _cursorLit = true;
        try { Input?.Invoke(text); } catch (Exception e) { Error?.Invoke(e.Message); }
        InvalidateVisual();
    }
    protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Send(string.Join(" ", files.Select(f => PathQuoter?.Invoke(f) ?? WindowsArguments.Quote(f))) + " ");
        Focus(); e.Handled = true;
    }
}
