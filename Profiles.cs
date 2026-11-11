using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

[DataContract]
internal sealed class OverlayProfile
{
    public const int ActCount = 10;
    public const string BuiltInName = "Основной";
    public const string BuiltInEnglishName = "Default";
    private const string ExileLevelingName = "Exile Leveling";

    [DataMember] public string Name;
    [DataMember(EmitDefaultValue = false)] public bool IsBuiltIn { get; private set; }
    [DataMember(EmitDefaultValue = false)] public bool IsExileLeveling { get; private set; }
    [DataMember(EmitDefaultValue = false)] public string PassiveBuildLink;
    [DataMember(Name = "Tasks", EmitDefaultValue = false, Order = 1)] private List<ActTask>[] tasks;
    [DataMember(Name = "Routes", EmitDefaultValue = false, Order = 2)] private List<RouteNote>[] routes;

    public bool ContentReadOnly { get { return IsBuiltIn && (IsExileLeveling || !BuiltInProfile.AuthorBuild); } }
    public List<ActTask>[] Tasks { get { return IsBuiltIn ? BuiltInProfile.GetContent(IsExileLeveling).Tasks : tasks; } }
    public List<RouteNote>[] Routes { get { return IsBuiltIn ? BuiltInProfile.GetContent(IsExileLeveling).Routes : routes; } }

    private OverlayProfile() { }

    public OverlayProfile(string name)
    {
        Name = name;
        NormalizeActs();
    }

    public void NormalizeActs()
    {
        if (IsBuiltIn)
        {
            Name = IsExileLeveling ? ExileLevelingName : BuiltInName;
            tasks = null;
            routes = null;
            return;
        }
        Normalize(ref tasks);
        Normalize(ref routes);
    }

    public static OverlayProfile CreateBuiltIn()
    {
        return new OverlayProfile { Name = BuiltInName, IsBuiltIn = true };
    }

    public static OverlayProfile CreateExileLeveling()
    {
        return new OverlayProfile { Name = ExileLevelingName, IsBuiltIn = true, IsExileLeveling = true };
    }

    private static void Normalize<T>(ref List<T>[] lists)
    {
        Array.Resize(ref lists, ActCount);
        for (int i = 0; i < lists.Length; i++)
            if (lists[i] == null) lists[i] = new List<T>();
    }

    public OverlayProfile Copy(string name)
    {
        var copy = new OverlayProfile(name) { PassiveBuildLink = PassiveBuildLink };
        for (int i = 0; i < ActCount; i++)
        {
            copy.Tasks[i].AddRange(Tasks[i].Select(task => new ActTask
                { Condition = task.Condition, Action = task.Action, Done = task.Done }));
            copy.Routes[i].AddRange(Routes[i].Select(note => new RouteNote { AreaId = note.AreaId, Text = note.Text, Done = note.Done }));
        }
        return copy;
    }

    public override string ToString() { return IsBuiltIn && !IsExileLeveling ? UiText.Get(BuiltInName, BuiltInEnglishName) : Name; }
}

internal static class BuiltInProfile
{
    [DataContract]
    private sealed class LocalizedProfiles
    {
        [DataMember(Order = 0)] public OverlayProfile Russian { get; private set; }
        [DataMember(Order = 1)] public OverlayProfile English { get; private set; }
    }

    private const string DefaultFileName = "BuiltInProfile.json";
    private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(LocalizedProfiles));
#if EDIT_DEFAULT_PROFILE
    public static readonly bool AuthorBuild = true;
    private static readonly string DefaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFileName);
    private static readonly LocalizedProfiles Default = Load(File.OpenRead(DefaultPath));
#else
    public static readonly bool AuthorBuild = false;
    private static readonly LocalizedProfiles Default = Load(OpenResource(DefaultFileName));
#endif
    private static readonly LocalizedProfiles ExileLeveling = Load(OpenResource("ExileLevelingProfile.json"));

    public static OverlayProfile GetContent(bool exileLeveling)
    {
        LocalizedProfiles profiles = exileLeveling ? ExileLeveling : Default;
        return UiText.English ? profiles.English : profiles.Russian;
    }

    private static Stream OpenResource(string name)
    {
        return typeof(BuiltInProfile).Assembly.GetManifestResourceStream(name);
    }

    private static LocalizedProfiles Load(Stream stream)
    {
        using (stream)
        {
            var profiles = (LocalizedProfiles)Serializer.ReadObject(stream);
            profiles.Russian.NormalizeActs();
            profiles.English.NormalizeActs();
            return profiles;
        }
    }

#if EDIT_DEFAULT_PROFILE
    public static void SaveDefault()
    {
        string temporary = DefaultPath + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(file, new UTF8Encoding(false), false, true, "  "))
                    Serializer.WriteObject(writer, Default);
                file.WriteByte((byte)'\n');
            }
            File.Replace(temporary, DefaultPath, null);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
