using System;
using System.Windows.Forms;

internal static class UiText
{
    public static bool English { get; set; }

    public static string Get(string russian, string english) { return English ? english : russian; }

    public static void Bind(Control control, string russian, string english)
    {
        control.Tag = Tuple.Create(russian, english);
        control.Text = Get(russian, english);
    }

    public static void Refresh(Control control)
    {
        var text = control.Tag as Tuple<string, string>;
        if (text != null) control.Text = Get(text.Item1, text.Item2);
        foreach (Control child in control.Controls) Refresh(child);
    }
}
