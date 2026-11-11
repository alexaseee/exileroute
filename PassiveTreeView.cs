using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

internal sealed class PassiveTreeView : Panel
{
    private readonly ComboBox buildPicker;
    private readonly ComboBox presets;
    private readonly PassiveTreeCanvas canvas;
    private readonly Label status;
    private readonly Label warning;
    private readonly Button zoomOut;
    private readonly Button zoomIn;
    private readonly Button fit;
    private readonly Dictionary<string, PassiveTreeData> trees = new Dictionary<string, PassiveTreeData>();
    private CancellationTokenSource pending;
    private string link;
    private PassivePreset selected;

    public PassiveTreeView(Action<int> selectBuild)
    {
        Size = new Size(348, 244);
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Theme.Label(this, "Билд", 9, Theme.Muted, new Rectangle(0, 0, 58, 26), english: "Build");
        buildPicker = AddPicker(new Rectangle(62, 0, 286, 26));
        buildPicker.Enabled = false;
        buildPicker.SelectionChangeCommitted += (sender, args) =>
        {
            if (buildPicker.SelectedIndex >= 0) selectBuild(buildPicker.SelectedIndex);
        };
        Theme.Label(this, "Пресет", 9, Theme.Muted, new Rectangle(0, 32, 58, 26), english: "Preset");
        presets = AddPicker(new Rectangle(62, 32, 161, 26));
        presets.SelectedIndexChanged += (sender, args) => SelectPreset(presets.SelectedItem as PassivePreset);
        zoomOut = Theme.Button(this, "−", new Rectangle(230, 32, 28, 26), (sender, args) => canvas.Zoom(1 / 1.25f), focusable: false);
        zoomIn = Theme.Button(this, "+", new Rectangle(262, 32, 28, 26), (sender, args) => canvas.Zoom(1.25f), focusable: false);
        fit = Theme.Button(this, "Всё", new Rectangle(294, 32, 54, 26), (sender, args) => canvas.Fit(), focusable: false, english: "Fit");
        zoomOut.Anchor = zoomIn.Anchor = fit.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        canvas = new PassiveTreeCanvas { Bounds = new Rectangle(0, 65, 348, 179),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        Controls.Add(canvas);
        status = Theme.Label(this, "", 10, Theme.Muted, canvas.Bounds);
        status.BackColor = Theme.Surface;
        status.Padding = new Padding(12);
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Anchor = canvas.Anchor;
        warning = Theme.Label(this, "Часть узлов отсутствует в данных дерева", 8, Theme.Warning, new Rectangle(0, 221, 348, 23),
            english: "Some nodes are missing from the tree data");
        warning.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        warning.BackColor = Theme.Surface;
        warning.Padding = new Padding(8, 0, 8, 0);
        warning.BringToFront();
        status.BringToFront();
        RefreshLanguage();
        ShowEmpty();
    }

    public void RefreshLanguage()
    {
        buildPicker.AccessibleName = UiText.Get("Активный билд", "Active build");
        presets.AccessibleName = UiText.Get("Пресет дерева", "Tree preset");
        zoomOut.AccessibleName = UiText.Get("Уменьшить масштаб", "Zoom out");
        zoomIn.AccessibleName = UiText.Get("Увеличить масштаб", "Zoom in");
        fit.AccessibleName = UiText.Get("Показать всё дерево", "Fit the whole tree");
        canvas.AccessibleName = UiText.Get("Дерево пассивных умений", "Passive skill tree");
        canvas.ClearHover();
    }

    public void ClearHover() { canvas.ClearHover(); }

    private ComboBox AddPicker(Rectangle bounds)
    {
        var picker = new ComboBox { Bounds = bounds, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10), BackColor = Theme.Surface, ForeColor = Theme.Text,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownWidth = 286 };
        Theme.StylePicker(picker);
        Controls.Add(picker);
        return picker;
    }