#endif

    public static bool MatchesRussian(OverlayProfile source)
    {
        OverlayProfile russian = Default.Russian;
        for (int i = 0; i < OverlayProfile.ActCount; i++)
        {
            if (!source.Tasks[i].Select(task => new { task.Condition, task.Action, task.Done })
                .SequenceEqual(russian.Tasks[i].Select(task => new { task.Condition, task.Action, task.Done }))) return false;
            if (!source.Routes[i].Select(note => new { note.AreaId, note.Text, note.Done })
                .SequenceEqual(russian.Routes[i].Select(note => new { note.AreaId, note.Text, note.Done }))) return false;
        }
        return true;
    }
}

internal sealed class ProfileNameDialog : Form
{
    private readonly TextBox nameBox;
    public string ProfileName { get { return nameBox.Text.Trim(); } }

    public ProfileNameDialog(List<OverlayProfile> profiles, OverlayProfile source)
    {
        SuspendLayout();
        Text = source == null ? UiText.Get("Новый профиль", "New profile") : UiText.Get("Копировать профиль", "Duplicate profile");
        ClientSize = new Size(420, 159);
        Theme.StyleWindow(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Label(this, "Имя профиля", 11, Theme.Accent, new Rectangle(18, 12, 384, 24), true, "Profile name");
        nameBox = new TextBox { Bounds = new Rectangle(18, 44, 384, 26), AccessibleName = UiText.Get("Имя профиля", "Profile name"),
            BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 11),
            Text = source == null ? "" : source.ToString() + UiText.Get(" — копия", " — copy") };
        Controls.Add(nameBox);
        var status = Theme.Label(this, "", 9, Theme.Muted, new Rectangle(18, 75, 384, 23));
        var cancel = Theme.Button(this, "Отмена", new Rectangle(194, 111, 94, 32), english: "Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var create = Theme.Button(this, source == null ? "Создать" : "Копировать", new Rectangle(298, 111, 104, 32),
            english: source == null ? "Create" : "Duplicate");
        create.BackColor = Theme.Accent;
        create.ForeColor = Theme.Background;
        create.DialogResult = DialogResult.OK;

        EventHandler validate = (sender, args) =>
        {
            string name = ProfileName;
            bool duplicate = profiles.Any(profile => String.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase) ||
                (profile.IsBuiltIn && !profile.IsExileLeveling &&
                    String.Equals(OverlayProfile.BuiltInEnglishName, name, StringComparison.OrdinalIgnoreCase)));
            create.Enabled = name.Length > 0 && !duplicate;
            status.Text = duplicate ? UiText.Get("Профиль с таким именем уже есть.", "A profile with this name already exists.") : "";
        };
        validate(this, EventArgs.Empty);
        nameBox.TextChanged += validate;
        AcceptButton = create;
        CancelButton = cancel;
        ActiveControl = nameBox;
        ResumeLayout(false);
    }
}

internal sealed class DeleteProfileDialog : Form
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);
    private readonly Stopwatch elapsed = new Stopwatch();
    private readonly Timer countdown = new Timer { Interval = 100 };
    private readonly Button confirm;
    private readonly string confirmText;

    public DeleteProfileDialog(string profileName)
    {
        SuspendLayout();
        Text = UiText.Get("Удалить профиль", "Delete profile");
        ClientSize = new Size(442, 188);
        Theme.StyleWindow(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Label(this, "Удалить выбранный профиль?", 11, Theme.Accent, new Rectangle(18, 12, 406, 24), true,
            "Delete the selected profile?");
        var name = Theme.Label(this, profileName, 11, Theme.Text, new Rectangle(18, 45, 406, 34), true);
        name.BackColor = Theme.Surface;
        name.Padding = new Padding(8, 0, 8, 0);
        Theme.Label(this, "Задачи, галочки и маршрут этого профиля будут удалены.", 9, Theme.Muted,
            new Rectangle(18, 87, 406, 35), english: "This deletes the profile's tasks, checkmarks, and route notes.");

        var cancel = Theme.Button(this, "Отмена", new Rectangle(202, 137, 98, 33), english: "Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        confirmText = UiText.Get("Удалить", "Delete");
        confirm = Theme.Button(this, confirmText + " (2)", new Rectangle(310, 137, 114, 33), (sender, args) =>
        {
            if (elapsed.Elapsed >= Delay) DialogResult = DialogResult.OK;
        });
        confirm.BackColor = Theme.Control;
        confirm.ForeColor = Theme.Muted;
        confirm.Enabled = false;
        countdown.Tick += (sender, args) => UpdateCountdown();
        CancelButton = cancel;
        ActiveControl = cancel;
        ResumeLayout(false);
    }

    protected override void OnShown(EventArgs args)
    {
        base.OnShown(args);
        elapsed.Start();
        countdown.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) countdown.Dispose();
        base.Dispose(disposing);
    }

    private void UpdateCountdown()
    {
        int seconds = (int)Math.Ceiling((Delay - elapsed.Elapsed).TotalSeconds);
        if (seconds > 0)
        {
            confirm.Text = confirmText + " (" + seconds + ")";
            return;
        }

        countdown.Stop();
        confirm.Text = confirmText;
        confirm.BackColor = Theme.Accent;
        confirm.ForeColor = Theme.Background;
        confirm.Enabled = true;
    }
}
