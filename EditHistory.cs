using System;
using System.Collections.Generic;

internal sealed class EditHistory<TSelection>
{
    private const int HistoryLimit = 5;
    private readonly List<Edit> edits = new List<Edit>();
    private int position;

    public bool CanUndo { get { return position > 0; } }
    public bool CanRedo { get { return position < edits.Count; } }

    public void Apply(Action undo, Action redo, TSelection previousSelection, TSelection selection)
    {
        redo();
        edits.RemoveRange(position, edits.Count - position);
        edits.Add(new Edit(undo, redo, previousSelection, selection));
        if (edits.Count > HistoryLimit) edits.RemoveAt(0);
        position = edits.Count;
    }

    public TSelection Undo()
    {
        Edit edit = edits[position - 1];
        edit.Undo();
        position--;
        return edit.PreviousSelection;
    }

    public TSelection Redo()
    {
        Edit edit = edits[position];
        edit.Redo();
        position++;
        return edit.Selection;
    }

    private sealed class Edit
    {
        public readonly Action Undo;
        public readonly Action Redo;
        public readonly TSelection PreviousSelection;
        public readonly TSelection Selection;

        public Edit(Action undo, Action redo, TSelection previousSelection, TSelection selection)
        {
            Undo = undo;
            Redo = redo;
            PreviousSelection = previousSelection;
            Selection = selection;
        }
    }
}