    public void SetBuilds(List<SavedPassiveBuild> builds, int activeIndex, bool reload)
    {
        if (!buildPicker.Items.Cast<SavedPassiveBuild>().SequenceEqual(builds))
        {
            buildPicker.BeginUpdate();
            buildPicker.Items.Clear();
            buildPicker.Items.AddRange(builds.ToArray());
            buildPicker.EndUpdate();
        }
        buildPicker.Enabled = builds.Count > 0;
        buildPicker.SelectedIndex = builds.Count == 0 ? -1 : activeIndex;
        var selectedBuild = (SavedPassiveBuild)buildPicker.SelectedItem;
        SetLink(selectedBuild == null ? null : selectedBuild.Link, reload);
    }

    private async void SetLink(string value, bool reload)
    {
        if (!reload && link == value) return;
        link = value;
        CancelRequest();
        presets.Items.Clear();
        selected = null;
        canvas.SetTree(null, null);
        if (String.IsNullOrEmpty(link))
        {
            ShowEmpty();
            return;
        }

        ShowStatus("Загрузка билда…", "Loading build…");
        using (var request = BeginRequest())
        {
            try
            {
                PassivePreset[] build = await Task.Run(() => PassiveBuild.Load(value, request.Token), request.Token);
                if (request.IsCancellationRequested) return;
                presets.Items.AddRange(build);
                presets.Enabled = true;
                presets.SelectedIndex = 0;
            }
            catch (Exception error)
            {
                if (!request.IsCancellationRequested) ShowError(error, false);
            }
            finally { EndRequest(request); }
        }
    }

    private async void SelectPreset(PassivePreset preset)
    {
        CancelRequest();
        selected = preset;
        if (preset == null) return;
        PassiveTreeData tree;
        if (!trees.TryGetValue(preset.Version, out tree))
        {
            canvas.SetTree(null, null);
            ShowStatus("Загрузка дерева…", "Loading tree…");
            using (var request = BeginRequest())
            {
                try
                {
                    tree = await Task.Run(() => PassiveBuild.LoadTree(preset.Version, request.Token), request.Token);
                    if (request.IsCancellationRequested) return;
                    trees[preset.Version] = tree;
                }
                catch (Exception error)
                {
                    if (!request.IsCancellationRequested) ShowError(error, true);
                    return;
                }
                finally { EndRequest(request); }
            }
        }
        if (selected != preset) return;
        int missing = canvas.SetTree(tree, preset);
        status.Visible = false;
        warning.Visible = missing > 0;
        zoomOut.Enabled = zoomIn.Enabled = fit.Enabled = true;
    }

    private CancellationTokenSource BeginRequest()
    {
        pending = new CancellationTokenSource();
        return pending;
    }

    private void EndRequest(CancellationTokenSource request)
    {
        if (pending == request) pending = null;
    }

    private void CancelRequest()
    {
        var request = pending;
        pending = null;
        if (request != null) request.Cancel();
    }

    private void ShowEmpty()
    {
        ShowStatus("Добавьте билд pobb.in в редакторе на вкладке «Пассивки».",
            "Add a pobb.in build in the editor’s Passives tab.");
    }

    private void ShowError(Exception error, bool tree)
    {
        var web = error as WebException;
        var response = web == null ? null : web.Response as HttpWebResponse;
        bool missing = response != null && response.StatusCode == HttpStatusCode.NotFound;
        if (response != null) response.Dispose();
        if (error is NotSupportedException || (tree && missing))
        {
            if (tree)
                ShowStatus("Нет данных для дерева " + selected.Version + ".", "No tree data for " + selected.Version + ".", true);
            else
                ShowStatus("Нужен билд для Path of Exile 1.", "Use a Path of Exile 1 build.", true);
        }
        else if (missing)
            ShowStatus("Билд не найден на pobb.in. Проверьте ссылку.", "Build not found on pobb.in. Check the link.", true);
        else if (web != null)
            ShowStatus("Не удалось загрузить данные. Повторите загрузку в редакторе.",
                "Could not download data. Retry from the editor.", true);
        else if (tree)
            ShowStatus("Не удалось прочитать дерево для этого пресета.", "Could not read this preset’s tree.", true);
        else
            ShowStatus("Не удалось прочитать билд. Проверьте ссылку.", "Could not read the build. Check the link.", true);
    }

