using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace QuotaBlocks;

/// <summary>
/// The panel that opens above the bar: every window with its remaining
/// percentage and reset time, where the numbers came from, and the app actions.
/// </summary>
public sealed class DetailsForm : LayeredForm
{
    private const float PadX = 13f;
    private const float PadY = 13f;
    private const float LineHeight = 24f;
    private const float HeadingHeight = 31f;
    private const float ActionHeight = 31f;
    private const float SeparatorHeight = 11f;
    private const float ColumnGap = 12f;
    private const float CornerRadius = 10f;
    private const float IconSize = 15f;
    private const float HeadingFontSize = 16f;
    private const float QuotaFontSize = 15f;
    private const float ActionFontSize = 15f;
    private const float NoteFontSize = 13.5f;
    private const float MinWidth = 390f;

    private readonly OverlayForm owner;
    private readonly List<Item> items = [];
    // Held in a field so the callback is not collected while the hook is live.
    private Native.HookProc? mouseProc;
    private IntPtr mouseHook;
    private int hoverIndex = -1;

    public DetailsForm(OverlayForm owner) : base(clickThroughFocus: false)
    {
        this.owner = owner;
        TopMost = true;
        KeyPreview = true;
        Build();
    }

    private abstract record Item
    {
        public sealed record Heading(string Text) : Item;

        public sealed record Section(string Text) : Item;

        public sealed record Line(string Label, string Value, string? Detail) : Item;

        public sealed record ResetCredit(string Label, string ExpiresAt) : Item;

        public sealed record Note(string Text) : Item;

        public sealed record Separator : Item;

        public sealed record Action(string Text, bool? Checked, System.Action Run) : Item;
    }

    public void Rebuild()
    {
        Build();
        var size = MeasureContent();
        Bounds = new Rectangle(Left, Bottom - size.Height, size.Width, size.Height);
        Redraw();
    }

    public void ShowNear(Rectangle bar)
    {
        var size = MeasureContent();
        var screen = Screen.FromRectangle(bar);
        var scale = DpiScale;

        var left = Math.Min(bar.Left, screen.Bounds.Right - size.Width - (int)(8 * scale));
        var top = Math.Max(screen.Bounds.Top + (int)(8 * scale), bar.Top - size.Height - (int)(8 * scale));

        Bounds = new Rectangle(left, top, size.Width, size.Height);
        Show();
        Redraw();
        BringToFront(activate: true);

        TakeForeground();
        Activate();

        // Deactivate alone is not enough to dismiss the panel: taking the
        // foreground can be refused, and clicks on the desktop or the taskbar do
        // not always produce one. Watching the mouse directly closes it on any
        // click outside, whatever the focus situation is.
        mouseProc = OnGlobalMouse;
        mouseHook = Native.SetWindowsHookEx(
            Native.WH_MOUSE_LL, mouseProc, Native.GetModuleHandle(null), 0);

        Log.Write($"panel shown at {Bounds}, foreground={Native.GetForegroundWindow() == Handle}");
    }

