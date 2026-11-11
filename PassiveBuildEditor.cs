using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class PassiveBuildEditor : Panel
{
    private readonly Action<int> openBuild;
    private readonly Action changed;
    private readonly DataGridView grid;
    private readonly Label empty;
    private readonly Button open;
    private readonly Button edit;
    private readonly Button delete;
    private OverlayProfile profile;

    public PassiveBuildEditor(Action<int> openBuild, Action changed)
    {
        this.openBuild = openBuild;
        this.changed = changed;
        BackColor = Theme.Background;
        Size = new Size(632, 205);

        open = Theme.Button(this, "Загрузить", new Rectangle(214, 53, 100, 30),
            (sender, args) => OpenSelectedBuild(), english: "Load build");
        edit = Theme.Button(this, "Изменить", new Rectangle(322, 53, 94, 30),
            (sender, args) => EditBuild(SelectedIndex), english: "Edit");
        delete = Theme.Button(this, "Удалить", new Rectangle(424, 53, 82, 30),
            (sender, args) => DeleteBuild(), english: "Delete");
        var add = Theme.Button(this, "+ Добавить", new Rectangle(514, 53, 104, 30),
            (sender, args) => EditBuild(-1), english: "+ Add");
        add.BackColor = Theme.Accent;
        add.ForeColor = Theme.Background;
        open.Anchor = edit.Anchor = delete.Anchor = add.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        grid = Theme.Grid(new Rectangle(14, 92, 604, 100));
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.Columns.Add(new DataGridViewTextBoxColumn { FillWeight = 38,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { FillWeight = 62,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        Controls.Add(grid);
        grid.SelectionChanged += (sender, args) => UpdateButtons();
        grid.CellDoubleClick += (sender, args) =>
        {
            if (args.RowIndex >= 0) openBuild(args.RowIndex);
        };

        empty = Theme.Label(this, "Нажмите «Добавить», чтобы сохранить ссылку на билд.", 10, Theme.Muted,
            new Rectangle(14, 120, 604, 72), english: "Use Add to save a build link.");
        empty.Anchor = grid.Anchor;
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.BringToFront();
        UpdateButtons();
    }

    private int SelectedIndex { get { return grid.SelectedRows.Count == 0 ? -1 : grid.SelectedRows[0].Index; } }

    public void RefreshLanguage()
    {
        grid.AccessibleName = UiText.Get("Билды профиля", "Profile builds");
        grid.Columns[0].HeaderText = UiText.Get("Название", "Name");
        grid.Columns[1].HeaderText = UiText.Get("Ссылка", "Link");
    }

    public void LoadProfile(OverlayProfile value)
    {
        profile = value;
        RefreshGrid(profile.ActivePassiveBuildIndex);
    }

    public void SelectBuild(int index)
    {
        if (index >= 0 && index < grid.Rows.Count && SelectedIndex != index)
            grid.CurrentCell = grid.Rows[index].Cells[0];
    }

    private void RefreshGrid(int selection)
    {
        grid.Rows.Clear();
        foreach (SavedPassiveBuild build in profile.PassiveBuilds)
            grid.Rows.Add(build.Name, build.Link);
        SelectBuild(selection);
        empty.Visible = grid.Rows.Count == 0;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        open.Enabled = edit.Enabled = delete.Enabled = SelectedIndex >= 0;
    }

    private void OpenSelectedBuild()
    {
        if (SelectedIndex >= 0) openBuild(SelectedIndex);
    }

    private void EditBuild(int index)
    {
        var builds = profile.PassiveBuilds;
        bool adding = index < 0;
        SavedPassiveBuild current = adding ? null : builds[index];
        using (var dialog = new BuildDialog(current, link => builds.Any(build => build != current && build.Link == link)))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            if (adding)
            {
                index = builds.Count;
                builds.Add(dialog.Build);
            }
            else builds[index] = dialog.Build;

            RefreshGrid(index);
            if (adding) openBuild(index);
            else changed();
        }
    }

    private void DeleteBuild()
    {
        int index = SelectedIndex;
        if (index < 0) return;
        var builds = profile.PassiveBuilds;
        string message = String.Format(UiText.Get("Удалить билд «{0}»?", "Delete the build “{0}”?"), builds[index]);
        if (MessageBox.Show(FindForm(), message, UiText.Get("Билды", "Builds"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        builds.RemoveAt(index);
        if (index < profile.ActivePassiveBuildIndex) profile.ActivePassiveBuildIndex--;
        else if (profile.ActivePassiveBuildIndex == builds.Count)
            profile.ActivePassiveBuildIndex = Math.Max(0, builds.Count - 1);
        RefreshGrid(profile.ActivePassiveBuildIndex);
        changed();
    }

    private sealed class BuildDialog : Form
    {
        private readonly TextBox linkBox;
        private readonly TextBox nameBox;
        private readonly Label error;
        private readonly Func<string, bool> linkExists;

        public SavedPassiveBuild Build { get; private set; }

        public BuildDialog(SavedPassiveBuild build, Func<string, bool> linkExists)
        {
            this.linkExists = linkExists;
            SuspendLayout();
            Text = build == null ? UiText.Get("Добавить билд", "Add build") : UiText.Get("Изменить билд", "Edit build");
            ClientSize = new Size(504, 218);
            Theme.StyleWindow(this);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;

            Theme.Label(this, "Ссылка на билд pobb.in", 10, Theme.Text, new Rectangle(18, 14, 468, 23),
                true, "pobb.in build link");
            linkBox = AddField(new Rectangle(18, 39, 468, 28), 256, build == null ? "" : build.Link,
                UiText.Get("Ссылка на билд pobb.in", "pobb.in build link"));
            Theme.Label(this, "Название (необязательно)", 10, Theme.Text, new Rectangle(18, 79, 468, 23),
                true, "Name (optional)");
            nameBox = AddField(new Rectangle(18, 104, 468, 28), 100, build == null ? "" : build.Name,
                UiText.Get("Название билда", "Build name"));
            error = Theme.Label(this, "", 9, Theme.Warning, new Rectangle(18, 139, 468, 25));
            linkBox.TextChanged += (sender, args) => error.Text = "";

            var cancel = Theme.Button(this, "Отмена", new Rectangle(260, 170, 104, 32), english: "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            var save = Theme.Button(this, "Сохранить", new Rectangle(372, 170, 114, 32),
                (sender, args) => SaveBuild(), english: "Save");
            save.BackColor = Theme.Accent;
            save.ForeColor = Theme.Background;
            AcceptButton = save;
            CancelButton = cancel;
            ActiveControl = linkBox;
            ResumeLayout(false);
        }

        private TextBox AddField(Rectangle bounds, int maxLength, string value, string name)
        {
            var field = new TextBox { Bounds = bounds, MaxLength = maxLength, Text = value ?? "", AccessibleName = name,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11) };
            Controls.Add(field);
            return field;
        }

        private void SaveBuild()
        {
            string link;
            if (!PassiveBuild.TryNormalizeLink(linkBox.Text.Trim(), out link))
            {
                error.Text = UiText.Get("Введите ссылку вида https://pobb.in/…", "Enter a link like https://pobb.in/…");
                linkBox.Focus();
                return;
            }
            if (linkExists(link))
            {
                error.Text = UiText.Get("Эта ссылка уже есть в профиле.", "This link is already saved in the profile.");
                linkBox.Focus();
                return;
            }
            Build = new SavedPassiveBuild { Link = link, Name = nameBox.Text.Trim() };
            DialogResult = DialogResult.OK;
        }
    }
}
