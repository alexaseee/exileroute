using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Theme
{
    private const string ScrollBarTheme = "DarkMode_Explorer";
    public static readonly Color Background = Color.FromArgb(16, 23, 34);
    public static readonly Color Surface = Color.FromArgb(25, 37, 52);
    public static readonly Color Control = Color.FromArgb(32, 45, 59);
    public static readonly Color Border = Color.FromArgb(78, 97, 115);
    public static readonly Color Divider = Color.FromArgb(51, 67, 83);
    public static readonly Color Text = Color.FromArgb(237, 244, 251);
    public static readonly Color Muted = Color.FromArgb(174, 192, 209);
    public static readonly Color Accent = Color.FromArgb(96, 219, 200);
    public static readonly Color Warning = Color.FromArgb(231, 181, 124);
    public static readonly Color Selected = Color.FromArgb(34, 59, 68);
    private static readonly Color Disabled = Color.FromArgb(123, 145, 165);

    public static void StyleWindow(Form form)
    {
        form.BackColor = Background;
        form.HandleCreated += (sender, args) =>
        {
            if (SystemInformation.HighContrast) return;
            const int captionColorAttribute = 35;
            const int textColorAttribute = 36;
            int caption = ColorTranslator.ToWin32(Background);
            int text = ColorTranslator.ToWin32(Text);
            DwmSetWindowAttribute(form.Handle, captionColorAttribute, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, textColorAttribute, ref text, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string classList);

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr window, ref ComboBoxInfo info);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendTrackBarMessage(IntPtr window, int message, IntPtr wParam, ref NativeRectangle bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public NativeRectangle ItemBounds;
        public NativeRectangle ButtonBounds;
        public int ButtonState;
        public IntPtr ComboWindow;
        public IntPtr ItemWindow;
        public IntPtr ListWindow;
    }

    private static void StyleNativeControl(Control control, string theme)
    {
        EventHandler applyTheme = (sender, args) =>
        {
            if (!SystemInformation.HighContrast) SetWindowTheme(control.Handle, theme, null);
        };
        control.HandleCreated += applyTheme;
        if (control.IsHandleCreated) applyTheme(control, EventArgs.Empty);
    }

    public static void StyleScrollBars(Control control)
    {
        StyleNativeControl(control, ScrollBarTheme);
    }

    public static Label Label(Control parent, string text, int size, Color color, Rectangle bounds, bool bold = false, string english = null)
    {
        var label = new Label { Text = text, ForeColor = color, AutoEllipsis = true,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleLeft, Bounds = bounds };
        if (english != null) UiText.Bind(label, text, english);
        parent.Controls.Add(label);
        return label;
    }

    public static Button Button(Control parent, string text, Rectangle bounds, EventHandler click = null, bool focusable = true, string english = null)
    {
        var button = new SignalButton(focusable) { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9), ForeColor = Text, BackColor = Control, UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
        if (english != null) UiText.Bind(button, text, english);
        button.Click += click;
        parent.Controls.Add(button);
        return button;
    }

    public static Button TreeButton(Control parent, Rectangle bounds, EventHandler click)
    {
        var button = (SignalButton)Button(parent, "", bounds, click, focusable: false);
        button.DrawIcon = DrawTreeIcon;
        return button;
    }

    private static void DrawTreeIcon(Graphics graphics, Rectangle bounds, Color color)
    {
        float scale = Scale(graphics, 16) / 16f;
        float left = bounds.Left + (bounds.Width - 16 * scale) / 2;
        float top = bounds.Top + (bounds.Height - 16 * scale) / 2;
        var outline = new PointF[]
        {
            new PointF(8, 1), new PointF(12.5f, 6), new PointF(10.5f, 6),
            new PointF(14.5f, 11.5f), new PointF(1.5f, 11.5f), new PointF(5.5f, 6), new PointF(3.5f, 6)
        };
        for (int i = 0; i < outline.Length; i++)
            outline[i] = new PointF(left + outline[i].X * scale, top + outline[i].Y * scale);
        SmoothingMode smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(color, 1.4f * scale) { LineJoin = LineJoin.Round, EndCap = LineCap.Round })
        {
            graphics.DrawPolygon(pen, outline);
            graphics.DrawLine(pen, left + 8 * scale, top + 11.5f * scale, left + 8 * scale, top + 15 * scale);
        }
        graphics.SmoothingMode = smoothing;
    }

    public static void SetTabState(Button tab, bool selected)
    {
        ((SignalButton)tab).SelectedTab = selected;
        tab.BackColor = selected ? Selected : Surface;
        tab.ForeColor = selected ? Text : Muted;
        tab.Invalidate();
    }

    public static Panel Panel(Control parent, Color color, Rectangle bounds)
    {
        var panel = new Panel { BackColor = color, Bounds = bounds };
        parent.Controls.Add(panel);
        return panel;
    }

    public static Panel Mark(Control parent, Rectangle bounds)
    {
        var panel = Panel(parent, parent.BackColor, bounds);
        panel.Paint += (sender, args) =>
        {
            Rectangle box = Rectangle.Inflate(panel.ClientRectangle, -1, -1);
            DrawSurface(args.Graphics, box, panel.BackColor, Accent, 3);
            using (var pen = new Pen(Accent, Scale(args.Graphics, 1)))
            {
                float left = box.Left + box.Width * 0.25f;
                float right = box.Left + box.Width * 0.75f;
                args.Graphics.DrawLine(pen, left, box.Top + box.Height * 0.33f, right, box.Top + box.Height * 0.33f);
                args.Graphics.DrawLine(pen, left, box.Top + box.Height * 0.52f, right, box.Top + box.Height * 0.52f);
                args.Graphics.DrawLine(pen, left, box.Top + box.Height * 0.71f, box.Left + box.Width * 0.60f, box.Top + box.Height * 0.71f);
            }
        };
        return panel;
    }

    public static Panel Card()
    {
        return new SignalCard { BackColor = Surface, ForeColor = Text };
    }

    public static void StylePicker(ComboBox picker)
    {
        StyleNativeControl(picker, "DarkMode_CFD");
        picker.DrawMode = DrawMode.OwnerDrawFixed;
        picker.DropDown += (sender, args) =>
        {
            if (SystemInformation.HighContrast) return;
            var info = new ComboBoxInfo { Size = Marshal.SizeOf(typeof(ComboBoxInfo)) };
            if (GetComboBoxInfo(picker.Handle, ref info) && info.ListWindow != IntPtr.Zero)
                SetWindowTheme(info.ListWindow, ScrollBarTheme, null);
        };
        picker.DrawItem += (sender, args) =>
        {
            if (args.Index < 0) return;
            bool popupSelection = (args.State & DrawItemState.Selected) != 0 && (args.State & DrawItemState.ComboBoxEdit) == 0;
            using (var background = new SolidBrush(popupSelection ? Selected : picker.BackColor))
                args.Graphics.FillRectangle(background, args.Bounds);
            Rectangle textBounds = Rectangle.Inflate(args.Bounds, -Scale(args.Graphics, 4), 0);
            TextRenderer.DrawText(args.Graphics, picker.GetItemText(picker.Items[args.Index]), picker.Font, textBounds,
                picker.Enabled ? Text : Disabled, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            args.DrawFocusRectangle();
        };
    }

    public static DataGridView Grid(Rectangle bounds)
    {
        var grid = new DataGridView
        {
            Bounds = bounds, BackgroundColor = Surface, GridColor = Divider, BorderStyle = BorderStyle.FixedSingle,
            EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10), RowTemplate = { Height = 30 }, ColumnHeadersHeight = 27
        };
        foreach (Control child in grid.Controls)
            if (child is ScrollBar) StyleScrollBars(child);
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selected;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Control;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.CellPainting += (sender, args) =>
        {
            if (args.RowIndex < 0 || args.ColumnIndex < 0) return;
            var cell = grid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            if (cell is DataGridViewCheckBoxCell)
            {
                args.Paint(args.ClipBounds, args.PaintParts & ~(DataGridViewPaintParts.ContentBackground | DataGridViewPaintParts.ContentForeground));
                DrawCheck(args.Graphics, args.CellBounds, args.Value is bool && (bool)args.Value, true);
                args.Handled = true;
            }
            else if (cell is DataGridViewButtonCell)
            {
                args.Paint(args.ClipBounds, args.PaintParts & ~(DataGridViewPaintParts.ContentBackground | DataGridViewPaintParts.ContentForeground));
                Rectangle button = Rectangle.Inflate(args.CellBounds, -Scale(args.Graphics, 4), -Scale(args.Graphics, 4));
                PaintButton(args.Graphics, button, args.FormattedValue as string, args.CellStyle.Font, Control, Text, Border, false);
                args.Handled = true;
            }
        };
        return grid;
    }

    public static void SetChecklistRowColor(DataGridViewRow row, bool done)
    {
        Color color = done ? Muted : Text;
        row.DefaultCellStyle.ForeColor = color;
        row.DefaultCellStyle.SelectionForeColor = color;
    }

    public static void DrawCheck(Graphics graphics, Rectangle bounds, bool isChecked, bool enabled)
    {
        int size = Math.Min(Scale(graphics, 16), Math.Min(bounds.Width, bounds.Height) - 2);
        if (size < 2) return;
        var box = new Rectangle(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size);
        Color ink = enabled ? Accent : Disabled;
        DrawSurface(graphics, box, isChecked ? ink : Surface, isChecked ? ink : enabled ? Muted : Border, 3);
        if (!isChecked) return;
        SmoothingMode smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Background, Scale(graphics, 2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            graphics.DrawLines(pen, new[] {
                new PointF(box.X + size * 0.24f, box.Y + size * 0.51f),
                new PointF(box.X + size * 0.43f, box.Y + size * 0.69f),
                new PointF(box.X + size * 0.77f, box.Y + size * 0.31f) });
        }
        graphics.SmoothingMode = smoothing;
    }

    private static void PaintButton(Graphics graphics, Rectangle bounds, string text, Font font, Color fill, Color foreground,
        Color stroke, bool selectedTab)
    {
        DrawSurface(graphics, bounds, fill, stroke, 5);
        if (selectedTab)
        {
            int inset = Scale(graphics, 5);
            int height = Scale(graphics, 2);
            using (var brush = new SolidBrush(Accent))
                graphics.FillRectangle(brush, bounds.Left + inset, bounds.Bottom - height - 1, Math.Max(0, bounds.Width - inset * 2), height);
        }
        TextRenderer.DrawText(graphics, text ?? "", font, Rectangle.Inflate(bounds, -Scale(graphics, 3), 0), foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static int Scale(Graphics graphics, int pixels)
    {
        return Math.Max(1, (int)Math.Round(pixels * graphics.DpiX / 96f));
    }

    private static void DrawSurface(Graphics graphics, Rectangle bounds, Color fill, Color stroke, int radius)
    {
        int width = bounds.Width - 1;
        int height = bounds.Height - 1;
        if (width < 2 || height < 2) return;
        int diameter = Math.Min(Scale(graphics, radius * 2), Math.Min(width, height));
        int right = bounds.Left + width;
        int bottom = bounds.Top + height;
        SmoothingMode smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(stroke))
        {
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
            graphics.DrawPath(pen, path);
        }
        graphics.SmoothingMode = smoothing;
    }

    private sealed class SignalButton : Button
    {
        public bool SelectedTab;
        public Action<Graphics, Rectangle, Color> DrawIcon;

        public SignalButton(bool focusable)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, focusable);
            TabStop = focusable;
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            using (var brush = new SolidBrush(Parent == null ? Background : Parent.BackColor))
                args.Graphics.FillRectangle(brush, args.ClipRectangle);
            bool hovered = Enabled && ClientRectangle.Contains(PointToClient(MousePosition));
            bool keyboardFocus = Enabled && Focused && ShowFocusCues;
            Color fill = Enabled ? BackColor : Surface;
            Color stroke = Enabled ? Border : Divider;
            if (hovered && fill != Accent) fill = Selected;
            if (hovered || keyboardFocus || fill == Accent) stroke = Accent;
            Color foreground = Enabled ? ForeColor : Disabled;
            PaintButton(args.Graphics, ClientRectangle, Text, Font, fill, foreground, stroke, SelectedTab);
            if (DrawIcon != null) DrawIcon(args.Graphics, ClientRectangle, foreground);
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            base.OnMouseEnter(args);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            Invalidate();
        }
    }

    internal sealed class SignalSlider : TrackBar
    {
        public SignalSlider()
        {
            if (!SystemInformation.HighContrast)
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnValueChanged(EventArgs args)
        {
            base.OnValueChanged(args);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            const int getThumb = 0x0419; // TBM_GETTHUMBRECT
            const int getChannel = 0x041A; // TBM_GETCHANNELRECT
            var thumb = new NativeRectangle();
            var channel = new NativeRectangle();
            SendTrackBarMessage(Handle, getThumb, IntPtr.Zero, ref thumb);
            SendTrackBarMessage(Handle, getChannel, IntPtr.Zero, ref channel);

            int x = (thumb.Left + thumb.Right) / 2;
            int y = (thumb.Top + thumb.Bottom) / 2;
            int railHeight = Theme.Scale(args.Graphics, 5);
            int knobSize = Theme.Scale(args.Graphics, 13);
            DrawSurface(args.Graphics, new Rectangle(channel.Left, y - railHeight / 2, channel.Right - channel.Left, railHeight),
                Divider, Border, 2);
            DrawSurface(args.Graphics, new Rectangle(x - knobSize / 2, y - knobSize / 2, knobSize, knobSize),
                Accent, Focused && ShowFocusCues ? Theme.Text : Accent, 7);
        }
    }

    private sealed class SignalCard : Panel
    {
        public SignalCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            using (var brush = new SolidBrush(Parent == null ? Background : Parent.BackColor))
                args.Graphics.FillRectangle(brush, args.ClipRectangle);
            DrawSurface(args.Graphics, ClientRectangle, BackColor, Border, 6);
        }
    }
}