    private void ShowStatus(string russian, string english, bool error = false)
    {
        presets.Enabled = presets.Items.Count > 0;
        zoomOut.Enabled = zoomIn.Enabled = fit.Enabled = false;
        warning.Visible = false;
        status.ForeColor = error ? Theme.Warning : Theme.Muted;
        UiText.Bind(status, russian, english);
        status.Visible = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CancelRequest();
        base.Dispose(disposing);
    }
}

internal sealed class PassiveTreeCanvas : Control
{
    private readonly ToolTip tooltip = new ToolTip { ShowAlways = true, UseAnimation = false, UseFading = false };
    private PassiveTreeData tree;
    private PassivePreset preset;
    private readonly List<PassiveNode> nodes = new List<PassiveNode>();
    private readonly List<PassiveEdge> edges = new List<PassiveEdge>();
    private readonly HashSet<int> active = new HashSet<int>();
    private RectangleF treeBounds;
    private PointF center;
    private float zoom = 1;
    private Point dragPoint;
    private bool dragging;
    private PassiveNode hovered;

    public PassiveTreeCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = false;
        BackColor = Theme.Surface;
    }

    public int SetTree(PassiveTreeData data, PassivePreset spec)
    {
        bool newTree = tree != data;
        ClearHover();
        tree = data;
        preset = spec;
        nodes.Clear();
        edges.Clear();
        active.Clear();
        if (tree == null) { Invalidate(); return 0; }

        var classes = new HashSet<string>(StringComparer.Ordinal);
        string[] ascendancies;
        int ascendancy = preset.AscendancyId - 1;
        if (tree.Ascendancies.TryGetValue(preset.ClassId, out ascendancies) && ascendancy >= 0 && ascendancy < ascendancies.Length)
            classes.Add(ascendancies[ascendancy]);
        if (!String.IsNullOrEmpty(preset.SecondaryAscendancy))
        {
            int secondary;
            if (Int32.TryParse(preset.SecondaryAscendancy, out secondary) && secondary > 0 && secondary <= tree.SecondaryAscendancies.Length)
                classes.Add(tree.SecondaryAscendancies[secondary - 1]);
            else
                classes.Add(preset.SecondaryAscendancy);
        }
        foreach (int id in preset.Allocated)
        {
            PassiveNode node;
            if (tree.Nodes.TryGetValue(id, out node) && node.Ascendancy != null) classes.Add(node.Ascendancy);
        }
        active.UnionWith(preset.Allocated);
        var visibleNodes = tree.Nodes.Values.Where(node => node.Ascendancy == null || classes.Contains(node.Ascendancy)).ToArray();
        foreach (PassiveNode node in visibleNodes)
            if (node.ClassId == preset.ClassId || node.AscendancyStart) active.Add(node.Id);
        nodes.AddRange(visibleNodes.OrderBy(node => active.Contains(node.Id)));
        edges.AddRange(tree.Edges.Where(edge => edge.From.Ascendancy == null || classes.Contains(edge.From.Ascendancy))
            .OrderBy(edge => active.Contains(edge.From.Id) && active.Contains(edge.To.Id)));
        treeBounds = RectangleF.FromLTRB(nodes.Min(node => node.Position.X), nodes.Min(node => node.Position.Y),
            nodes.Max(node => node.Position.X), nodes.Max(node => node.Position.Y));
        if (newTree) FocusPreset();
        else Invalidate();
        return preset.Allocated.Count(id => !tree.Nodes.ContainsKey(id));
    }

    public void Fit()
    {
        zoom = 1;
        center = new PointF(treeBounds.Left + treeBounds.Width / 2, treeBounds.Top + treeBounds.Height / 2);
        ClearHover();
        Invalidate();
    }

    private void FocusPreset()
    {
        Fit();
        var path = nodes.Where(node => node.Ascendancy == null && active.Contains(node.Id)).ToArray();
        if (path.Length == 0) return;
        float left = path.Min(node => node.Position.X), right = path.Max(node => node.Position.X);
        float top = path.Min(node => node.Position.Y), bottom = path.Max(node => node.Position.Y);
        float magnification = Math.Min(treeBounds.Width / (right - left + 4500), treeBounds.Height / (bottom - top + 4500));
        if (magnification <= 1.1f) return;
        zoom = Math.Min(2.5f, magnification);
        center = new PointF((left + right) / 2, (top + bottom) / 2);
    }

    public void Zoom(float factor) { ZoomAt(factor, new Point(Width / 2, Height / 2)); }

    private float ViewScale
    {
        get
        {
            return zoom * Math.Min(Math.Max(1, Width - 22) / Math.Max(1, treeBounds.Width),
                Math.Max(1, Height - 22) / Math.Max(1, treeBounds.Height));
        }
    }

    private void ZoomAt(float factor, Point anchor)
    {
        if (tree == null) return;
        float oldScale = ViewScale;
        zoom = Math.Max(1, Math.Min(24, zoom * factor));
        if (zoom == 1) { Fit(); return; }
        float offset = 1 / oldScale - 1 / ViewScale;
        center.X += (anchor.X - Width / 2f) * offset;
        center.Y += (anchor.Y - Height / 2f) * offset;
        KeepTreeInView();
        ClearHover();
        Invalidate();
    }

    private void KeepTreeInView()
    {
        center.X = Math.Max(treeBounds.Left, Math.Min(treeBounds.Right, center.X));
        center.Y = Math.Max(treeBounds.Top, Math.Min(treeBounds.Bottom, center.Y));
    }

    private PointF ToScreen(PointF position, float scale)
    {
        return new PointF(Width / 2f + (position.X - center.X) * scale, Height / 2f + (position.Y - center.Y) * scale);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        if (tree == null) return;
        Graphics graphics = args.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = ViewScale;
        float dpi = graphics.DpiX / 96f;
        using (var line = new Pen(Theme.Border, 0.8f * dpi))
        using (var chosenLine = new Pen(Theme.Accent, 1.4f * dpi))
        using (var idle = new SolidBrush(Color.FromArgb(112, 135, 157)))
        using (var chosen = new SolidBrush(Theme.Accent))
        {
            foreach (PassiveEdge edge in edges)
            {
                PointF from = ToScreen(edge.From.Position, scale);
                PointF to = ToScreen(edge.To.Position, scale);
                bool allocated = active.Contains(edge.From.Id) && active.Contains(edge.To.Id);
                DrawEdge(graphics, allocated ? chosenLine : line, edge, from, to, scale);
            }
            foreach (PassiveNode node in nodes)
            {
                PointF point = ToScreen(node.Position, scale);
                if (point.X < -12 || point.Y < -12 || point.X > Width + 12 || point.Y > Height + 12) continue;
                bool allocated = active.Contains(node.Id);
                DrawNode(graphics, node, point, NodeRadius(node, dpi), allocated ? chosen : idle,
                    allocated ? chosenLine : line);
            }
        }
    }

    private void DrawEdge(Graphics graphics, Pen pen, PassiveEdge edge, PointF from, PointF to, float scale)
    {
        if (edge.From.Group == edge.To.Group && edge.From.Orbit == edge.To.Orbit && edge.From.Radius > 0)
        {
            float radius = edge.From.Radius * scale;
            PointF point = ToScreen(edge.From.Center, scale);
            var bounds = new RectangleF(point.X - radius, point.Y - radius, 2 * radius, 2 * radius);
            if (!bounds.IntersectsWith(ClientRectangle)) return;
            float sweep = (edge.To.Angle - edge.From.Angle + 540) % 360 - 180;
            if (sweep != 0) graphics.DrawArc(pen, bounds, edge.From.Angle - 90, sweep);
        }
        else if (!((from.X < 0 && to.X < 0) || (from.Y < 0 && to.Y < 0) ||
                   (from.X > Width && to.X > Width) || (from.Y > Height && to.Y > Height)))
            graphics.DrawLine(pen, from, to);
    }

    private float NodeRadius(PassiveNode node, float dpi)
    {
        float radius = Math.Min(3.7f, 0.75f + zoom * 0.25f);
        return (radius + (node.Kind == PassiveNodeKind.Small ? 0 : node.Kind == PassiveNodeKind.Start ? 1.8f : 0.65f)) * dpi;
    }

    private void DrawNode(Graphics graphics, PassiveNode node, PointF point, float radius, Brush brush, Pen pen)
    {
        float diameter = 2 * radius;
        var bounds = new RectangleF(point.X - radius, point.Y - radius, diameter, diameter);
        switch (node.Kind)
        {
            case PassiveNodeKind.Keystone:
            case PassiveNodeKind.Mastery:
                graphics.FillPolygon(brush, new[] { new PointF(point.X, bounds.Top), new PointF(bounds.Right, point.Y),
                    new PointF(point.X, bounds.Bottom), new PointF(bounds.Left, point.Y) });
                break;
            case PassiveNodeKind.Jewel:
                graphics.FillRectangle(brush, bounds);
                break;
            default:
                graphics.FillEllipse(brush, bounds);
                break;
        }
        if (node.Kind == PassiveNodeKind.Start || hovered == node)
        {
            bounds.Inflate(2, 2);
            graphics.DrawEllipse(pen, bounds);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs args)
    {
        base.OnMouseWheel(args);
        ZoomAt((float)Math.Pow(1.25, args.Delta / 120f), args.Location);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button != MouseButtons.Left || tree == null || zoom == 1) return;
        ClearHover();
        dragPoint = args.Location;
        dragging = true;
        Cursor = Cursors.SizeAll;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (tree == null) return;
        if (dragging)
        {
            if ((args.Button & MouseButtons.Left) == 0) { StopDrag(); return; }
            float scale = ViewScale;
            center.X += (dragPoint.X - args.X) / scale;
            center.Y += (dragPoint.Y - args.Y) / scale;
            dragPoint = args.Location;
            KeepTreeInView();
            Invalidate();
        }
        else
            HoverAt(args.Location);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button == MouseButtons.Left && dragging) StopDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (!Capture && dragging) StopDrag();
    }

    private void StopDrag()
    {
        dragging = false;
        Cursor = Cursors.Default;
        Capture = false;
    }

    protected override void OnMouseLeave(EventArgs args)
    {
        base.OnMouseLeave(args);
        ClearHover();
    }

    public void ClearHover()
    {
        if (hovered == null) return;
        tooltip.Hide(this);
        hovered = null;
        Invalidate();
    }

    private void HoverAt(Point cursor)
    {
        float scale = ViewScale;
        float distance = 49;
        PassiveNode nearest = null;
        foreach (PassiveNode node in nodes)
        {
            PointF point = ToScreen(node.Position, scale);
            float dx = point.X - cursor.X, dy = point.Y - cursor.Y;
            float squared = dx * dx + dy * dy;
            if (squared >= distance) continue;
            distance = squared;
            nearest = node;
        }
        if (hovered == nearest) return;
        ClearHover();
        hovered = nearest;
        if (nearest == null) return;
        PassiveDescription description;
        if (!preset.Overrides.TryGetValue(nearest.Id, out description)) description = nearest.Description;
        IEnumerable<string> stats = description.Stats;
        if (nearest.Masteries != null)
        {
            int effect;
            string[] chosen;
            if (preset.Masteries.TryGetValue(nearest.Id, out effect) && nearest.Masteries.TryGetValue(effect, out chosen))
                stats = chosen;
            else if (nearest.Masteries.Count > 0)
                stats = nearest.Masteries.Values.Select(lines => "• " + String.Join(" ", lines));
        }
        string details = String.Join(Environment.NewLine, stats);
        tooltip.ToolTipTitle = details.Length == 0 ? "" : description.Name;
        if (details.Length == 0) details = description.Name;
        tooltip.Show(details, this, cursor.X + 12, cursor.Y + 18);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tooltip.Dispose();
        base.Dispose(disposing);
    }
}
