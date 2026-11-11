using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Windows.Forms;

[DataContract]
internal sealed class RouteNote
{
    [DataMember] public string AreaId;
    [DataMember] public string Text;
    [DataMember] public bool Done;
}

internal sealed class RouteEditor : Panel
{
    private const int HistoryLimit = 5;
    private readonly Action changed;
    private readonly ConditionalWeakTable<List<RouteNote>, EditHistory> histories =
        new ConditionalWeakTable<List<RouteNote>, EditHistory>();
    private readonly ComboBox catalog;
    private readonly DataGridView grid;
    private readonly Label empty;
    private readonly Button undo;
    private readonly Button redo;
    private readonly Button up;
    private readonly Button down;
    private readonly Button edit;
    private readonly Button delete;
    private readonly Button add;
    private int act;
    private List<RouteNote> notes;
    private EditHistory history;
    private bool readOnly;

    public RouteEditor(Action changed, Action<RouteNote, bool> setDone)
    {
        this.changed = changed;
        BackColor = Theme.Background;
        Size = new Size(632, 205);

        undo = Theme.Button(this, "Отменить", new Rectangle(286, 46, 82, 30), (sender, args) => UndoEdit(), english: "Undo");
        redo = Theme.Button(this, "Повторить", new Rectangle(376, 46, 86, 30), (sender, args) => RedoEdit(), english: "Redo");
        up = Theme.Button(this, "↑", new Rectangle(540, 46, 34, 30), (sender, args) => MoveNote(-1));
        down = Theme.Button(this, "↓", new Rectangle(584, 46, 34, 30), (sender, args) => MoveNote(1));
        delete = Theme.Button(this, "Удалить", new Rectangle(285, 9, 82, 30), (sender, args) => DeleteNote(), english: "Delete");
        edit = Theme.Button(this, "Изменить", new Rectangle(375, 9, 91, 30), (sender, args) => EditNote(SelectedIndex), english: "Edit");
        add = Theme.Button(this, "+ Добавить заметку", new Rectangle(474, 9, 144, 30), (sender, args) => EditNote(-1), english: "+ Add note");
        add.BackColor = Theme.Accent;
        add.ForeColor = Theme.Background;
        undo.Anchor = redo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        up.Anchor = down.Anchor = delete.Anchor = edit.Anchor = add.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        Theme.Label(this, "Локации акта", 9, Theme.Muted, new Rectangle(14, 46, 105, 28), true, "Act locations");
        catalog = AreaPicker(new Rectangle(122, 48, 154, 27), UiText.Get("Локации акта", "Act locations"));
        catalog.DropDownWidth = 340;
        catalog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(catalog);
        Theme.Label(this, "Порядок", 9, Theme.Muted, new Rectangle(474, 46, 62, 30), english: "Order").Anchor = AnchorStyles.Top | AnchorStyles.Right;

        grid = Theme.Grid(new Rectangle(14, 83, 604, 111));
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.RowTemplate.Height = 27;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", Width = 38, FlatStyle = FlatStyle.Flat });
        grid.Columns.Add(new DataGridViewTextBoxColumn { FillWeight = 38,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { FillWeight = 62,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        Controls.Add(grid);
        empty = Theme.Label(this, "Добавьте первую заметку для этого акта.", 10, Theme.Muted,
            new Rectangle(14, 112, 604, 78), english: "Add your first note for this act.");
        empty.Anchor = grid.Anchor;
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.BringToFront();
        grid.SelectionChanged += (sender, args) => UpdateButtons();
        grid.CellContentClick += (sender, args) =>
        {
            if (args.RowIndex < 0 || args.ColumnIndex != 0) return;
            RouteNote note = notes[args.RowIndex];
            setDone(note, !note.Done);
        };
        grid.CellDoubleClick += (sender, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex > 0) EditNote(args.RowIndex);
        };
        RefreshLanguage();
        UpdateButtons();
    }

    public void RefreshLanguage()
    {
        up.AccessibleName = UiText.Get("Поднять заметку", "Move note up");
        down.AccessibleName = UiText.Get("Опустить заметку", "Move note down");
        catalog.AccessibleName = UiText.Get("Локации акта", "Act locations");
        grid.Columns[0].HeaderCell.ToolTipText = UiText.Get("Выполнено", "Completed");
        grid.Columns[1].HeaderText = UiText.Get("Локация", "Location");
        grid.Columns[2].HeaderText = UiText.Get("Заметка", "Note");
        if (act == 0) return;
        int selection = SelectedIndex;
        var area = (CampaignArea)catalog.SelectedItem;
        FillPicker(catalog, act, area.Id);
        RefreshGrid(selection);
    }

    public void LoadAct(int act, List<RouteNote> notes, bool readOnly)
    {
        if (this.act == act && this.notes == notes && this.readOnly == readOnly) return;
        this.act = act;
        this.notes = notes;
        this.readOnly = readOnly;
        history = readOnly ? null : histories.GetValue(notes, route => new EditHistory());
        FillPicker(catalog, act, null);
        RefreshGrid(0);
    }

    private int SelectedIndex { get { return grid.SelectedRows.Count == 0 ? -1 : grid.SelectedRows[0].Index; } }

    private void RefreshGrid(int selection)
    {
        grid.Rows.Clear();
        foreach (RouteNote note in notes)
        {
            string preview = (note.Text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            int index = grid.Rows.Add(note.Done, CampaignAreas.NameOf(note.AreaId), preview);
            grid.Rows[index].Cells[2].ToolTipText = note.Text;
            Theme.SetChecklistRowColor(grid.Rows[index], note.Done);
        }
        if (selection >= 0 && selection < grid.Rows.Count)
            grid.CurrentCell = grid.Rows[selection].Cells[1];
        empty.Visible = notes.Count == 0;
        UpdateButtons();
    }

    public void SyncCompletion(RouteNote note)
    {
        int index = notes.IndexOf(note);
        if (index < 0) return;
        DataGridViewRow row = grid.Rows[index];
        row.Cells[0].Value = note.Done;
        Theme.SetChecklistRowColor(row, note.Done);
    }

    private void UpdateButtons()
    {
        int index = SelectedIndex;
        undo.Enabled = history != null && history.CanUndo;
        redo.Enabled = history != null && history.CanRedo;
        add.Enabled = notes != null && !readOnly;
        edit.Enabled = delete.Enabled = !readOnly && index >= 0;
        up.Enabled = !readOnly && index > 0;
        down.Enabled = !readOnly && index >= 0 && index < grid.Rows.Count - 1;
    }

    private void EditNote(int index)
    {
        if (readOnly) return;
        List<RouteNote> route = notes;
        bool adding = index < 0;
        RouteNote note = adding ? new RouteNote { AreaId = ((CampaignArea)catalog.SelectedItem).Id } : route[index];
        using (var dialog = new NoteDialog(act, note, adding))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            string areaId = dialog.AreaId;
            string text = dialog.NoteText;
            if (adding)
            {
                note.AreaId = areaId;
                note.Text = text;
                int position = route.Count;
                ApplyEdit(() => route.RemoveAt(position), () => route.Add(note), SelectedIndex, position);
                return;
            }

            string previousAreaId = note.AreaId;
            string previousText = note.Text;
            if (previousAreaId == areaId && (previousText ?? "") == text) return;
            ApplyEdit(() => SetContent(note, previousAreaId, previousText),
                () => SetContent(note, areaId, text), index, index);
        }
    }

    private static void SetContent(RouteNote note, string areaId, string text)
    {
        note.AreaId = areaId;
        note.Text = text;
    }

    private void MoveNote(int direction)
    {
        if (readOnly) return;
        int index = SelectedIndex;
        int destination = index + direction;
        if (index < 0 || destination < 0 || destination >= notes.Count) return;
        List<RouteNote> route = notes;
        Action swap = () =>
        {
            RouteNote note = route[index];
            route[index] = route[destination];
            route[destination] = note;
        };
        ApplyEdit(swap, swap, index, destination);
    }

    private void DeleteNote()
    {
        if (readOnly) return;
        int index = SelectedIndex;
        if (index < 0) return;
        if (MessageBox.Show(FindForm(), UiText.Get("Удалить выбранную заметку?", "Delete the selected note?"), UiText.Get("Маршрут", "Route"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        List<RouteNote> route = notes;
        RouteNote note = route[index];
        ApplyEdit(() => route.Insert(index, note), () => route.RemoveAt(index), index, Math.Min(index, route.Count - 2));
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z) && undo.Enabled)
        {
            UndoEdit();
            return true;
        }
        if ((keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) && redo.Enabled)
        {
            RedoEdit();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void ApplyEdit(Action undoEdit, Action redoEdit, int previousSelection, int selection)
    {
        redoEdit();
        history.Record(new NoteEdit(undoEdit, redoEdit, previousSelection, selection));
        Commit(selection);
    }

    private void UndoEdit()
    {
        if (history != null && history.CanUndo) Commit(history.Undo());
    }

    private void RedoEdit()
    {
        if (history != null && history.CanRedo) Commit(history.Redo());
    }

    private void Commit(int selection)
    {
        RefreshGrid(selection);
        changed();
    }

    private sealed class NoteEdit
    {
        public readonly Action Undo;
        public readonly Action Redo;
        public readonly int PreviousSelection;
        public readonly int Selection;

        public NoteEdit(Action undo, Action redo, int previousSelection, int selection)
        {
            Undo = undo;
            Redo = redo;
            PreviousSelection = previousSelection;
            Selection = selection;
        }
    }

    private sealed class EditHistory
    {
        private readonly List<NoteEdit> edits = new List<NoteEdit>();
        private int position;

        public bool CanUndo { get { return position > 0; } }
        public bool CanRedo { get { return position < edits.Count; } }

        public void Record(NoteEdit edit)
        {
            edits.RemoveRange(position, edits.Count - position);
            edits.Add(edit);
            if (edits.Count > HistoryLimit) edits.RemoveAt(0);
            position = edits.Count;
        }

        public int Undo()
        {
            NoteEdit edit = edits[position - 1];
            edit.Undo();
            position--;
            return edit.PreviousSelection;
        }

        public int Redo()
        {
            NoteEdit edit = edits[position];
            edit.Redo();
            position++;
            return edit.Selection;
        }
    }

    private static ComboBox AreaPicker(Rectangle bounds, string name)
    {
        var picker = new ComboBox { Bounds = bounds, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10), BackColor = Theme.Surface,
            ForeColor = Theme.Text, MaxDropDownItems = 14, AccessibleName = name };
        Theme.StylePicker(picker);
        return picker;
    }

    private static void FillPicker(ComboBox picker, int act, string selectedId)
    {
        picker.BeginUpdate();
        picker.Items.Clear();
        picker.Items.Add(new CampaignArea(null, "Без локации", "No location"));
        int selection = 0;
        foreach (CampaignArea area in CampaignAreas.ForAct(act))
        {
            int index = picker.Items.Add(area);
            if (area.Id == selectedId) selection = index;
        }
        if (selection == 0 && !String.IsNullOrEmpty(selectedId))
            selection = picker.Items.Add(new CampaignArea(selectedId, selectedId, selectedId));
        picker.SelectedIndex = selection;
        picker.EndUpdate();
    }

    private sealed class NoteDialog : Form
    {
        private readonly ComboBox picker;
        private readonly TextBox text;

        public string AreaId { get { return ((CampaignArea)picker.SelectedItem).Id; } }
        public string NoteText { get { return text.Text; } }

        public NoteDialog(int act, RouteNote note, bool adding)
        {
            SuspendLayout();
            Text = (adding ? UiText.Get("Новая заметка · акт ", "New note · Act ") :
                UiText.Get("Изменить заметку · акт ", "Edit note · Act ")) + act;
            ClientSize = new Size(472, 304);
            Theme.StyleWindow(this);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;

            Theme.Label(this, "Локация", 11, Theme.Accent, new Rectangle(20, 13, 432, 24), true, "Location");
            picker = AreaPicker(new Rectangle(20, 43, 432, 28), UiText.Get("Локация заметки", "Note location"));
            FillPicker(picker, act, note.AreaId);
            Controls.Add(picker);
            Theme.Label(this, "Заметка", 11, Theme.Accent, new Rectangle(20, 87, 432, 24), true, "Note");
            Theme.Label(this, "Выберите локацию, напишите заметку или заполните оба поля.", 9, Theme.Muted,
                new Rectangle(20, 111, 432, 23), english: "Choose a location, write a note, or do both.");
            text = new TextBox { Bounds = new Rectangle(20, 143, 432, 99), Text = note.Text ?? "",
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface,
                ForeColor = Theme.Text, Font = new Font("Segoe UI", 11), AccessibleName = UiText.Get("Текст заметки", "Note text") };
            Theme.StyleScrollBars(text);
            Controls.Add(text);

            var cancel = Theme.Button(this, "Отмена", new Rectangle(234, 255, 98, 32), english: "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            var save = Theme.Button(this, "Сохранить", new Rectangle(340, 255, 112, 32), english: "Save");
            save.DialogResult = DialogResult.OK;
            save.BackColor = Theme.Accent;
            save.ForeColor = Theme.Background;
            EventHandler validate = (sender, args) => save.Enabled = AreaId != null || !String.IsNullOrWhiteSpace(NoteText);
            validate(this, EventArgs.Empty);
            text.TextChanged += validate;
            picker.SelectedIndexChanged += validate;
            CancelButton = cancel;
            AcceptButton = save;
            ActiveControl = text;
            ResumeLayout(false);
        }
    }
}
