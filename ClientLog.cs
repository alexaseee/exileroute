using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal sealed class ClientLog
{
    private const string Timestamp = @"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}\s+";
    private const string Header = Timestamp + @"(?:[a-fA-F0-9]+\s+){0,2}";
    private static readonly Regex Generation = new Regex(
        Header + @"\[DEBUG Client \d+\] Generating level \d+ area ""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Campaign = new Regex(@"^(?:1_([1-5])|2_([6-9]|10))_", RegexOptions.Compiled);
    private static readonly Regex Scene = new Regex(
        Header + @"\[INFO Client \d+\] \[SCENE\] Set Source \[(.+)\]$", RegexOptions.Compiled);
    private static readonly Regex Entry = new Regex(
        Header + @"\[INFO Client \d+\] : (?:<<[^>]+>>)*(?:You have entered|Вы вошли в область) (.+)\.$",
        RegexOptions.Compiled);
    private static readonly Regex NameForm = new Regex(@"<if:[^>]+>\{[^}]*\}<else>\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex Session = new Regex(
        @"(?:" + Timestamp + @"\*{5} LOG FILE (?:OPENING|CLOSING) \*{5}|" + Header +
        @"\[INFO Client \d+\] Async connecting to \S*login\.)",
        RegexOptions.Compiled);

    private const int TailSize = 512 * 1024;
    private readonly byte[] buffer = new byte[TailSize];
    private readonly byte[] anchor = new byte[96];
    private int anchorLength;
    private long offset;
    private bool positioned;

    public readonly string Path;
    public int? Act { get; private set; }
    public string AreaId { get; private set; }
    public string Zone { get; private set; }
    public bool Available { get; private set; }

    public ClientLog(string path)
    {
        Path = path;
        ClearArea();
    }

    public void Poll()
    {
        if (String.IsNullOrEmpty(Path)) return;

        try
        {
            using (var file = new FileStream(Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                long length = file.Length;
                if (!positioned || length < offset || length - offset > TailSize || !AnchorMatches(file))
                {
                    ClearArea();
                    offset = Math.Max(0, length - TailSize);
                    anchorLength = 0;
                    positioned = true;
                }

                if (length > offset)
                {
                    file.Position = offset;
                    int count = file.Read(buffer, 0, (int)(length - offset));
                    int newline = count == 0 ? -1 : Array.LastIndexOf(buffer, (byte)'\n', count - 1, count);
                    if (newline >= 0)
                    {
                        int consumed = newline + 1;
                        ReadLines(Encoding.UTF8.GetString(buffer, 0, consumed));
                        Advance(consumed);
                    }
                    else if (count == buffer.Length)
                    {
                        Advance(count);
                    }
                }
            }
            Available = true;
        }
        catch (IOException)
        {
            MarkUnavailable();
        }
        catch (UnauthorizedAccessException)
        {
            MarkUnavailable();
        }
    }

    private bool AnchorMatches(FileStream file)
    {
        file.Position = offset - anchorLength;
        int count = file.Read(buffer, 0, anchorLength);
        if (count != anchorLength) return false;
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] != anchor[i]) return false;
        }
        return true;
    }

    private void Advance(int count)
    {
        offset += count;
        anchorLength = Math.Min(anchor.Length, count);
        Array.Copy(buffer, count - anchorLength, anchor, 0, anchorLength);
    }

    private void MarkUnavailable()
    {
        Available = false;
        positioned = false;
        ClearArea();
    }

    private void ClearArea()
    {
        Act = null;
        AreaId = null;
        Zone = "";
    }

    private void ReadLines(string text)
    {
        using (var lines = new StringReader(text))
        {
            string line;
            while ((line = lines.ReadLine()) != null)
            {
                if (Session.IsMatch(line))
                {
                    ClearArea();
                    continue;
                }

                Match generation = Generation.Match(line);
                if (generation.Success)
                {
                    AreaId = generation.Groups[1].Value;
                    Match campaign = Campaign.Match(AreaId);
                    Act = campaign.Success ? (int?)Int32.Parse(campaign.Groups[1].Value + campaign.Groups[2].Value) : null;
                    Zone = "";
                    continue;
                }

                Match entry = Scene.Match(line);
                if (!entry.Success) entry = Entry.Match(line);
                if (entry.Success)
                {
                    string zone = NameForm.Replace(entry.Groups[1].Value, "$1").Trim();
                    if (zone.Length == 0 || zone == "(null)" || zone == "(unknown)") continue;
                    Zone = zone;
                }
            }
        }
    }

    public static string Discover()
    {
        var libraries = new List<string>();
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!String.IsNullOrWhiteSpace(steam)) libraries.Add(steam);

        var programFiles = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        foreach (string directory in programFiles)
        {
            if (directory.Length > 0) libraries.Add(System.IO.Path.Combine(directory, "Steam"));
        }

        foreach (string root in libraries.ToArray())
        {
            string config = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(config)) continue;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(config), @"""path""\s+""([^""]+)"""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        string latest = null;
        foreach (string root in libraries)
        {
            string path = System.IO.Path.Combine(root, "steamapps", "common", "Path of Exile", "logs", "Client.txt");
            latest = NewerLog(latest, path);
        }
        foreach (string directory in programFiles)
        {
            if (directory.Length == 0) continue;
            string path = System.IO.Path.Combine(directory, "Grinding Gear Games", "Path of Exile", "logs", "Client.txt");
            latest = NewerLog(latest, path);
        }
        return latest;
    }

    private static string NewerLog(string previous, string candidate)
    {
        if (!File.Exists(candidate)) return previous;
        if (previous == null || File.GetLastWriteTimeUtc(candidate) > File.GetLastWriteTimeUtc(previous))
            return System.IO.Path.GetFullPath(candidate);
        return previous;
    }
}
