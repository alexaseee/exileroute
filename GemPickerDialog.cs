using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class GemEntry
{
    private readonly string russianName;
    private readonly string englishName;
    public readonly bool IsSupport;

    public GemEntry(string russianName, string englishName, bool isSupport)
    {
        this.russianName = russianName;
        this.englishName = englishName;
        IsSupport = isSupport;
    }

    public string Name { get { return UiText.Get(russianName, englishName); } }
    public string OtherName { get { return UiText.Get(englishName, russianName); } }

    public bool Matches(string query)
    {
        return russianName.Replace('ё', 'е').Replace('Ё', 'Е').IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            englishName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class GemCatalog
{
    public static readonly string Version;
    private static readonly List<GemEntry> Entries = new List<GemEntry>();

    static GemCatalog()
    {
        using (var stream = typeof(GemCatalog).Assembly.GetManifestResourceStream("GemCatalog.tsv"))
        using (var reader = new StreamReader(stream))
        {
            Version = reader.ReadLine();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] columns = line.Split('\t');
                Entries.Add(new GemEntry(columns[1], columns[2], columns[0] == "support"));
            }
        }
    }

    public static GemEntry[] SortedEntries()
    {
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo(UiText.English ? "en-US" : "ru-RU"), true);
        return Entries.OrderBy(gem => gem.Name, comparer).ToArray();
    }
}

internal sealed class GemPickerDialog : Form
{
    private readonly GemEntry[] gems;
    private readonly TextBox search;
    private readonly ComboBox category;
    private readonly DataGridView results;
    private readonly Label resultCount;
    private readonly Label empty;
    private readonly Button insert;

    public GemEntry SelectedGem { get { return results.CurrentRow == null ? null : (GemEntry)results.CurrentRow.Tag; } }
    public string SelectedName
    {
        get
        {
            GemEntry gem = SelectedGem;
            if (gem == null) return null;
            return results.CurrentCell.ColumnIndex == 1 ? gem.OtherName : gem.Name;
        }
    }

    public GemPickerDialog()
    {
        SuspendLayout();
        Text = UiText.Get("Вставить камень", "Insert a gem");
        ClientSize = new Size(700, 487);
        Theme.StyleWindow(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        gems = GemCatalog.SortedEntries();

        Theme.Label(this, "Камни Path of Exile 1", 18, Theme.Text, new Rectangle(18, 12, 490, 38), true, "Path of Exile 1 gems");
        var version = Theme.Label(this, UiText.Get("Версия ", "Patch ") + GemCatalog.Version, 9, Theme.Accent,
            new Rectangle(515, 15, 167, 27), true);
        version.TextAlign = ContentAlignment.MiddleRight;
        Theme.Label(this, "Название на русском или английском", 9, Theme.Muted,
            new Rectangle(18, 59, 447, 20), english: "Search by Russian or English name");
        Theme.Label(this, "Тип камня", 9, Theme.Muted, new Rectangle(480, 59, 202, 20), english: "Gem type");
        search = new TextBox { Bounds = new Rectangle(18, 84, 447, 27), BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11),
            AccessibleName = UiText.Get("Поиск камня", "Search gems") };
        Controls.Add(search);
        category = new ComboBox { Bounds = new Rectangle(480, 84, 202, 27), DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10),
            AccessibleName = UiText.Get("Тип камня", "Gem type") };
        Theme.StylePicker(category);
        category.Items.AddRange(new object[] { UiText.Get("Все камни", "All gems"), UiText.Get("Умения", "Skill gems"),
            UiText.Get("Поддержка", "Support gems") });
        category.SelectedIndex = 0;
        Controls.Add(category);

        results = Theme.Grid(new Rectangle(18, 124, 664, 303));
        results.SelectionMode = DataGridViewSelectionMode.CellSelect;
        results.DefaultCellStyle.SelectionBackColor = Theme.Accent;
        results.DefaultCellStyle.SelectionForeColor = Theme.Background;
        results.RowTemplate.Height = 26;
        results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiText.Get("Русское название", "English name"), FillWeight = 54,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiText.Get("Английское название", "Russian name"), FillWeight = 46,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiText.Get("Тип", "Type"), Width = 101,
            SortMode = DataGridViewColumnSortMode.NotSortable });
        Controls.Add(results);
        empty = Theme.Label(this, "Камни не найдены.", 11, Theme.Muted, new Rectangle(18, 155, 664, 83), english: "No matching gems.");
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.BackColor = Theme.Surface;
        empty.BringToFront();
        resultCount = Theme.Label(this, "", 9, Theme.Muted, new Rectangle(18, 438, 404, 33));

        var cancel = Theme.Button(this, "Отмена", new Rectangle(456, 438, 100, 33), english: "Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        insert = Theme.Button(this, "Вставить", new Rectangle(567, 438, 115, 33), english: "Insert");
        insert.BackColor = Theme.Accent;
        insert.ForeColor = Theme.Background;
        insert.DialogResult = DialogResult.OK;
        AcceptButton = insert;
        CancelButton = cancel;

        search.TextChanged += (sender, args) => Filter();
        category.SelectedIndexChanged += (sender, args) => Filter();
        results.CurrentCellChanged += (sender, args) => insert.Enabled = SelectedGem != null;
        results.CellDoubleClick += (sender, args) =>
        {
            if (args.RowIndex < 0 || args.ColumnIndex < 0) return;
            results.CurrentCell = results.Rows[args.RowIndex].Cells[args.ColumnIndex];
            if (SelectedGem != null) DialogResult = DialogResult.OK;
        };
        results.KeyDown += (sender, args) =>
        {
            if (args.KeyCode != Keys.Enter || SelectedGem == null) return;
            args.SuppressKeyPress = true;
            DialogResult = DialogResult.OK;
        };
        ActiveControl = search;
        Filter();
        ResumeLayout(false);
    }

    private void Filter()
    {
        string query = search.Text.Trim().Replace('ё', 'е').Replace('Ё', 'Е');
        int selectedCategory = category.SelectedIndex;
        results.SuspendLayout();
        results.Rows.Clear();
        foreach (GemEntry gem in gems)
        {
            if (selectedCategory != 0 && gem.IsSupport != (selectedCategory == 2)) continue;
            if (!gem.Matches(query)) continue;
            string type = gem.IsSupport ? UiText.Get("Поддержка", "Support") : UiText.Get("Умение", "Skill");
            int row = results.Rows.Add(gem.Name, gem.OtherName, type);
            results.Rows[row].Tag = gem;
            results.Rows[row].Cells[0].ToolTipText = gem.Name;
            results.Rows[row].Cells[1].ToolTipText = gem.OtherName;
        }
        if (results.Rows.Count > 0) results.CurrentCell = results.Rows[0].Cells[0];
        results.ResumeLayout();
        empty.Visible = results.Rows.Count == 0;
        insert.Enabled = SelectedGem != null;
        resultCount.Text = UiText.Get("Найдено ", "Matches ") + results.Rows.Count;
    }
}