    private IntPtr OnGlobalMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        // Nothing may escape here. An exception thrown inside a native callback
        // does not reach Application.ThreadException; the kernel terminates the
        // process outright with STATUS_FATAL_USER_CALLBACK_EXCEPTION. A click
        // that arrives while the panel is being torn down would otherwise take
        // the whole app down with it, because both the Bounds read and the
        // BeginInvoke throw once the form is disposed.
        try
        {
            if (code >= 0 && IsButtonDown(wParam) && !IsDisposed && IsHandleCreated)
            {
                var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                if (!Bounds.Contains(data.Point.X, data.Point.Y))
                {
                    // Never close from inside the hook: it runs ahead of the
                    // click being delivered, and tearing the window down here
                    // would stall input for every process on the machine.
                    BeginInvoke(Close);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"mouse hook failed: {e}");
        }

        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool IsButtonDown(IntPtr message) =>
        (int)message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN;

    private void ReleaseMouseHook()
    {
        if (mouseHook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
        mouseProc = null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ReleaseMouseHook();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        // Belt and braces: the hook must be gone before the form is, on every
        // path, or a later click reaches a callback whose form no longer exists.
        if (disposing) ReleaseMouseHook();
        base.Dispose(disposing);
    }

    private float DpiScale => DeviceDpi / 96f;

    private static float S(float value, float scale) => value * scale;

    private void Build()
    {
        items.Clear();

        AddProvider("ChatGPT / Codex", owner.CodexState);
        items.Add(new Item.Separator());

        items.Add(new Item.Action(Loc.T("切换为 English", "切换为中文"), null, () =>
        {
            AppSettings.Language = Loc.IsChinese ? AppLanguage.English : AppLanguage.Chinese;
            owner.ApplyLanguageChange();
            Rebuild();
        }));
        items.Add(new Item.Action(Loc.T("开机自动启动", "Launch at login"), AppSettings.LaunchAtLogin, () =>
        {
            AppSettings.LaunchAtLogin = !AppSettings.LaunchAtLogin;
            Rebuild();
        }));
        items.Add(new Item.Action(Loc.T("打开 Codex 额度页面", "Open Codex usage"), null,
            () => Open("https://chatgpt.com/codex/settings/usage")));
        items.Add(new Item.Action(Loc.T("退出", "Quit"), null, Application.Exit));
    }

    private void AddProvider(string title, QuotaState state)
    {
        items.Add(new Item.Heading(title));

        switch (state)
        {
            case QuotaState.Available available:
            {
                var snapshot = available.Snapshot;
                if (snapshot.Session is { } session)
                {
                    items.Add(Line(Loc.T("5 小时", "5-hour"), session));
                }
                items.Add(Line(Loc.T("周额度", "Weekly"), snapshot.Weekly));
                if (snapshot.Extra is { } extra)
                {
                    items.Add(Line(snapshot.ExtraLabel ?? "Extra", extra));
                }
                if (snapshot.AvailableResetCredits.Count > 0)
                {
                    items.Add(new Item.Separator());
                    items.Add(new Item.Section(Loc.T("可用重置机会", "Available reset credits")));
                    items.Add(new Item.Note(Loc.T("完全重置（每周 + 5 小时）", "Full reset (Weekly + 5-hour)")));
                    for (var i = 0; i < snapshot.AvailableResetCredits.Count; i++)
                    {
                        var credit = snapshot.AvailableResetCredits[i];
                        items.Add(new Item.ResetCredit(
                            Loc.T($"重置机会 {i + 1}", $"Reset credit {i + 1}"),
                            Loc.T($"到期 {Loc.ResetDate(credit.ExpiresAt)}", $"expires {Loc.ResetDate(credit.ExpiresAt)}")));
                    }
                }
                break;
            }
            case QuotaState.Unavailable unavailable:
                items.Add(new Item.Note(unavailable.Message));
                break;
            default:
                items.Add(new Item.Note(Loc.T("正在读取…", "Loading…")));
                break;
        }
    }

    private static Item.Line Line(string label, QuotaWindow window) => new(
        label,
        Loc.T($"剩余 {window.RemainingPercent}%", $"{window.RemainingPercent}% left"),
        window.ResetsAt is { } reset ? Loc.T($"重置 {Loc.ResetDate(reset)}", $"resets {Loc.ResetDate(reset)}") : null);

    protected override Size MeasureContent()
    {
        var scale = DpiScale;
        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var headingFont = Fonts.Ui(S(HeadingFontSize, scale), FontStyle.Bold);
        using var quotaFont = Fonts.Ui(S(QuotaFontSize, scale));
        using var actionFont = Fonts.Ui(S(ActionFontSize, scale));
        using var noteFont = Fonts.Ui(S(NoteFontSize, scale));

        float quotaLeftWidth = 0, quotaRightWidth = 0, fullRowWidth = 0;
        var height = S(PadY, scale) * 2;

        foreach (var item in items)
        {
            switch (item)
            {
                case Item.Heading heading:
                    fullRowWidth = Math.Max(fullRowWidth,
                        S(IconSize + 6f, scale) + TextWidth(g, heading.Text, headingFont));
                    height += S(HeadingHeight, scale);
                    break;
                case Item.Section section:
                    fullRowWidth = Math.Max(fullRowWidth, TextWidth(g, section.Text, quotaFont));
                    height += S(LineHeight, scale);
                    break;
                case Item.Line line:
                    quotaLeftWidth = Math.Max(quotaLeftWidth,
                        S(10f, scale) + TextWidth(g, line.Label, quotaFont));
                    quotaRightWidth = Math.Max(quotaRightWidth, TextWidth(g, RightText(line), quotaFont));
                    height += S(LineHeight, scale);
                    break;
                case Item.ResetCredit credit:
                    quotaLeftWidth = Math.Max(quotaLeftWidth, TextWidth(g, credit.Label, quotaFont));
                    quotaRightWidth = Math.Max(quotaRightWidth, TextWidth(g, credit.ExpiresAt, quotaFont));
                    height += S(LineHeight, scale);
                    break;
                case Item.Note note:
                    fullRowWidth = Math.Max(fullRowWidth,
                        S(10f, scale) + TextWidth(g, note.Text, noteFont));
                    height += S(LineHeight, scale);
                    break;
                case Item.Separator:
                    height += S(SeparatorHeight, scale);
                    break;
                case Item.Action action:
                    var checkSpace = action.Checked is null ? 0f : S(24f, scale);
                    fullRowWidth = Math.Max(fullRowWidth,
                        TextWidth(g, action.Text, actionFont) + checkSpace);
                    height += S(ActionHeight, scale);
                    break;
            }
        }

        var quotaRowWidth = quotaLeftWidth
            + (quotaRightWidth > 0 ? S(ColumnGap, scale) + quotaRightWidth : 0);
        var contentWidth = Math.Max(fullRowWidth, quotaRowWidth);
        var width = Math.Max(S(MinWidth, scale), S(PadX, scale) * 2 + contentWidth);
        return new Size((int)Math.Ceiling(width), (int)Math.Ceiling(height));
    }

    private static string RightText(Item.Line line) =>
        line.Detail is null ? line.Value : $"{line.Value}   {line.Detail}";

    private static float TextWidth(Graphics g, string text, Font font) =>
        g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;

    protected override void Render(Graphics g, Size size)
    {
        var theme = Theme.Current;
        var scale = DpiScale;
        var bounds = new RectangleF(0.5f, 0.5f, size.Width - 1f, size.Height - 1f);

        // The panel is a reading surface, so it stays fully opaque; only the bar
        // itself is translucent enough to blend into the taskbar.
        g.FillRounded(bounds, S(CornerRadius, scale), Color.FromArgb(255, theme.Surface));
        g.DrawRounded(bounds, S(CornerRadius, scale), theme.Border, 1f);

        using var headingFont = Fonts.Ui(S(HeadingFontSize, scale), FontStyle.Bold);
        using var quotaFont = Fonts.Ui(S(QuotaFontSize, scale));
        using var actionFont = Fonts.Ui(S(ActionFontSize, scale));
        using var noteFont = Fonts.Ui(S(NoteFontSize, scale));
        using var textBrush = new SolidBrush(theme.Text);
        using var secondaryBrush = new SolidBrush(theme.SecondaryText);

        var y = S(PadY, scale);
        var x = S(PadX, scale);

        for (var i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case Item.Heading heading:
                {
                    var iconSize = S(IconSize, scale);
                    var textHeight = g.MeasureString(heading.Text, headingFont, PointF.Empty, StringFormat.GenericTypographic).Height;
                    DrawIcon(g, new RectangleF(x, y + (S(HeadingHeight, scale) - iconSize) / 2f, iconSize, iconSize));
                    g.DrawString(heading.Text, headingFont, textBrush,
                        x + iconSize + S(6f, scale), y + (S(HeadingHeight, scale) - textHeight) / 2f,
                        StringFormat.GenericTypographic);
                    y += S(HeadingHeight, scale);
                    break;
                }
                case Item.Section section:
                {
                    var lineHeight = S(LineHeight, scale);
                    var textHeight = g.MeasureString(section.Text, quotaFont, PointF.Empty, StringFormat.GenericTypographic).Height;
                    g.DrawString(section.Text, quotaFont, textBrush,
                        x + S(10f, scale), y + (lineHeight - textHeight) / 2f, StringFormat.GenericTypographic);
                    y += lineHeight;
                    break;
                }
                case Item.Line line:
                {
                    var lineHeight = S(LineHeight, scale);
                    var textHeight = g.MeasureString(line.Label, quotaFont, PointF.Empty, StringFormat.GenericTypographic).Height;
                    var top = y + (lineHeight - textHeight) / 2f;
                    g.DrawString(line.Label, quotaFont, secondaryBrush, x + S(10f, scale), top, StringFormat.GenericTypographic);

                    var right = RightText(line);
                    var rightWidth = TextWidth(g, right, quotaFont);
                    g.DrawString(right, quotaFont, textBrush, size.Width - S(PadX, scale) - rightWidth, top, StringFormat.GenericTypographic);
                    y += lineHeight;
                    break;
                }
                case Item.ResetCredit credit:
                {
                    var lineHeight = S(LineHeight, scale);
                    var textHeight = g.MeasureString(credit.Label, quotaFont, PointF.Empty, StringFormat.GenericTypographic).Height;
                    var top = y + (lineHeight - textHeight) / 2f;
                    g.DrawString(credit.Label, quotaFont, textBrush, x + S(10f, scale), top, StringFormat.GenericTypographic);
                    var expiryWidth = TextWidth(g, credit.ExpiresAt, quotaFont);
                    g.DrawString(credit.ExpiresAt, quotaFont, textBrush,
                        size.Width - S(PadX, scale) - expiryWidth, top, StringFormat.GenericTypographic);
                    y += lineHeight;
                    break;
                }
                case Item.Note note:
                {
                    var lineHeight = S(LineHeight, scale);
                    var textHeight = g.MeasureString(note.Text, noteFont, PointF.Empty, StringFormat.GenericTypographic).Height;
                    g.DrawString(note.Text, noteFont, secondaryBrush,
                        x + S(10f, scale), y + (lineHeight - textHeight) / 2f, StringFormat.GenericTypographic);
                    y += lineHeight;
                    break;
                }
                case Item.Separator:
                {
                    var middle = y + S(SeparatorHeight, scale) / 2f;
                    using var pen = new Pen(theme.Divider, 1f);
                    g.DrawLine(pen, x, middle, size.Width - x, middle);
                    y += S(SeparatorHeight, scale);
                    break;
                }
                case Item.Action action:
                {
                    var rowHeight = S(ActionHeight, scale);
                    var rect = new RectangleF(x - S(5f, scale), y, size.Width - 2 * x + S(10f, scale), rowHeight);
                    if (i == hoverIndex) g.FillRounded(rect, S(5f, scale), theme.Hover);

                    var textHeight = g.MeasureString(action.Text, actionFont, PointF.Empty, StringFormat.GenericTypographic).Height;
                    g.DrawString(action.Text, actionFont, textBrush, x, y + (rowHeight - textHeight) / 2f, StringFormat.GenericTypographic);

                    if (action.Checked == true)
                    {
                        DrawCheck(g, theme, new RectangleF(size.Width - S(PadX + 11f, scale), y + rowHeight / 2f - S(4f, scale), S(10f, scale), S(8f, scale)), scale);
                    }
                    y += rowHeight;
                    break;
                }
            }
        }
    }

    private static void DrawCheck(Graphics g, Theme theme, RectangleF rect, float scale)
    {
        using var pen = new Pen(theme.Text, Math.Max(1.4f, S(1.4f, scale))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen,
        [
            new PointF(rect.Left, rect.Top + rect.Height * 0.55f),
            new PointF(rect.Left + rect.Width * 0.35f, rect.Bottom),
            new PointF(rect.Right, rect.Top),
        ]);
    }

    private static void DrawIcon(Graphics g, RectangleF rect)
    {
        var path = SvgPath.Load("chatgpt.svg");
        if (path is null)
        {
            g.FillRounded(rect, rect.Width / 4f, Theme.CodexGreen);
            return;
        }

        using var clone = (GraphicsPath)path.Clone();
        using var transform = new Matrix();
        transform.Translate(rect.X, rect.Y);
        transform.Scale(rect.Width, rect.Height);
        clone.Transform(transform);

        using var brush = new SolidBrush(Theme.CodexGreen);
        g.FillPath(brush, clone);
    }

    /// <summary>Maps a point to an action row, or -1 when it is over static content.</summary>
    private int HitTest(Point point)
    {
        var scale = DpiScale;
        var y = S(PadY, scale);

        for (var i = 0; i < items.Count; i++)
        {
            var height = items[i] switch
            {
                Item.Heading => S(HeadingHeight, scale),
                Item.Section or Item.Line or Item.ResetCredit or Item.Note => S(LineHeight, scale),
                Item.Separator => S(SeparatorHeight, scale),
                _ => S(ActionHeight, scale),
            };
            if (point.Y >= y && point.Y < y + height) return items[i] is Item.Action ? i : -1;
            y += height;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = HitTest(e.Location);
        if (index == hoverIndex) return;
        hoverIndex = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        Redraw();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIndex < 0) return;
        hoverIndex = -1;
        Redraw();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var index = HitTest(e.Location);
        if (index >= 0 && items[index] is Item.Action action) action.Run();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
