using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("ExileRoute")]
[assembly: AssemblyProduct("ExileRoute")]

[DataContract]
internal sealed class ActTask
{
    [DataMember] public string Condition;
    [DataMember] public string Action;
    [DataMember] public bool Done;
}

internal enum EditorMode
{
    Tasks,
    Route,
    Regex,
    Passives
}

internal enum HintMode { Tasks, Route, Passives }

[DataContract]
internal sealed class OverlaySettings
{
    public const int DefaultOverlayTransparency = 3;
    public const int MaxOverlayTransparency = 80;
    internal static readonly string FilePath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "PoEActOverlay", "settings.json");
    private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(OverlaySettings));

    [DataMember] public string LogPath;
    [DataMember] public List<OverlayProfile> Profiles = new List<OverlayProfile>
        { OverlayProfile.CreateBuiltIn(), OverlayProfile.CreateExileLeveling() };
    [DataMember] public int ActiveProfileIndex;
    [DataMember(Name = "Tasks", EmitDefaultValue = false)] private List<ActTask>[] legacyTasks;
    [DataMember(Name = "Routes", EmitDefaultValue = false)] private List<RouteNote>[] legacyRoutes;
    [DataMember(Name = "RouteNotes", EmitDefaultValue = false)] private string[] legacyRouteNotes;
    [DataMember] public bool ShowRoute;
    [DataMember(EmitDefaultValue = false)] public bool ShowPassives;
    [DataMember(Name = "EditorShowRoute", EmitDefaultValue = false)] private bool? legacyEditorShowRoute;
    [DataMember(EmitDefaultValue = false)] public EditorMode? EditorTab = EditorMode.Tasks;
    [DataMember(Name = "SearchRegex", EmitDefaultValue = false)] private string legacySearchRegex;
    [DataMember] public List<string> RegexEntries = new List<string>();
    [DataMember] public string Language;
    [DataMember(Name = "Notes", EmitDefaultValue = false)] private string[] legacyNotes;
    [DataMember] public int? Left;
    [DataMember] public int? Top;
    [DataMember(EmitDefaultValue = false)] public int? OverlayWidth;
    [DataMember(EmitDefaultValue = false)] public int? OverlayHeight;
    [DataMember(EmitDefaultValue = false)] public int? EditorWidth;
    [DataMember(EmitDefaultValue = false)] public int? EditorHeight;
    [DataMember(EmitDefaultValue = false)] public int? OverlayTransparency;
    [DataMember(EmitDefaultValue = false)] private int? clickThroughShortcut;

    public Keys ClickThroughShortcut
    {
        get
        {
            Keys shortcut = (Keys)(clickThroughShortcut ?? (int)OverlayHotkey.Default);
            return OverlayHotkey.IsValid(shortcut) ? shortcut : OverlayHotkey.Default;
        }
        set { clickThroughShortcut = value == OverlayHotkey.Default ? (int?)null : (int)value; }
    }

    public OverlayProfile ActiveProfile { get { return Profiles[ActiveProfileIndex]; } }
    public List<ActTask>[] Tasks { get { return ActiveProfile.Tasks; } }
    public List<RouteNote>[] Routes { get { return ActiveProfile.Routes; } }
    public HintMode Mode
    {
        get { return ShowPassives ? HintMode.Passives : CampaignMode; }
        set
        {
            ShowPassives = value == HintMode.Passives;
            if (!ShowPassives) ShowRoute = value == HintMode.Route;
        }
    }
    public HintMode CampaignMode { get { return ShowRoute ? HintMode.Route : HintMode.Tasks; } }
    public int OverlayTransparencyPercent
    {
        get { return Math.Max(0, Math.Min(MaxOverlayTransparency, OverlayTransparency ?? DefaultOverlayTransparency)); }
    }

    public static OverlaySettings Load()
    {
        if (!File.Exists(FilePath)) return new OverlaySettings();
        using (var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            var settings = (OverlaySettings)Serializer.ReadObject(file) ?? new OverlaySettings();
            settings.LoadProfiles();
            settings.EditorTab = settings.EditorTab ?? ((settings.legacyEditorShowRoute ?? settings.ShowRoute)
                ? EditorMode.Route : EditorMode.Tasks);
            settings.legacyEditorShowRoute = null;
            if (settings.RegexEntries == null)
            {
                settings.RegexEntries = new List<string>();
                if (!String.IsNullOrEmpty(settings.legacySearchRegex)) settings.RegexEntries.Add(settings.legacySearchRegex);
            }
            settings.legacySearchRegex = null;
            return settings;
        }
    }

    private void LoadProfiles()
    {
        if (Profiles == null || Profiles.Count == 0)
            Profiles = new List<OverlayProfile> { MigrateProfile() };
        foreach (OverlayProfile profile in Profiles) profile.NormalizeActs();
        if (ActiveProfileIndex < 0 || ActiveProfileIndex >= Profiles.Count) ActiveProfileIndex = 0;
        EnsureBuiltInProfile();
        if (!Profiles.Any(profile => profile.IsExileLeveling))
            Profiles.Add(OverlayProfile.CreateExileLeveling());
        legacyTasks = null;
        legacyRoutes = null;
        legacyNotes = null;
        legacyRouteNotes = null;
    }

    private void EnsureBuiltInProfile()
    {
        if (Profiles.Any(profile => profile.IsBuiltIn && !profile.IsExileLeveling)) return;
        int index = Profiles.FindIndex(profile => String.Equals(profile.Name, OverlayProfile.BuiltInName, StringComparison.OrdinalIgnoreCase));
        var builtIn = OverlayProfile.CreateBuiltIn();
        if (index >= 0)
        {
            OverlayProfile previous = Profiles[index];
            bool empty = previous.Tasks.All(act => act.Count == 0) && previous.Routes.All(act => act.Count == 0);
            if (empty || BuiltInProfile.MatchesRussian(previous))
            {
                builtIn.CopyPassiveBuildsFrom(previous);
                Profiles[index] = builtIn;
                return;
            }

            string baseName = previous.Name + " — сохранённая копия";
            string name = baseName;
            for (int suffix = 2; Profiles.Any(profile => String.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
                name = baseName + " " + suffix;
            previous.Name = name;
        }

        Profiles.Insert(0, builtIn);
        ActiveProfileIndex++;
    }

    private OverlayProfile MigrateProfile()
    {
        var profile = new OverlayProfile(OverlayProfile.BuiltInName);
        for (int i = 0; i < OverlayProfile.ActCount; i++)
        {
            if (legacyTasks != null && i < legacyTasks.Length && legacyTasks[i] != null)
                profile.Tasks[i] = legacyTasks[i];
            else if (legacyNotes != null && i < legacyNotes.Length && !String.IsNullOrWhiteSpace(legacyNotes[i]))
                profile.Tasks[i].Add(new ActTask { Condition = "", Action = legacyNotes[i] });

            if (legacyRoutes != null && i < legacyRoutes.Length && legacyRoutes[i] != null)
                profile.Routes[i] = legacyRoutes[i];
            else if (legacyRouteNotes != null && i < legacyRouteNotes.Length && !String.IsNullOrWhiteSpace(legacyRouteNotes[i]))
                profile.Routes[i].Add(new RouteNote { Text = legacyRouteNotes[i] });
        }
        return profile;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string temporary = FilePath + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
                Serializer.WriteObject(file, this);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

internal static class Program
{
    public const string AppName = "ExileRoute";

    [STAThread]
    private static void Main()
    {
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool first;
        using (var instance = new Mutex(true, @"Local\PoEActOverlay", out first))
        {
            try
            {
                OverlaySettings settings = OverlaySettings.Load();
                UiText.English = settings.Language == "en";
                if (!first)
                {
                    MessageBox.Show(UiText.Get("Оверлей уже запущен. Откройте его редактор через панель задач.",
                        "The overlay is already running. Open the editor from the taskbar."), AppName);
                    return;
                }
                using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    Application.Run(new EditorForm(settings) { Icon = icon });
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

internal sealed class EditorForm : Form
{
    private readonly OverlaySettings settings;
    private readonly HintOverlay overlay;
    private readonly Timer poll = new Timer { Interval = 350 };
    private readonly Timer autosave = new Timer { Interval = 400 };
    private readonly Panel actPicker;
    private readonly Button[] actButtons = new Button[OverlayProfile.ActCount];
    private readonly Button resetChecksButton;
    private readonly ComboBox profilePicker;
    private readonly Button deleteProfileButton;
    private readonly Label profileStatus;
    private readonly Button russianButton;
    private readonly Button englishButton;
    private readonly Label saveStatus;
    private readonly Button previewButton;
    private readonly Button tasksTab;
    private readonly Button routeTab;
    private readonly Button regexTab;
    private readonly Button passivesTab;
    private readonly Panel editorPanel;
    private readonly Panel taskEditor;
    private readonly RouteEditor routeEditor;
    private readonly RegexEditor regexEditor;
    private readonly PassiveBuildEditor passivesEditor;
    private readonly DataGridView taskGrid;
    private readonly Button addTaskButton;
    private readonly Button editTaskButton;
    private readonly Button deleteTaskButton;
    private readonly Label noTasks;
    private ClientLog log;
    private OverlaySettingsDialog overlaySettingsDialog;
    private int selectedAct = 1;
    private int? previewAct;
    private bool dirty;
#if EDIT_DEFAULT_PROFILE
    private bool defaultProfileDirty;
#endif
    private int activeShortcutId;

    public EditorForm(OverlaySettings settings)
    {
        this.settings = settings;
        UiText.English = settings.Language == "en";
        SuspendLayout();
        if (BuiltInProfile.AuthorBuild)
            UiText.Bind(this, Program.AppName + " — режим автора", Program.AppName + " — Author mode");
        else
            UiText.Bind(this, Program.AppName + " — редактор", Program.AppName + " — Editor");
        Theme.StyleWindow(this);
        ClientSize = new Size(688, 586);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Mark(this, new Rectangle(28, 25, 17, 17));
        Theme.Label(this, "PATH OF EXILE 1   /   КАМПАНИЯ", 9, Theme.Muted, new Rectangle(52, 23, 274, 22), true,
            "PATH OF EXILE 1   /   CAMPAIGN").Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var languageLabel = Theme.Label(this, "ЯЗЫК", 9, Theme.Muted, new Rectangle(346, 22, 92, 24), true, "LANGUAGE");
        languageLabel.TextAlign = ContentAlignment.MiddleRight;
        languageLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        russianButton = Theme.Button(this, "RU", new Rectangle(446, 22, 43, 24), (sender, args) => ChangeLanguage(false));
        englishButton = Theme.Button(this, "EN", new Rectangle(497, 22, 43, 24), (sender, args) => ChangeLanguage(true));
        russianButton.Anchor = englishButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        russianButton.AccessibleName = "Русский";
        englishButton.AccessibleName = "English";
        Theme.Button(this, "Настройки", new Rectangle(551, 22, 109, 24), (sender, args) => ShowOverlaySettings(), english: "Settings")
            .Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Theme.Label(this, "План кампании", 24, Theme.Text, new Rectangle(26, 46, 634, 48), true, "Campaign plan");
        profileStatus = Theme.Label(this, "", 9, Theme.Accent, new Rectangle(453, 57, 207, 28), true);
        profileStatus.TextAlign = ContentAlignment.MiddleRight;
        profileStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        profileStatus.BringToFront();
        Theme.Label(this, "ПРОФИЛЬ", 9, Theme.Accent, new Rectangle(28, 99, 72, 30), true, "PROFILE");
        profilePicker = new ComboBox { Bounds = new Rectangle(106, 102, 165, 27), DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 233,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 10), BackColor = Theme.Surface,
            ForeColor = Theme.Text };
        Theme.StylePicker(profilePicker);
        Controls.Add(profilePicker);
        Theme.Button(this, "Сохранить", new Rectangle(280, 99, 91, 30), (sender, args) => Save(force: true), english: "Save").Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Theme.Button(this, "Создать", new Rectangle(380, 99, 79, 30), (sender, args) => AddProfile(null), english: "New").Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Theme.Button(this, "Копировать", new Rectangle(468, 99, 104, 30), (sender, args) => AddProfile(settings.ActiveProfile), english: "Duplicate").Anchor = AnchorStyles.Top | AnchorStyles.Right;
        deleteProfileButton = Theme.Button(this, "Удалить", new Rectangle(581, 99, 79, 30), (sender, args) => DeleteProfile(), english: "Delete");
        deleteProfileButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        actPicker = Theme.Panel(this, Theme.Background, new Rectangle(28, 140, 632, 79));
        Theme.Label(actPicker, "АКТ КАМПАНИИ", 9, Theme.Accent, new Rectangle(0, 0, 448, 23), true, "CAMPAIGN ACT");
        resetChecksButton = Theme.Button(actPicker, "Сбросить галочки", new Rectangle(480, 0, 152, 24),
            (sender, args) => ResetActCompletion(), english: "Reset checkmarks");
        for (int i = 0; i < actButtons.Length; i++)
        {
            int act = i + 1;
            actButtons[i] = Theme.Button(actPicker, act.ToString(), new Rectangle(i * 64, 27, 56, 36),
                (sender, args) => SelectAct(act));
        }

        editorPanel = Theme.Panel(this, Theme.Background, new Rectangle(28, actPicker.Bottom, 632, 295));
        editorPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        taskEditor = Theme.Panel(editorPanel, Theme.Background, new Rectangle(0, 0, 632, 205));
        deleteTaskButton = Theme.Button(taskEditor, "Удалить", new Rectangle(285, 9, 82, 30), (sender, args) => DeleteTask(), english: "Delete");
        editTaskButton = Theme.Button(taskEditor, "Изменить", new Rectangle(375, 9, 91, 30), (sender, args) => EditTask(SelectedTask), english: "Edit");
        addTaskButton = Theme.Button(taskEditor, "+ Добавить задачу", new Rectangle(474, 9, 144, 30), (sender, args) => EditTask(null), english: "+ Add task");
        addTaskButton.BackColor = Theme.Accent;
        addTaskButton.ForeColor = Theme.Background;
        deleteTaskButton.Anchor = editTaskButton.Anchor = addTaskButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        taskGrid = CreateTaskGrid();
        taskEditor.Controls.Add(taskGrid);
        noTasks = Theme.Label(taskEditor, "", 10, Theme.Muted, new Rectangle(14, 81, 604, 110));
        noTasks.Anchor = taskGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        noTasks.TextAlign = ContentAlignment.MiddleCenter;
        noTasks.BringToFront();
        taskGrid.SelectionChanged += (sender, args) => UpdateTaskButtons();
        taskGrid.CellContentClick += (sender, args) =>
        {
            if (args.ColumnIndex != 0 || args.RowIndex < 0) return;
            var task = (ActTask)taskGrid.Rows[args.RowIndex].Tag;
            SetTaskDone(task, !task.Done);
        };
        taskGrid.CellDoubleClick += (sender, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex > 0) EditTask((ActTask)taskGrid.Rows[args.RowIndex].Tag);
        };
        taskEditor.Dock = DockStyle.Fill;

        routeEditor = new RouteEditor(() =>
        {
            ScheduleProfileSave();
            UpdateOverlay(routeEdited: true);
        }, SetRouteNoteDone) { Dock = DockStyle.Fill };
        editorPanel.Controls.Add(routeEditor);
        regexEditor = new RegexEditor(settings.RegexEntries, ScheduleSave) { Dock = DockStyle.Fill };
        editorPanel.Controls.Add(regexEditor);
        passivesEditor = new PassiveBuildEditor(index => OpenPassiveBuild(index, reload: true), () =>
        {
            if (settings.ShowPassives) ApplyOverlayMode();
            ScheduleSave();
        }) { Dock = DockStyle.Fill };
        editorPanel.Controls.Add(passivesEditor);

        tasksTab = Theme.Button(editorPanel, "Задачи", new Rectangle(14, 9, 58, 30), (sender, args) => SetEditorMode(EditorMode.Tasks), english: "Tasks");
        routeTab = Theme.Button(editorPanel, "Маршрут", new Rectangle(78, 9, 68, 30), (sender, args) => SetEditorMode(EditorMode.Route), english: "Route");
        regexTab = Theme.Button(editorPanel, "Regex", new Rectangle(152, 9, 52, 30), (sender, args) => SetEditorMode(EditorMode.Regex));
        passivesTab = Theme.Button(editorPanel, "Пассивки", new Rectangle(210, 9, 68, 30),
            (sender, args) => SetEditorMode(EditorMode.Passives), english: "Passives");
        tasksTab.BringToFront();
        routeTab.BringToFront();
        regexTab.BringToFront();
        passivesTab.BringToFront();
        var tabDivider = Theme.Panel(editorPanel, Theme.Divider, new Rectangle(14, 44, 604, 1));
        tabDivider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        tabDivider.BringToFront();
        Theme.Panel(this, Theme.Divider, new Rectangle(28, 523, 632, 1)).Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        previewButton = Theme.Button(this, "Предпросмотр", new Rectangle(28, 532, 138, 34), (sender, args) =>
        {
            previewAct = previewAct.HasValue ? (int?)null : selectedAct;
            UpdateOverlay();
        });
        previewButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        saveStatus = Theme.Label(this, UiText.Get("Автосохранение", "Autosave"), 9, Theme.Muted, new Rectangle(180, 530, 480, 38));
        saveStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        overlay = new HintOverlay(OpenEditor, StoreOverlayBounds, SetTaskDone, SetRouteNoteDone, ToggleCampaignMode, TogglePassives,
            index => OpenPassiveBuild(index, reload: false),
            settings.OverlayTransparencyPercent);
        log = new ClientLog(settings.LogPath ?? ClientLog.Discover());
        RefreshProfilePicker();
        UpdateLanguageControls();
        ApplyEditorMode();
        LoadPassiveProfile();
        SelectAct(1);
        poll.Tick += (sender, args) => Poll();
        autosave.Tick += (sender, args) => Save();
        ResumeLayout(false);
        PerformAutoScale();
        MinimumSize = SizeFromClientSize(new Size(ClientSize.Width, (int)Math.Round(540 * CurrentAutoScaleDimensions.Height / 96f)));
        Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(settings.EditorWidth ?? Width, workArea.Width),
            Math.Min(settings.EditorHeight ?? Height, workArea.Height));
    }

    private DataGridView CreateTaskGrid()
    {
        var grid = Theme.Grid(new Rectangle(14, 50, 604, 142));
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", Width = 38, FlatStyle = FlatStyle.Flat });
        grid.Columns.Add(new DataGridViewTextBoxColumn { FillWeight = 42,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { FillWeight = 58,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        return grid;
    }

    protected override void OnShown(EventArgs args)
    {
        base.OnShown(args);
        TryRegisterShortcut(settings.ClickThroughShortcut);
        overlay.ApplySavedBounds(settings.Left, settings.Top, settings.OverlayWidth, settings.OverlayHeight);
        Poll();
        overlay.Show();
        poll.Start();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.HotkeyMessage && activeShortcutId != 0 && message.WParam.ToInt32() == activeShortcutId)
            overlay.ToggleClickThrough();
        base.WndProc(ref message);
    }

    protected override void OnResizeEnd(EventArgs args)
    {
        base.OnResizeEnd(args);
        if (WindowState != FormWindowState.Normal || (settings.EditorWidth == Width && settings.EditorHeight == Height)) return;
        settings.EditorWidth = Width;
        settings.EditorHeight = Height;
        ScheduleSave();
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (!Save() && args.CloseReason == CloseReason.UserClosing)
        {
            MessageBox.Show(this, saveStatus.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            args.Cancel = true;
        }
        base.OnFormClosing(args);
    }

    protected override void OnFormClosed(FormClosedEventArgs args)
    {
        if (activeShortcutId != 0) Native.UnregisterHotKey(Handle, activeShortcutId);
        poll.Dispose();
        autosave.Dispose();
        overlay.Dispose();
        base.OnFormClosed(args);
    }

    private void OpenEditor()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (settings.Mode == HintMode.Passives) SetEditorMode(EditorMode.Passives);
        Activate();
    }

    private void ShowOverlaySettings()
    {
        using (var dialog = new OverlaySettingsDialog(settings.OverlayTransparencyPercent, percent =>
        {
            settings.OverlayTransparency = percent;
            overlay.SetTransparency(percent);
            ScheduleSave();
        }, settings.ClickThroughShortcut, activeShortcutId != 0, ApplyShortcut, () => SetLog(null), BrowseLog))
        {
            overlaySettingsDialog = dialog;
            try
            {
                dialog.UpdateLog(log.Path, Status(), log.Available);
                dialog.ShowDialog(this);
            }
            finally
            {
                overlaySettingsDialog = null;
            }
        }
    }

    private bool ApplyShortcut(Keys shortcut)
    {
        if (!TryRegisterShortcut(shortcut)) return false;
        settings.ClickThroughShortcut = shortcut;
        ScheduleSave();
        return true;
    }

    private bool TryRegisterShortcut(Keys shortcut)
    {
        if (activeShortcutId != 0 && shortcut == settings.ClickThroughShortcut) return true;
        int id = activeShortcutId == Native.LockShortcut ? Native.AlternateLockShortcut : Native.LockShortcut;
        if (!OverlayHotkey.Register(Handle, id, shortcut)) return false;

        // Keep the current shortcut available until Windows accepts the replacement.
        if (activeShortcutId != 0 && !Native.UnregisterHotKey(Handle, activeShortcutId))
        {
            Native.UnregisterHotKey(Handle, id);
            return false;
        }

        activeShortcutId = id;
        overlay.SetClickThroughShortcut(OverlayHotkey.Format(shortcut));
        return true;
    }

    private void StoreOverlayBounds(Rectangle bounds)
    {
        settings.Left = bounds.Left;
        settings.Top = bounds.Top;
        settings.OverlayWidth = bounds.Width;
        settings.OverlayHeight = bounds.Height;
        ScheduleSave();
    }

    private void RefreshProfilePicker()
    {
        profilePicker.SelectedIndexChanged -= ProfileSelected;
        profilePicker.BeginUpdate();
        profilePicker.Items.Clear();
        foreach (OverlayProfile profile in settings.Profiles) profilePicker.Items.Add(profile);
        profilePicker.SelectedIndex = settings.ActiveProfileIndex;
        profilePicker.EndUpdate();
        profilePicker.SelectedIndexChanged += ProfileSelected;
    }

    private void ProfileSelected(object sender, EventArgs args)
    {
        int index = profilePicker.SelectedIndex;
        if (index >= 0 && index != settings.ActiveProfileIndex) ChangeProfile(index);
    }

    private void AddProfile(OverlayProfile source)
    {
        using (var dialog = new ProfileNameDialog(settings.Profiles, source))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            OverlayProfile profile = source == null ? new OverlayProfile(dialog.ProfileName) : source.Copy(dialog.ProfileName);
            ChangeProfile(settings.Profiles.Count, profile);
        }
    }

    private void ChangeProfile(int index, OverlayProfile added = null)
    {
        int previous = settings.ActiveProfileIndex;
        if (added != null) settings.Profiles.Add(added);
        settings.ActiveProfileIndex = index;
        if (!Save(force: true))
        {
            settings.ActiveProfileIndex = previous;
            if (added != null) settings.Profiles.RemoveAt(index);
            RefreshProfilePicker();
            return;
        }

        if (added != null) RefreshProfilePicker();
        LoadPassiveProfile();
        SelectAct(selectedAct);
    }

    private void DeleteProfile()
    {
        if (settings.Profiles.Count <= 1 || settings.ActiveProfile.IsBuiltIn) return;
        int index = settings.ActiveProfileIndex;
        OverlayProfile profile = settings.ActiveProfile;
        using (var dialog = new DeleteProfileDialog(profile.Name))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
        }

        settings.Profiles.RemoveAt(index);
        settings.ActiveProfileIndex = Math.Min(index, settings.Profiles.Count - 1);
        if (!Save(force: true))
        {
            settings.Profiles.Insert(index, profile);
            settings.ActiveProfileIndex = index;
            return;
        }

        RefreshProfilePicker();
        LoadPassiveProfile();
        SelectAct(selectedAct);
    }

    private void BrowseLog(IWin32Window owner)
    {
        using (var dialog = new OpenFileDialog { Title = UiText.Get("Выберите журнал Path of Exile 1", "Choose the Path of Exile 1 log"),
            FileName = "Client.txt", Filter = UiText.Get("Журнал Path of Exile (Client.txt)|Client.txt|Текстовые файлы (*.txt)|*.txt",
                "Path of Exile log (Client.txt)|Client.txt|Text files (*.txt)|*.txt") })
        {
            if (log.Path != null) dialog.InitialDirectory = Path.GetDirectoryName(log.Path);
            if (dialog.ShowDialog(owner) == DialogResult.OK) SetLog(dialog.FileName);
        }
    }

    private void SetLog(string path)
    {
        settings.LogPath = path;
        log = new ClientLog(path ?? ClientLog.Discover());
        previewAct = null;
        ScheduleSave();
        Poll();
    }

    private void Poll()
    {
        log.Poll();
        if (overlaySettingsDialog != null)
            overlaySettingsDialog.UpdateLog(log.Path, Status(), log.Available);
        UpdateOverlay();
    }

    private string Status()
    {
        if (log.Path == null) return UiText.Get("Журнал игры не найден", "Game log not found");
        if (!log.Available) return UiText.Get("Журнал пока недоступен", "Game log is unavailable");
        if (log.Act.HasValue)
        {
            string name = CurrentZone;
            return UiText.Get("Акт ", "Act ") + log.Act.Value + (name.Length > 0 ? " · " + name : "");
        }
        if (log.AreaId != null) return UiText.Get("Вне кампании", "Outside the campaign") + (log.Zone.Length > 0 ? " · " + log.Zone : "");
        return UiText.Get("Ожидание перехода в игровую область", "Waiting to enter a game area");
    }

    private string CurrentZone { get { return CampaignAreas.NameOf(log.AreaId, log.Zone); } }

    private void SelectAct(int act)
    {
        selectedAct = act;
        OverlayProfile profile = settings.ActiveProfile;
        profileStatus.Visible = profile.IsBuiltIn;
        profileStatus.Text = profile.ContentReadOnly
            ? UiText.Get("Встроенный профиль", "Built-in profile")
            : UiText.Get("Редактирование оригинала · RU", "Editing the original · EN");
        deleteProfileButton.Enabled = !profile.IsBuiltIn && settings.Profiles.Count > 1;
        for (int i = 0; i < actButtons.Length; i++)
        {
            bool selected = i + 1 == selectedAct;
            actButtons[i].BackColor = selected ? Theme.Accent : Theme.Control;
            actButtons[i].ForeColor = selected ? Theme.Background : Theme.Text;
        }
        RefreshTaskGrid();
        routeEditor.LoadAct(act, settings.Routes[act - 1], profile.ContentReadOnly);
        UpdateResetButton();
        if (previewAct.HasValue) previewAct = act;
        UpdateOverlay();
    }

    private void SetEditorMode(EditorMode mode)
    {
        if (settings.EditorTab == mode) return;
        settings.EditorTab = mode;
        ApplyEditorMode();
        ScheduleSave();
    }

    private void ToggleCampaignMode()
    {
        settings.Mode = settings.ShowRoute ? HintMode.Tasks : HintMode.Route;
        ApplyOverlayMode();
        ScheduleSave();
    }

    private void TogglePassives()
    {
        settings.Mode = settings.ShowPassives ? settings.CampaignMode : HintMode.Passives;
        ApplyOverlayMode();
        ScheduleSave();
    }

    private void LoadPassiveProfile()
    {
        passivesEditor.LoadProfile(settings.ActiveProfile);
        ApplyOverlayMode();
    }

    private void OpenPassiveBuild(int index, bool reload)
    {
        OverlayProfile profile = settings.ActiveProfile;
        if (index < 0 || index >= profile.PassiveBuilds.Count) return;
        profile.ActivePassiveBuildIndex = index;
        settings.Mode = HintMode.Passives;
        passivesEditor.SelectBuild(index);
        ApplyOverlayMode(reload);
        if (reload) overlay.BringToFront();
        ScheduleSave();
    }

    private void ApplyOverlayMode(bool reloadPassives = false)
    {
        HintMode mode = settings.Mode;
        overlay.SetMode(mode, settings.ShowRoute);
        previewButton.Enabled = mode != HintMode.Passives;
        if (mode == HintMode.Passives)
            overlay.SetPassiveBuilds(settings.ActiveProfile.PassiveBuilds, settings.ActiveProfile.ActivePassiveBuildIndex, reloadPassives);
    }

    private void ChangeLanguage(bool english)
    {
        if (UiText.English == english) return;
        settings.Language = english ? "en" : "ru";
        UiText.English = english;
        UiText.Refresh(this);
        UpdateLanguageControls();
        routeEditor.RefreshLanguage();
        overlay.RefreshLanguage();
        RefreshProfilePicker();
        if (settings.ActiveProfile.IsBuiltIn) SelectAct(selectedAct);
        ScheduleSave();
        Poll();
    }

    private void UpdateLanguageControls()
    {
        russianButton.BackColor = UiText.English ? Theme.Control : Theme.Accent;
        russianButton.ForeColor = UiText.English ? Theme.Text : Theme.Background;
        englishButton.BackColor = UiText.English ? Theme.Accent : Theme.Control;
        englishButton.ForeColor = UiText.English ? Theme.Background : Theme.Text;
        profilePicker.AccessibleName = UiText.Get("Активный профиль", "Active profile");
        passivesEditor.RefreshLanguage();
        regexEditor.RefreshLanguage();
        taskGrid.Columns[0].HeaderCell.ToolTipText = UiText.Get("Выполнено", "Completed");
        taskGrid.Columns[1].HeaderText = UiText.Get("Условие", "Condition");
        taskGrid.Columns[2].HeaderText = UiText.Get("Действие", "Action");
    }

    private void ApplyEditorMode()
    {
        bool regex = settings.EditorTab == EditorMode.Regex;
        bool passives = settings.EditorTab == EditorMode.Passives;
        actPicker.Visible = !regex && !passives;
        int editorTop = regex || passives ? actPicker.Top : actPicker.Bottom;
        editorPanel.SetBounds(editorPanel.Left, editorTop, editorPanel.Width, editorPanel.Bottom - editorTop);
        ShowEditorTab(tasksTab, taskEditor, settings.EditorTab == EditorMode.Tasks);
        ShowEditorTab(routeTab, routeEditor, settings.EditorTab == EditorMode.Route);
        ShowEditorTab(regexTab, regexEditor, regex);
        ShowEditorTab(passivesTab, passivesEditor, passives);
    }

    private static void ShowEditorTab(Button tab, Panel panel, bool selected)
    {
        panel.Visible = selected;
        Theme.SetTabState(tab, selected);
    }

    private List<ActTask> SelectedActTasks { get { return settings.Tasks[selectedAct - 1]; } }

    private ActTask SelectedTask
    {
        get { return taskGrid.SelectedRows.Count == 0 ? null : (ActTask)taskGrid.SelectedRows[0].Tag; }
    }

    private void RefreshTaskGrid(ActTask selection = null)
    {
        taskGrid.Rows.Clear();
        foreach (ActTask task in SelectedActTasks)
        {
            int index = taskGrid.Rows.Add(task.Done, task.Condition, task.Action);
            DataGridViewRow row = taskGrid.Rows[index];
            row.Tag = task;
            Theme.SetChecklistRowColor(row, task.Done);
            if (task == selection) taskGrid.CurrentCell = row.Cells[1];
        }
        noTasks.Visible = SelectedActTasks.Count == 0;
        if (settings.ActiveProfile.ContentReadOnly)
            UiText.Bind(noTasks, "В этом акте нет задач. Откройте вкладку «Маршрут».", "This act has no tasks. Open the Route tab.");
        else
            UiText.Bind(noTasks, "Пока нет задач. Добавьте первую задачу для этого акта.", "No tasks yet. Add your first task for this act.");
        UpdateTaskButtons();
    }

    private void UpdateTaskButtons()
    {
        addTaskButton.Enabled = !settings.ActiveProfile.ContentReadOnly;
        editTaskButton.Enabled = addTaskButton.Enabled && SelectedTask != null;
        deleteTaskButton.Enabled = editTaskButton.Enabled;
    }

    private void EditTask(ActTask existing)
    {
        if (settings.ActiveProfile.ContentReadOnly) return;
        using (var dialog = new TaskDialog(existing))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            ActTask task = existing ?? new ActTask();
            task.Condition = dialog.Condition;
            task.Action = dialog.Action;
            if (existing == null) SelectedActTasks.Add(task);
            TasksEdited(task);
        }
    }

    private void DeleteTask()
    {
        if (settings.ActiveProfile.ContentReadOnly) return;
        ActTask task = SelectedTask;
        if (task == null) return;
        if (MessageBox.Show(this, UiText.Get("Удалить выбранную задачу?", "Delete the selected task?"), UiText.Get("Задачи по актам", "Campaign tasks"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        SelectedActTasks.Remove(task);
        TasksEdited(null);
    }

    private void TasksEdited(ActTask selection)
    {
        RefreshTaskGrid(selection);
        ScheduleProfileSave();
        UpdateOverlay(true);
    }

    private void SetTaskDone(ActTask task, bool done)
    {
        if (task.Done == done) return;
        task.Done = done;
        foreach (DataGridViewRow row in taskGrid.Rows)
        {
            if (row.Tag != task) continue;
            row.Cells[0].Value = done;
            Theme.SetChecklistRowColor(row, done);
            break;
        }
        overlay.SyncCompletion(task);
        UpdateResetButton();
        ScheduleSave();
    }

    private void SetRouteNoteDone(RouteNote note, bool done)
    {
        if (note.Done == done) return;
        note.Done = done;
        routeEditor.SyncCompletion(note);
        overlay.SyncCompletion(note);
        UpdateResetButton();
        ScheduleSave();
    }

    private void UpdateResetButton()
    {
        resetChecksButton.Enabled = SelectedActTasks.Any(task => task.Done) || settings.Routes[selectedAct - 1].Any(note => note.Done);
    }

    private void ResetActCompletion()
    {
        if (!resetChecksButton.Enabled) return;
        int act = selectedAct;
        OverlayProfile profile = settings.ActiveProfile;
        string prompt = String.Format(UiText.Get("Снять все галочки в задачах и маршруте акта {0} в профиле «{1}»?",
            "Clear all checkmarks in Act {0} tasks and route notes in the “{1}” profile?"), act, profile);
        if (MessageBox.Show(this, prompt, resetChecksButton.Text, MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        foreach (ActTask task in profile.Tasks[act - 1]) SetTaskDone(task, false);
        foreach (RouteNote note in profile.Routes[act - 1]) SetRouteNoteDone(note, false);
    }

    private void ScheduleProfileSave()
    {
#if EDIT_DEFAULT_PROFILE
        OverlayProfile profile = settings.ActiveProfile;
        if (profile.IsBuiltIn && !profile.ContentReadOnly)
        {
            BuiltInProfile.UpdateDefault(profile);
            defaultProfileDirty = true;
        }
#endif
        UpdateResetButton();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        dirty = true;
        saveStatus.Text = UiText.Get("Сохранение…", "Saving…");
        saveStatus.ForeColor = Theme.Muted;
        autosave.Stop();
        autosave.Start();
    }

    private bool Save(bool force = false)
    {
        autosave.Stop();
        if (!dirty && !force) return true;
        try
        {
#if EDIT_DEFAULT_PROFILE
            if (defaultProfileDirty)
            {
                BuiltInProfile.SaveDefault();
                defaultProfileDirty = false;
            }
#endif
            settings.Save();
            dirty = false;
            saveStatus.Text = UiText.Get("Сохранено на этом компьютере", "Saved on this computer");
            saveStatus.ForeColor = Theme.Accent;
            return true;
        }
        catch (IOException error)
        {
            return SaveError(error);
        }
        catch (UnauthorizedAccessException error) { return SaveError(error); }
        catch (SerializationException error) { return SaveError(error); }
    }

    private bool SaveError(Exception error)
    {
        saveStatus.Text = UiText.Get("Ошибка сохранения. ", "Could not save. ") + error.Message;
        saveStatus.ForeColor = Theme.Warning;
        return false;
    }

    private void UpdateOverlay(bool tasksEdited = false, bool routeEdited = false)
    {
        string preview = UiText.Get("Предпросмотр", "Preview");
        previewButton.Text = previewAct.HasValue ? UiText.Get("Вернуться в авто", "Back to auto") : preview;
        int? act = previewAct ?? log.Act;
        string title = act.HasValue ? UiText.Get("Акт ", "Act ") + act.Value : UiText.Get("Кампания", "Campaign");
        string subtitle = previewAct.HasValue ? preview : (act.HasValue ? CurrentZone : Status());
        List<ActTask> tasks = act.HasValue ? settings.Tasks[act.Value - 1] : null;
        string connectLog = UiText.Get("Подключите журнал игры в настройках редактора.", "Connect the game log in the editor settings.");
        string noTasks = settings.ActiveProfile.IsBuiltIn ? UiText.Get("В этом акте нет задач. Маршрут доступен в соседнем режиме.",
            "This act has no tasks. Switch to Route to see the walkthrough.") : UiText.Get("Задач пока нет. Добавьте их через «Правка».", "No tasks yet. Use Edit to add some.");
        string empty = act.HasValue ? noTasks :
            log.Available ? UiText.Get("Задачи появятся при входе в область кампании.", "Tasks appear when you enter a campaign area.") : connectLog;
        overlay.SetContent(title, subtitle, tasks, empty, tasksEdited && act == selectedAct);
        List<RouteNote> route = act.HasValue ? settings.Routes[act.Value - 1] : null;
        string emptyRoute = act.HasValue ? UiText.Get("Маршрут пока пуст. Добавьте заметки через «Правка».", "No route notes yet. Use Edit to add some.") :
            log.Available ? UiText.Get("Маршрут появится при входе в область кампании.", "Route notes appear when you enter a campaign area.") : connectLog;
        overlay.SetRouteNotes(route, emptyRoute, routeEdited && act == selectedAct);
    }
}

internal static class OverlayHotkey
{
    public const Keys Default = Keys.Control | Keys.Shift | Keys.D9;
    public static readonly Keys[] KeyChoices = KeyRange(Keys.D0, Keys.D9)
        .Concat(KeyRange(Keys.A, Keys.Z))
        .Concat(KeyRange(Keys.F1, Keys.F24))
        .Concat(KeyRange(Keys.NumPad0, Keys.NumPad9))
        .Concat(new[] { Keys.Space, Keys.Enter, Keys.Tab, Keys.Escape, Keys.Back, Keys.Insert, Keys.Delete,
            Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Left, Keys.Up, Keys.Right, Keys.Down })
        .ToArray();
    private static readonly KeysConverter KeyNames = new KeysConverter();

    private static IEnumerable<Keys> KeyRange(Keys first, Keys last)
    {
        for (Keys key = first; key <= last; key++) yield return key;
    }

    public static bool IsValid(Keys shortcut)
    {
        Keys key = shortcut & Keys.KeyCode;
        Keys modifiers = shortcut & Keys.Modifiers;
        return (modifiers & ~(Keys.Control | Keys.Shift | Keys.Alt)) == Keys.None && KeyChoices.Contains(key) &&
            (modifiers != Keys.None || (key >= Keys.F1 && key <= Keys.F24));
    }

    public static string KeyName(Keys key) { return KeyNames.ConvertToInvariantString(key); }

    public static string Format(Keys shortcut)
    {
        return ((shortcut & Keys.Control) != 0 ? "Ctrl+" : "") +
            ((shortcut & Keys.Shift) != 0 ? "Shift+" : "") +
            ((shortcut & Keys.Alt) != 0 ? "Alt+" : "") +
            KeyName(shortcut & Keys.KeyCode);
    }

    public static bool Register(IntPtr window, int id, Keys shortcut)
    {
        if (!IsValid(shortcut)) return false;
        uint modifiers = Native.NoHotkeyRepeat;
        if ((shortcut & Keys.Control) != 0) modifiers |= Native.HotkeyControl;
        if ((shortcut & Keys.Shift) != 0) modifiers |= Native.HotkeyShift;
        if ((shortcut & Keys.Alt) != 0) modifiers |= Native.HotkeyAlt;
        return Native.RegisterHotKey(window, id, modifiers, (uint)(shortcut & Keys.KeyCode));
    }
}

internal sealed class OverlaySettingsDialog : Form
{
    private readonly CheckBox control;
    private readonly CheckBox shift;
    private readonly CheckBox alt;
    private readonly ComboBox keyPicker;
    private readonly Button apply;
    private readonly Button reset;
    private readonly Label shortcutStatus;
    private readonly Label trackingStatus;
    private readonly Label logPath;
    private readonly Func<Keys, bool> applyShortcut;
    private Keys currentShortcut;
    private bool shortcutRegistered;

    public OverlaySettingsDialog(int transparency, Action<int> changed, Keys shortcut, bool registered,
        Func<Keys, bool> applyShortcut, Action findLog, Action<IWin32Window> browseLog)
    {
        this.applyShortcut = applyShortcut;
        currentShortcut = shortcut;
        shortcutRegistered = registered;
        SuspendLayout();
        Text = UiText.Get("Настройки", "Settings");
        Theme.StyleWindow(this);
        ClientSize = new Size(460, 482);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Label(this, "Прозрачность оверлея", 15, Theme.Text, new Rectangle(20, 14, 320, 35),
            true, "Overlay transparency");
        var value = Theme.Label(this, transparency + "%", 15, Theme.Accent, new Rectangle(345, 14, 95, 35), true);
        value.TextAlign = ContentAlignment.MiddleRight;
        var slider = new Theme.SignalSlider
        {
            Bounds = new Rectangle(20, 57, 420, 34), Minimum = 0, Maximum = OverlaySettings.MaxOverlayTransparency,
            Value = transparency, SmallChange = 1, LargeChange = 10, TickStyle = TickStyle.None, AutoSize = false,
            BackColor = Theme.Background, AccessibleName = UiText.Get("Прозрачность оверлея", "Overlay transparency")
        };
        Controls.Add(slider);
        slider.ValueChanged += (sender, args) =>
        {
            value.Text = slider.Value + "%";
            changed(slider.Value);
        };
        Theme.Label(this, "0% · Непрозрачно", 9, Theme.Muted, new Rectangle(20, 92, 196, 22),
            english: "0% · Opaque");
        var high = Theme.Label(this, "80% · Прозрачнее", 9, Theme.Muted, new Rectangle(244, 92, 196, 22),
            english: "80% · More transparent");
        high.TextAlign = ContentAlignment.MiddleRight;
        Theme.Label(this, "Прозрачность сохраняется автоматически.", 9, Theme.Muted,
            new Rectangle(20, 118, 420, 24), english: "Transparency saves automatically.");

        Theme.Panel(this, Theme.Divider, new Rectangle(20, 151, 420, 1));
        Theme.Label(this, "Горячая клавиша режима «Сквозь»", 13, Theme.Text, new Rectangle(20, 161, 420, 32),
            true, "Click-through shortcut");
        control = AddModifier("Ctrl", 20);
        shift = AddModifier("Shift", 77);
        alt = AddModifier("Alt", 134);
        keyPicker = new ComboBox
        {
            Bounds = new Rectangle(189, 203, 127, 30), DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Control, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9),
            FormattingEnabled = true, AccessibleName = UiText.Get("Клавиша сочетания", "Shortcut key")
        };
        Theme.StylePicker(keyPicker);
        keyPicker.Format += (sender, args) => args.Value = OverlayHotkey.KeyName((Keys)args.ListItem);
        keyPicker.Items.AddRange(OverlayHotkey.KeyChoices.Select(key => (object)key).ToArray());
        Controls.Add(keyPicker);
        apply = Theme.Button(this, "Применить", new Rectangle(330, 200, 110, 30),
            (sender, args) => ApplyShortcut(SelectedShortcut), english: "Apply");
        shortcutStatus = Theme.Label(this, "", 9, Theme.Muted, new Rectangle(20, 234, 420, 34));
        reset = Theme.Button(this, "По умолчанию", new Rectangle(20, 270, 133, 30), (sender, args) =>
        {
            SelectShortcut(OverlayHotkey.Default);
            ApplyShortcut(OverlayHotkey.Default);
        }, english: "Restore default");

        SelectShortcut(shortcut);
        control.CheckedChanged += (sender, args) => UpdateShortcutStatus();
        shift.CheckedChanged += (sender, args) => UpdateShortcutStatus();
        alt.CheckedChanged += (sender, args) => UpdateShortcutStatus();
        keyPicker.SelectedIndexChanged += (sender, args) => UpdateShortcutStatus();
        UpdateShortcutStatus();

        var connection = Theme.Panel(this, Theme.Surface, new Rectangle(20, 316, 420, 106));
        trackingStatus = Theme.Label(connection, "", 11, Theme.Accent, new Rectangle(14, 8, 392, 24), true);
        logPath = Theme.Label(connection, "", 9, Theme.Muted, new Rectangle(14, 35, 392, 25));
        Theme.Button(connection, "Автопоиск", new Rectangle(206, 65, 86, 29), (sender, args) => findLog(), english: "Find log");
        Theme.Button(connection, "Выбрать…", new Rectangle(300, 65, 106, 29), (sender, args) => browseLog(this), english: "Browse…");

        var close = Theme.Button(this, "Готово", new Rectangle(330, 438, 110, 30), english: "Done");
        close.DialogResult = DialogResult.OK;
        close.BackColor = Theme.Accent;
        close.ForeColor = Theme.Background;
        AcceptButton = CancelButton = close;
        ActiveControl = slider;
        ResumeLayout(false);
    }

    public void UpdateLog(string path, string status, bool available)
    {
        trackingStatus.Text = status;
        trackingStatus.ForeColor = available ? Theme.Accent : Theme.Warning;
        logPath.Text = path ?? UiText.Get(@"Выберите файл logs\Client.txt в папке игры", @"Choose logs\Client.txt in the game folder");
    }

    private CheckBox AddModifier(string name, int x)
    {
        var check = new CheckBox
        {
            Text = name, Bounds = new Rectangle(x, 200, 55, 30), ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9)
        };
        Controls.Add(check);
        return check;
    }

    private Keys SelectedShortcut
    {
        get
        {
            Keys shortcut = keyPicker.SelectedItem as Keys? ?? Keys.None;
            if (control.Checked) shortcut |= Keys.Control;
            if (shift.Checked) shortcut |= Keys.Shift;
            if (alt.Checked) shortcut |= Keys.Alt;
            return shortcut;
        }
    }

    private void SelectShortcut(Keys shortcut)
    {
        keyPicker.SelectedItem = shortcut & Keys.KeyCode;
        control.Checked = (shortcut & Keys.Control) != 0;
        shift.Checked = (shortcut & Keys.Shift) != 0;
        alt.Checked = (shortcut & Keys.Alt) != 0;
    }

    private void UpdateShortcutStatus()
    {
        Keys selected = SelectedShortcut;
        bool valid = OverlayHotkey.IsValid(selected);
        apply.Enabled = valid && (!shortcutRegistered || selected != currentShortcut);
        reset.Enabled = currentShortcut != OverlayHotkey.Default || selected != OverlayHotkey.Default;
        shortcutStatus.ForeColor = valid && shortcutRegistered ? Theme.Muted : Theme.Warning;
        shortcutStatus.Text = !valid
            ? UiText.Get("Добавьте Ctrl, Shift или Alt либо выберите клавишу F1–F24.",
                "Add Ctrl, Shift, or Alt, or choose a key from F1–F24.")
            : CurrentShortcutStatus();
    }

    private string CurrentShortcutStatus()
    {
        string name = OverlayHotkey.Format(currentShortcut);
        return shortcutRegistered
            ? String.Format(UiText.Get("Действует {0}", "Active shortcut · {0}"), name)
            : String.Format(UiText.Get("{0} недоступно", "{0} is unavailable"), name);
    }

    private void ApplyShortcut(Keys shortcut)
    {
        if (!applyShortcut(shortcut))
        {
            shortcutStatus.ForeColor = Theme.Warning;
            shortcutStatus.Text = String.Format(UiText.Get("{0} недоступно.", "{0} is unavailable."), OverlayHotkey.Format(shortcut));
            if (shortcutRegistered) shortcutStatus.Text += " " + CurrentShortcutStatus() + ".";
            return;
        }

        currentShortcut = shortcut;
        shortcutRegistered = true;
        UpdateShortcutStatus();
    }
}

internal sealed class TaskDialog : Form
{
    private readonly TextBox condition;
    private readonly TextBox action;

    public string Condition { get { return condition.Text.Trim(); } }
    public string Action { get { return action.Text.Trim(); } }

    public TaskDialog(ActTask existing)
    {
        SuspendLayout();
        Text = existing == null ? UiText.Get("Новая задача", "New task") : UiText.Get("Изменить задачу", "Edit task");
        ClientSize = new Size(472, 337);
        Theme.StyleWindow(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Label(this, "Условие", 11, Theme.Accent, new Rectangle(20, 15, 270, 22), true, "Condition");
        Theme.Button(this, "Вставить камень", new Rectangle(303, 13, 149, 24), (sender, args) => InsertGem(condition), english: "Insert gem");
        Theme.Label(this, "Например, получить 12 уровень. Поле можно оставить пустым.", 9,
            Theme.Muted, new Rectangle(20, 39, 432, 20), english: "For example, reach level 12. This field is optional.");
        condition = AddField(new Rectangle(20, 67, 432, 57), existing == null ? "" : existing.Condition);
        condition.AccessibleName = UiText.Get("Условие", "Condition");
        Theme.Label(this, "Действие", 11, Theme.Accent, new Rectangle(20, 141, 270, 22), true, "Action");
        Theme.Button(this, "Вставить камень", new Rectangle(303, 139, 149, 24), (sender, args) => InsertGem(action), english: "Insert gem");
        Theme.Label(this, "Например, забрать умение «Ядовитая смесь».", 9,
            Theme.Muted, new Rectangle(20, 165, 432, 20), english: "For example, pick up a skill gem.");
        action = AddField(new Rectangle(20, 193, 432, 79), existing == null ? "" : existing.Action);
        action.AccessibleName = UiText.Get("Действие", "Action");

        var cancel = Theme.Button(this, "Отмена", new Rectangle(234, 288, 98, 32), english: "Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var save = Theme.Button(this, "Сохранить", new Rectangle(340, 288, 112, 32), english: "Save");
        save.DialogResult = DialogResult.OK;
        save.BackColor = Theme.Accent;
        save.ForeColor = Theme.Background;
        save.Enabled = Action.Length > 0;
        action.TextChanged += (sender, args) => save.Enabled = Action.Length > 0;
        CancelButton = cancel;
        AcceptButton = save;
        ActiveControl = condition;
        ResumeLayout(false);
    }

    private void InsertGem(TextBox target)
    {
        int start = target.SelectionStart;
        int length = target.SelectionLength;
        using (var picker = new GemPickerDialog())
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            target.Focus();
            target.Select(start, length);
            target.SelectedText = picker.SelectedName;
        }
    }

    private TextBox AddField(Rectangle bounds, string text)
    {
        var field = new TextBox { Bounds = bounds, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface,
            ForeColor = Theme.Text, Font = new Font("Segoe UI", 11), Text = text ?? "" };
        Theme.StyleScrollBars(field);
        Controls.Add(field);
        field.SelectionStart = field.TextLength;
        return field;
    }
}

internal sealed class HintOverlay : Form
{
    private readonly Label modeLabel;
    private readonly Label heading;
    private readonly Label subtitle;
    private readonly Label progress;
    private readonly Label footer;
    private readonly FlowLayoutPanel body;
    private readonly FlowLayoutPanel routeBody;
    private readonly PassiveTreeView passiveView;
    private readonly Dictionary<ActTask, CheckBox> checkboxes = new Dictionary<ActTask, CheckBox>();
    private readonly Dictionary<RouteNote, CheckBox> routeCheckboxes = new Dictionary<RouteNote, CheckBox>();
    private readonly Button modeButton;
    private readonly Button passivesButton;
    private readonly Button lockButton;
    private readonly ToolTip passivesTip = new ToolTip { ShowAlways = true };
    private readonly Action<Rectangle> boundsChanged;
    private readonly Action<ActTask, bool> taskDone;
    private readonly Action<RouteNote, bool> routeNoteDone;
    private readonly Native.MouseHookCallback wheelCallback;
    private readonly Timer resize = new Timer { Interval = 16 };
    private IntPtr wheelHook;
    private Point dragCursor;
    private Point dragLocation;
    private Point resizeCursor;
    private Rectangle resizeOrigin;
    private Rectangle resizeScreen;
    private int resizeHorizontal;
    private int resizeVertical;
    private bool dragging;
    private bool locked;
    private string clickThroughShortcut;
    private HintMode mode;
    private bool showRoute;
    private int transparencyPercent;
    private List<ActTask> displayedTasks;
    private string displayedEmptyText;
    private List<RouteNote> displayedRoute;
    private string displayedRouteEmptyText;

    public HintOverlay(Action edit, Action<Rectangle> boundsChanged, Action<ActTask, bool> taskDone,
        Action<RouteNote, bool> routeNoteDone, Action switchCampaign, Action togglePassives,
        Action<int> selectBuild, int transparency = OverlaySettings.DefaultOverlayTransparency)
    {
        this.boundsChanged = boundsChanged;
        this.taskDone = taskDone;
        this.routeNoteDone = routeNoteDone;
        transparencyPercent = transparency;
        wheelCallback = HandleWheel;
        SuspendLayout();
        Text = Program.AppName;
        BackColor = Theme.Background;
        ClientSize = new Size(380, 324);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Panel(this, Theme.Divider, new Rectangle(0, 0, 1, 324)).Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        var mark = Theme.Mark(this, new Rectangle(16, 13, 16, 16));
        modeLabel = Theme.Label(this, "", 8, Theme.Muted, new Rectangle(36, 10, 75, 21), true);
        heading = Theme.Label(this, "", 22, Theme.Text, new Rectangle(16, 36, 204, 38), true);
        progress = Theme.Label(this, "", 10, Theme.Accent, new Rectangle(221, 43, 143, 25), true);
        progress.TextAlign = ContentAlignment.MiddleRight;
        progress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        subtitle = Theme.Label(this, "", 9, Theme.Muted, new Rectangle(16, 73, 348, 24));
        heading.Anchor = subtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        passivesButton = Theme.TreeButton(this, new Rectangle(113, 10, 28, 24), (sender, args) => togglePassives());
        modeButton = Theme.Button(this, "", new Rectangle(146, 10, 71, 24), (sender, args) => switchCampaign(), focusable: false);
        modeButton.ForeColor = Theme.Accent;
        var editButton = Theme.Button(this, "Правка", new Rectangle(222, 10, 65, 24), (sender, args) => edit(), english: "Edit");
        lockButton = Theme.Button(this, "Сквозь", new Rectangle(294, 10, 70, 24), (sender, args) => ToggleClickThrough(), english: "Lock");
        lockButton.Enabled = false;
        passivesButton.Anchor = modeButton.Anchor = editButton.Anchor = lockButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        body = CreateCardList();
        Controls.Add(body);
        routeBody = CreateCardList();
        routeBody.AccessibleName = UiText.Get("Маршрут текущего акта", "Route for the current act");
        Controls.Add(routeBody);
        passiveView = new PassiveTreeView(selectBuild) { Location = new Point(16, 44), Visible = false };
        Controls.Add(passiveView);
        footer = Theme.Label(this, "", 8, Theme.Muted, new Rectangle(16, 292, 348, 24));
        footer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        body.SizeChanged += (sender, args) => ResizeCards(body);
        routeBody.SizeChanged += (sender, args) => ResizeCards(routeBody);

        AddResizeHandle(new Rectangle(3, 0, 11, 14), AnchorStyles.Top | AnchorStyles.Left, Cursors.SizeNWSE, -1, -1);
        AddResizeHandle(new Rectangle(14, 0, 352, 6), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Cursors.SizeNS, 0, -1);
        AddResizeHandle(new Rectangle(366, 0, 14, 14), AnchorStyles.Top | AnchorStyles.Right, Cursors.SizeNESW, 1, -1);
        AddResizeHandle(new Rectangle(3, 14, 6, 296), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, Cursors.SizeWE, -1, 0);
        AddResizeHandle(new Rectangle(374, 14, 6, 296), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right, Cursors.SizeWE, 1, 0);
        AddResizeHandle(new Rectangle(3, 310, 11, 14), AnchorStyles.Bottom | AnchorStyles.Left, Cursors.SizeNESW, -1, 1);
        AddResizeHandle(new Rectangle(14, 318, 352, 6), AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Cursors.SizeNS, 0, 1);
        var grip = AddResizeHandle(new Rectangle(366, 310, 14, 14), AnchorStyles.Bottom | AnchorStyles.Right, Cursors.SizeNWSE, 1, 1);
        grip.Paint += (sender, args) => ControlPaint.DrawSizeGrip(args.Graphics, Theme.Background, grip.ClientRectangle);
        // The overlay keeps the game in the foreground, even while the cursor leaves the resize edge.
        resize.Tick += (sender, args) =>
        {
            int button = SystemInformation.MouseButtonsSwapped ? 0x02 : 0x01;
            if (Native.GetAsyncKeyState(button) < 0) ResizeTo(Cursor.Position);
            else FinishResize();
        };

        foreach (Control handle in new Control[] { mark, modeLabel, heading, subtitle })
        {
            handle.Cursor = Cursors.SizeAll;
            handle.MouseDown += StartDrag;
            handle.MouseMove += MoveDrag;
            handle.MouseUp += EndDrag;
        }
        ResumeLayout(false);
        PerformAutoScale();
        MinimumSize = new Size(ClientSize.Width, Pixels(198));
    }

    private static FlowLayoutPanel CreateCardList()
    {
        var list = new FlowLayoutPanel { Bounds = new Rectangle(16, 103, 348, 185), AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Theme.StyleScrollBars(list);
        return list;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        ApplyTransparency();
    }

    public void SetTransparency(int percent)
    {
        transparencyPercent = percent;
        if (IsHandleCreated) ApplyTransparency();
    }

    private void ApplyTransparency()
    {
        byte alpha = (byte)((100 - transparencyPercent) * 255 / 100);
        Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LayeredAlpha);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = (IntPtr)3; // MA_NOACTIVATE, with the mouse click delivered.
            return;
        }
        base.WndProc(ref message);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep the layered style at 0% transparency so click-through still works.
            parameters.ExStyle |= 0x08000000 | Native.Layered;
            if (locked) parameters.ExStyle |= Native.Transparent;
            return parameters;
        }
    }

    public void SetClickThroughShortcut(string shortcut)
    {
        clickThroughShortcut = shortcut;
        lockButton.Enabled = shortcut != null;
        UpdateFooter();
    }

    public void ToggleClickThrough()
    {
        if (clickThroughShortcut == null) return;
        locked = !locked;
        lockButton.BackColor = locked ? Theme.Accent : Theme.Control;
        lockButton.ForeColor = locked ? Theme.Background : Theme.Text;
        if (locked) FinishResize();
        if (locked) passiveView.ClearHover();
        int style = Native.GetWindowLong(Handle, Native.ExtendedStyle);
        Native.SetWindowLong(Handle, Native.ExtendedStyle, locked ? style | Native.Transparent : style & ~Native.Transparent);
        if (locked && mode != HintMode.Passives)
            wheelHook = Native.SetWindowsHookEx(Native.LowLevelMouse, wheelCallback, Native.GetModuleHandle(null), 0);
        else
            RemoveWheelHook();
        UpdateFooter();
    }

    private IntPtr HandleWheel(int code, IntPtr message, IntPtr data)
    {
        if (code == 0 && message == (IntPtr)Native.MouseWheel && locked && IsHandleCreated)
        {
            FlowLayoutPanel list = mode == HintMode.Route ? routeBody : body;
            if (list.Visible && list.IsHandleCreated && list.VerticalScroll.Visible && Native.IsWindowEnabled(Handle))
            {
                var input = (Native.LowLevelMouseInput)Marshal.PtrToStructure(data, typeof(Native.LowLevelMouseInput));
                Rectangle bounds = new Rectangle(list.PointToScreen(Point.Empty), list.Size);
                if (bounds.Contains(input.Position) && !IsCoveredAt(input.Position))
                {
                    int delta = unchecked((int)(input.MouseData & 0xffff0000));
                    int coordinates = (ushort)input.Position.X | ((ushort)input.Position.Y << 16);
                    if (Native.PostMessage(list.Handle, Native.MouseWheel, (IntPtr)delta, (IntPtr)coordinates))
                        return (IntPtr)1;
                }
            }
        }
        return Native.CallNextHookEx(wheelHook, code, message, data);
    }

    private bool IsCoveredAt(Point position)
    {
        IntPtr hovered = Native.GetAncestor(Native.WindowFromPoint(position), Native.RootWindow);
        for (IntPtr above = Native.GetWindow(Handle, Native.PreviousWindow); above != IntPtr.Zero;
            above = Native.GetWindow(above, Native.PreviousWindow))
        {
            if (above == hovered) return true;
        }
        return false;
    }

    private void RemoveWheelHook()
    {
        if (wheelHook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(wheelHook);
        wheelHook = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            passivesTip.Dispose();
            resize.Dispose();
            RemoveWheelHook();
        }
        base.Dispose(disposing);
    }

    private void UpdateFooter()
    {
        if (mode == HintMode.Passives)
            footer.Text = locked
                ? UiText.Get("Для управления деревом нажмите ", "Unlock the tree with ") + clickThroughShortcut
                : UiText.Get("Колесо меняет масштаб · Потяните дерево", "Wheel to zoom · Drag to pan");
        else if (locked && wheelHook != IntPtr.Zero)
            footer.Text = UiText.Get("Сквозь  ·  Колесо над списком  ·  ", "Click-through  ·  Wheel over list  ·  ") + clickThroughShortcut;
        else if (locked)
            footer.Text = UiText.Get("Прокрутка недоступна  ·  ", "Wheel unavailable  ·  ") + clickThroughShortcut;
        else if (clickThroughShortcut != null)
            footer.Text = UiText.Get("Перетащите за заголовок  ·  ", "Drag the header  ·  ") + clickThroughShortcut;
        else footer.Text = UiText.Get("Перетащите за заголовок  ·  Горячая клавиша занята", "Drag the header  ·  Shortcut unavailable");
    }

    public void RefreshLanguage()
    {
        UiText.Refresh(this);
        routeBody.AccessibleName = UiText.Get("Маршрут текущего акта", "Route for the current act");
        displayedRoute = null;
        displayedRouteEmptyText = null;
        passiveView.RefreshLanguage();
        SetMode(mode, showRoute);
    }

    public void ApplySavedBounds(int? left, int? top, int? width, int? height)
    {
        var saved = new Point(left.GetValueOrDefault(), top.GetValueOrDefault());
        Rectangle screen = Screen.FromPoint(saved).WorkingArea;
        bool useSavedPosition = left.HasValue && top.HasValue && screen.Contains(saved);
        if (!useSavedPosition) screen = Screen.PrimaryScreen.WorkingArea;

        Size = new Size(Math.Min(width ?? Width, screen.Width), Math.Min(height ?? Height, screen.Height));
        Location = useSavedPosition ? saved : new Point(screen.Right - Width - Pixels(24), screen.Top + Pixels(32));
        KeepOnScreen(screen);
    }

    public void SetContent(string title, string detail, List<ActTask> tasks, string emptyText, bool refresh)
    {
        heading.Text = title;
        subtitle.Text = detail;
        bool empty = tasks == null || tasks.Count == 0;
        if (!refresh && tasks == displayedTasks && (!empty || emptyText == displayedEmptyText)) return;

        int scroll = tasks == displayedTasks ? -body.AutoScrollPosition.Y : 0;
        displayedTasks = tasks;
        displayedEmptyText = emptyText;
        checkboxes.Clear();
        RebuildCards(body, scroll, empty ? emptyText : null, width =>
        {
            foreach (ActTask task in tasks)
                checkboxes.Add(task, AddCard(body, width, task.Condition, task.Action, task.Done, done => taskDone(task, done)));
        });
        UpdateProgress();
    }

    public void SetPassiveBuilds(List<SavedPassiveBuild> builds, int activeIndex, bool reload)
    {
        passiveView.SetBuilds(builds, activeIndex, reload);
    }

    public void SetMode(HintMode value, bool campaignRoute)
    {
        mode = value;
        showRoute = campaignRoute;
        bool passives = mode == HintMode.Passives;
        if (passives) RemoveWheelHook();
        else if (locked && wheelHook == IntPtr.Zero)
            wheelHook = Native.SetWindowsHookEx(Native.LowLevelMouse, wheelCallback, Native.GetModuleHandle(null), 0);
        modeLabel.Text = passives ? UiText.Get("ПАССИВКИ", "PASSIVES")
            : mode == HintMode.Route ? UiText.Get("МАРШРУТ", "ROUTE") : UiText.Get("ЗАДАЧИ", "TASKS");
        modeButton.Text = showRoute ? UiText.Get("Задачи", "Tasks") : UiText.Get("Маршрут", "Route");
        passivesButton.AccessibleName = UiText.Get("Пассивки", "Passives");
        passivesTip.SetToolTip(passivesButton, !passives ? passivesButton.AccessibleName : showRoute
            ? UiText.Get("Вернуться к маршруту", "Return to route")
            : UiText.Get("Вернуться к задачам", "Return to tasks"));
        passivesButton.BackColor = passives ? Theme.Accent : Theme.Control;
        passivesButton.ForeColor = passives ? Theme.Background : Theme.Accent;
        heading.Visible = subtitle.Visible = progress.Visible = !passives;
        body.Visible = mode == HintMode.Tasks;
        routeBody.Visible = mode == HintMode.Route;
        passiveView.Visible = passives;
        if (!passives) passiveView.ClearHover();
        UpdateFooter();
        UpdateProgress();
    }

    public void SetRouteNotes(List<RouteNote> notes, string emptyText, bool refresh)
    {
        bool empty = notes == null || notes.Count == 0;
        bool sameRoute = notes == displayedRoute;
        if (!refresh && sameRoute && (!empty || emptyText == displayedRouteEmptyText)) return;

        int scroll = sameRoute ? -routeBody.AutoScrollPosition.Y : 0;
        displayedRoute = notes;
        displayedRouteEmptyText = emptyText;
        routeCheckboxes.Clear();
        RebuildCards(routeBody, scroll, empty ? emptyText : null, width =>
        {
            foreach (RouteNote note in notes)
            {
                string title = String.IsNullOrEmpty(note.AreaId) ? null : CampaignAreas.NameOf(note.AreaId);
                routeCheckboxes.Add(note, AddCard(routeBody, width, title, note.Text, note.Done, done => routeNoteDone(note, done)));
            }
        });
        UpdateProgress();
    }

    private void RebuildCards(FlowLayoutPanel list, int scroll, string emptyText, Action<int> addCards)
    {
        list.SuspendLayout();
        while (list.Controls.Count > 0) list.Controls[0].Dispose();
        int width = list.Width - SystemInformation.VerticalScrollBarWidth;
        if (emptyText != null)
        {
            var message = Theme.Label(list, emptyText, 10, Theme.Muted, new Rectangle(0, 0, width, Pixels(72)));
            message.Margin = Padding.Empty;
            message.Padding = new Padding(Pixels(11));
            message.BackColor = Theme.Surface;
        }
        else
        {
            addCards(width);
        }
        list.ResumeLayout();
        list.AutoScrollPosition = new Point(0, scroll);
    }

    private static void ResizeCards(FlowLayoutPanel list)
    {
        int width = list.Width - SystemInformation.VerticalScrollBarWidth;
        if (list.Controls.Count == 0 || list.Controls[0].Width == width) return;
        int scroll = -list.AutoScrollPosition.Y;
        list.SuspendLayout();
        foreach (Control card in list.Controls) card.Width = width;
        list.ResumeLayout();
        list.AutoScrollPosition = new Point(0, scroll);
    }

    private CheckBox AddCard(FlowLayoutPanel list, int width, string title, string text, bool done, Action<bool> setDone)
    {
        var card = Theme.Card();
        card.Width = width;
        card.Margin = new Padding(0, 0, 0, Pixels(7));
        bool hasTitle = !String.IsNullOrWhiteSpace(title);
        bool hasText = !String.IsNullOrWhiteSpace(text);
        Label header = hasTitle && hasText ? AddCardText(card, title, true) : null;
        string caption = hasText ? text : title ?? "";
        var content = AddCardText(card, caption, hasTitle && !hasText);
        content.ResetForeColor();
        var check = new OverlayCheckBox { Checked = done, AccessibleName = caption, TabStop = false, Cursor = Cursors.Hand };
        check.CheckedChanged += (sender, args) => setDone(check.Checked);
        content.Cursor = Cursors.Hand;
        content.Click += (sender, args) => check.Checked = !check.Checked;
        card.Controls.Add(check);
        ArrangeCard(card, header, content, check);
        card.SizeChanged += (sender, args) => ArrangeCard(card, header, content, check);
        card.ForeColor = done ? Theme.Muted : Theme.Text;
        list.Controls.Add(card);
        return check;
    }

    private static Label AddCardText(Panel card, string text, bool bold)
    {
        var label = Theme.Label(card, text, 10, Theme.Text, Rectangle.Empty, bold);
        label.AutoEllipsis = false;
        label.TextAlign = ContentAlignment.TopLeft;
        return label;
    }

    private void ArrangeCard(Panel card, Label header, Label content, CheckBox check)
    {
        int left = Pixels(31);
        int width = card.Width - left - Pixels(9);
        if (content.Width == width) return;
        int top = Pixels(8);
        if (header != null)
        {
            PositionCardText(header, left, top, width);
            top = header.Bottom + Pixels(3);
        }
        PositionCardText(content, left, top, width);
        int checkTop = header == null ? top : header.Top + (content.Bottom - header.Top - Pixels(20)) / 2;
        check.SetBounds(Pixels(7), checkTop, Pixels(20), Pixels(20));
        card.Height = Math.Max(content.Bottom, check.Bottom) + Pixels(8);
    }

    private static void PositionCardText(Label label, int left, int top, int width)
    {
        int height = TextRenderer.MeasureText(label.Text, label.Font, new Size(width, Int32.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
        label.SetBounds(left, top, width, height);
    }

    private int Pixels(int size)
    {
        return (int)Math.Round(size * CurrentAutoScaleDimensions.Width / 96f);
    }

    public void SyncCompletion(ActTask task)
    {
        SyncCompletion(checkboxes, task, task.Done);
    }

    public void SyncCompletion(RouteNote note)
    {
        SyncCompletion(routeCheckboxes, note, note.Done);
    }

    private void SyncCompletion<T>(Dictionary<T, CheckBox> controls, T item, bool done)
    {
        CheckBox check;
        if (!controls.TryGetValue(item, out check)) return;
        check.Checked = done;
        check.Parent.ForeColor = done ? Theme.Muted : Theme.Text;
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        progress.Text = mode == HintMode.Route ? CompletionText(displayedRoute, note => note.Done) : CompletionText(displayedTasks, task => task.Done);
    }

    private static string CompletionText<T>(List<T> items, Func<T, bool> isDone)
    {
        if (items == null || items.Count == 0) return "";
        return UiText.Get("Выполнено ", "Completed ") + items.Count(isDone) + " / " + items.Count;
    }

    private void StartDrag(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        dragging = true;
        dragCursor = Cursor.Position;
        dragLocation = Location;
        ((Control)sender).Capture = true;
    }

    private void MoveDrag(object sender, MouseEventArgs args)
    {
        if (!dragging || (args.Button & MouseButtons.Left) == 0) return;
        Point cursor = Cursor.Position;
        Location = new Point(dragLocation.X + cursor.X - dragCursor.X, dragLocation.Y + cursor.Y - dragCursor.Y);
    }

    private void EndDrag(object sender, MouseEventArgs args)
    {
        if (!dragging || args.Button != MouseButtons.Left) return;
        dragging = false;
        ((Control)sender).Capture = false;
        Size previousSize = Size;
        KeepOnScreen(Screen.FromRectangle(Bounds).WorkingArea);
        if (Location != dragLocation || Size != previousSize) boundsChanged(Bounds);
    }

    private void KeepOnScreen(Rectangle screen)
    {
        Size = new Size(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
        Location = new Point(Math.Max(screen.Left, Math.Min(Left, screen.Right - Width)),
            Math.Max(screen.Top, Math.Min(Top, screen.Bottom - Height)));
    }

    private Panel AddResizeHandle(Rectangle bounds, AnchorStyles anchor, Cursor cursor, int horizontal, int vertical)
    {
        var handle = Theme.Panel(this, Theme.Background, bounds);
        handle.Anchor = anchor;
        handle.Cursor = cursor;
        handle.MouseDown += (sender, args) =>
        {
            if (locked || args.Button != MouseButtons.Left) return;
            resizeCursor = handle.PointToScreen(args.Location);
            resizeOrigin = Bounds;
            resizeScreen = Screen.FromRectangle(resizeOrigin).WorkingArea;
            resizeHorizontal = horizontal;
            resizeVertical = vertical;
            resize.Start();
        };
        handle.MouseUp += (sender, args) =>
        {
            if (!resize.Enabled || args.Button != MouseButtons.Left) return;
            ResizeTo(Cursor.Position);
            FinishResize();
        };
        return handle;
    }

    private void ResizeTo(Point point)
    {
        int dx = point.X - resizeCursor.X;
        int dy = point.Y - resizeCursor.Y;
        int left = resizeOrigin.Left, right = resizeOrigin.Right, top = resizeOrigin.Top, bottom = resizeOrigin.Bottom;
        if (resizeHorizontal < 0) left = Math.Min(right - MinimumSize.Width, Math.Max(resizeScreen.Left, left + dx));
        if (resizeHorizontal > 0) right = Math.Max(left + MinimumSize.Width, Math.Min(resizeScreen.Right, right + dx));
        if (resizeVertical < 0) top = Math.Min(bottom - MinimumSize.Height, Math.Max(resizeScreen.Top, top + dy));
        if (resizeVertical > 0) bottom = Math.Max(top + MinimumSize.Height, Math.Min(resizeScreen.Bottom, bottom + dy));
        Bounds = Rectangle.FromLTRB(left, top, right, bottom);
    }

    private void FinishResize()
    {
        if (!resize.Enabled) return;
        resize.Stop();
        if (Bounds != resizeOrigin) boundsChanged(Bounds);
    }

    private sealed class OverlayCheckBox : CheckBox
    {
        public OverlayCheckBox()
        {
            SetStyle(ControlStyles.Selectable, false);
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            using (var brush = new SolidBrush(BackColor))
                args.Graphics.FillRectangle(brush, args.ClipRectangle);
            Theme.DrawCheck(args.Graphics, ClientRectangle, Checked, Enabled);
        }
    }

}

internal static class Native
{
    public const int LockShortcut = 1;
    public const int AlternateLockShortcut = 2;
    public const int HotkeyMessage = 0x0312;
    public const uint HotkeyAlt = 0x0001;
    public const uint HotkeyControl = 0x0002;
    public const uint HotkeyShift = 0x0004;
    public const uint NoHotkeyRepeat = 0x4000;
    public const int ExtendedStyle = -20;
    public const int Transparent = 0x20;
    public const int Layered = 0x80000;
    public const uint LayeredAlpha = 0x02;
    public const int LowLevelMouse = 14;
    public const uint MouseWheel = 0x020A;
    public const uint RootWindow = 2;
    public const uint PreviousWindow = 3;

    public delegate IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct LowLevelMouseInput
    {
        public Point Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SetWindowsHookEx(int type, MouseHookCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint type);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint direction);
}
