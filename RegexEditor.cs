using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class RegexEditor : Panel
{
    private readonly List<string> expressions;
    private readonly Action changed;
    private readonly DataGridView grid;
    private readonly DataGridViewButtonColumn copyColumn;
    private readonly DataGridViewButtonColumn editColumn;
    private readonly DataGridViewButtonColumn deleteColumn;
    private readonly Label empty;
    private readonly Label status;

    public RegexEditor(List<string> expressions, Action changed)
    {
        this.expressions = expressions;
        this.changed = changed;
        BackColor = Theme.Background;
        Size = new Size(632, 205);

        status = Theme.Label(this, "", 9, Theme.Accent, new Rectangle(292, 9, 172, 30));
        status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var add = Theme.Button(this, "+ Добавить", new Rectangle(474, 9, 144, 30),
            (sender, args) => EditExpression(-1), english: "+ Add");
        add.BackColor = Theme.Accent;
        add.ForeColor = Theme.Background;
        add.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        grid = Theme.Grid(new Rectangle(14, 50, 604, 142));
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ColumnHeadersVisible = false;
        grid.RowTemplate.Height = 34;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Consolas", 10),
                Padding = new Padding(6, 0, 6, 0)
            }
        });
        copyColumn = AddButtonColumn(108);
        editColumn = AddButtonColumn(94);
        deleteColumn = AddButtonColumn(83);
        Controls.Add(grid);

        empty = Theme.Label(this, "Нажмите «Добавить», чтобы сохранить выражение.", 10, Theme.Muted,
            new Rectangle(14, 50, 604, 142), english: "Use Add to save a search expression.");
        empty.Anchor = grid.Anchor;
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.BringToFront();
        empty.Visible = expressions.Count == 0;

        foreach (string expression in expressions)
            SetExpressionCell(grid.Rows.Add(), expression);
        grid.CellContentClick += (sender, args) =>
        {
            if (args.RowIndex < 0) return;
            if (args.ColumnIndex == copyColumn.Index) CopyExpression(expressions[args.RowIndex]);
            else if (args.ColumnIndex == editColumn.Index) EditExpression(args.RowIndex);
            else if (args.ColumnIndex == deleteColumn.Index) DeleteExpression(args.RowIndex);
        };
    }

    public void RefreshLanguage()
    {
        grid.AccessibleName = UiText.Get("Выражения для поиска в Path of Exile", "Path of Exile search expressions");
        copyColumn.Text = UiText.Get("Копировать", "Copy");
        editColumn.Text = UiText.Get("Изменить", "Edit");
        deleteColumn.Text = UiText.Get("Удалить", "Delete");
    }

    private DataGridViewButtonColumn AddButtonColumn(int width)
    {
        var column = new DataGridViewButtonColumn
        {
            Width = width, FlatStyle = FlatStyle.Flat, UseColumnTextForButtonValue = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        grid.Columns.Add(column);
        return column;
    }

    private void SetExpressionCell(int index, string expression)
    {
        DataGridViewCell cell = grid.Rows[index].Cells[0];
        cell.Value = expression.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        cell.ToolTipText = expression;
    }

    private void EditExpression(int index)
    {
        bool adding = index < 0;
        using (var dialog = new ExpressionDialog(adding ? null : expressions[index]))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            string expression = dialog.Expression;
            if (adding)
            {
                expressions.Add(expression);
                index = grid.Rows.Add();
            }
            else
            {
                expressions[index] = expression;
            }
            SetExpressionCell(index, expression);
            grid.CurrentCell = grid.Rows[index].Cells[0];
        }
        Commit();
    }

    private void DeleteExpression(int index)
    {
        if (MessageBox.Show(FindForm(), UiText.Get("Удалить это выражение?", "Delete this expression?"),
            "Regex", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        expressions.RemoveAt(index);
        grid.Rows.RemoveAt(index);
        Commit();
    }

    private void Commit()
    {
        empty.Visible = expressions.Count == 0;
        UiText.Bind(status, "", "");
        changed();
    }

    private void CopyExpression(string expression)
    {
        try
        {
            Clipboard.SetText(expression, TextDataFormat.UnicodeText);
            UiText.Bind(status, "Скопировано", "Copied");
            status.ForeColor = Theme.Accent;
        }
        catch (ExternalException)
        {
            UiText.Bind(status, "Буфер занят. Повторите.", "Clipboard busy. Try again.");
            status.ForeColor = Theme.Warning;
        }
    }

    private sealed class ExpressionDialog : Form
    {
        private readonly TextBox text;

        public string Expression { get { return text.Text; } }

        public ExpressionDialog(string expression)
        {
            SuspendLayout();
            Text = expression == null ? UiText.Get("Добавить выражение", "Add expression") :
                UiText.Get("Изменить выражение", "Edit expression");
            ClientSize = new Size(500, 219);
            Theme.StyleWindow(this);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;

            Theme.Label(this, "Выражение для поиска в Path of Exile", 11, Theme.Accent, new Rectangle(18, 14, 464, 26),
                true, "Path of Exile search expression");
            text = new TextBox { Bounds = new Rectangle(18, 48, 464, 105), Multiline = true, AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Consolas", 11),
                Text = expression ?? "", AccessibleName = UiText.Get("Выражение", "Expression") };
            Theme.StyleScrollBars(text);
            Controls.Add(text);

            var cancel = Theme.Button(this, "Отмена", new Rectangle(256, 169, 104, 32), english: "Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            var save = Theme.Button(this, "Сохранить", new Rectangle(368, 169, 114, 32), english: "Save");
            save.DialogResult = DialogResult.OK;
            save.BackColor = Theme.Accent;
            save.ForeColor = Theme.Background;
            save.Enabled = text.TextLength > 0;
            text.TextChanged += (sender, args) => save.Enabled = text.TextLength > 0;
            CancelButton = cancel;
            AcceptButton = save;
            ActiveControl = text;
            ResumeLayout(false);
        }
    }
}
