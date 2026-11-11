using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

internal sealed class PassivePreset
{
    public string Title;
    public string Version;
    public int ClassId;
    public int AscendancyId;
    public string SecondaryAscendancy;
    public readonly HashSet<int> Allocated = new HashSet<int>();
    public readonly Dictionary<int, int> Masteries = new Dictionary<int, int>();
    public readonly Dictionary<int, PassiveDescription> Overrides = new Dictionary<int, PassiveDescription>();

    public override string ToString() { return Title; }
}

internal sealed class PassiveDescription
{
    public string Name;
    public string[] Stats;
}

internal static class PassiveBuild
{
    private const int BuildLimit = 2 * 1024 * 1024;
    private const int TreeLimit = 20 * 1024 * 1024;
    private const string UserAgent = "ExileRoute (contact: https://github.com/alexaseee/exileroute)";
    private static readonly string CacheDirectory = Path.Combine(Path.GetDirectoryName(OverlaySettings.FilePath), "PassiveCache");
    private static readonly Regex PastePath = new Regex(@"^/(?:u/[A-Za-z0-9_-]+/)?[A-Za-z0-9_-]+/?$");
    private static readonly Regex TreeVersion = new Regex(@"^(?<league>3_\d{1,3})(?:_(?<variant>ruthless_alternate|ruthless|alternate))?$");
    private static readonly Regex MasteryPair = new Regex(@"\{(\d+),(\d+)\}");

    static PassiveBuild()
    {
        // The standalone compiler defaults to the legacy .NET Framework TLS settings.
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    }

    public static bool TryNormalizeLink(string text, out string link)
    {
        link = null;
        Uri uri;
        if (String.IsNullOrEmpty(text) || text.Length > 256 || !Uri.TryCreate(text, UriKind.Absolute, out uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("pobb.in", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.UserInfo.Length > 0 || !PastePath.IsMatch(uri.AbsolutePath))
            return false;
        link = "https://pobb.in" + uri.AbsolutePath.TrimEnd('/');
        return true;
    }

    public static PassivePreset[] Load(string link, CancellationToken token)
    {
        string normalized;
        if (!TryNormalizeLink(link, out normalized)) throw new InvalidDataException();
        return ReadSource(normalized + "/raw", BuildLimit, DecodeBuild, token);
    }

    public static PassiveTreeData LoadTree(string version, CancellationToken token)
    {
        Match match = TreeVersion.Match(version);
        if (!match.Success) throw new NotSupportedException(version);
        string league = match.Groups["league"].Value;
        string variant = match.Groups["variant"].Value;
        string fileName = variant.Length == 0 ? "data" : variant.Replace('_', '-');
        // PoB's 3_29 tree includes the 3.29.1 revision.
        string release = league.Replace('_', '.') + (league == "3_29" ? ".1" : ".0");
        string url = "https://raw.githubusercontent.com/grindinggear/skilltree-export/" + release + "/" + fileName + ".json";
        return ReadSource(url, TreeLimit, PassiveTreeData.Decode, token);
    }

    private static T ReadSource<T>(string url, int limit, Func<byte[], T> parse, CancellationToken token)
    {
        string fileName;
        using (var hash = SHA256.Create())
            fileName = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "") + ".cache";
        string path = Path.Combine(CacheDirectory, fileName);
        token.ThrowIfCancellationRequested();
        try
        {
            using (var cached = File.OpenRead(path))
                return parse(ReadLimited(cached, limit, token));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (XmlException) { }
        catch (FormatException) { }
        catch (InvalidDataException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }

        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = UserAgent;
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;
        request.AllowAutoRedirect = false;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        byte[] bytes;
        using (token.Register(request.Abort))
        using (var response = request.GetResponse())
        using (var stream = response.GetResponseStream())
            bytes = ReadLimited(stream, limit, token);

        T result = parse(bytes);
        token.ThrowIfCancellationRequested();
        StoreCache(path, bytes);
        return result;
    }

    private static void StoreCache(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static byte[] ReadLimited(Stream stream, int limit, CancellationToken token)
    {
        using (var buffer = new MemoryStream())
        {
            byte[] block = new byte[16384];
            int count;
            while ((count = stream.Read(block, 0, block.Length)) != 0)
            {
                token.ThrowIfCancellationRequested();
                if (buffer.Length + count > limit) throw new InvalidDataException();
                buffer.Write(block, 0, count);
            }
            return buffer.ToArray();
        }
    }

    private static byte[] DecodeBase64(string text)
    {
        string code = text.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(code.PadRight((code.Length + 3) / 4 * 4, '='));
    }

    private static PassivePreset[] DecodeBuild(byte[] bytes)
    {
        byte[] code = DecodeBase64(Encoding.UTF8.GetString(bytes));
        // PoB wraps raw DEFLATE in a zlib header and an Adler-32 checksum.
        if (code.Length < 7 || (code[0] & 15) != 8 || ((code[0] << 8) + code[1]) % 31 != 0 || (code[1] & 32) != 0)
            throw new InvalidDataException();
        XElement root;
        using (var compressed = new MemoryStream(code, 2, code.Length - 6))
        using (var deflate = new DeflateStream(compressed, CompressionMode.Decompress))
        using (var xml = new MemoryStream(ReadLimited(deflate, 16 * 1024 * 1024, CancellationToken.None)))
        using (var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            root = XElement.Load(reader);
        if (root.Name == "PathOfBuilding2") throw new NotSupportedException("PoE 2");
        if (root.Name != "PathOfBuilding") throw new InvalidDataException();
        var specs = root.Elements("Tree").Elements("Spec").ToList();
        if (specs.Count == 0) specs = root.Elements("Spec").ToList();
        if (specs.Count == 0) throw new InvalidDataException();
        return specs.Select(DecodePreset).ToArray();
    }

    private static PassivePreset DecodePreset(XElement spec, int index)
    {
        var preset = new PassivePreset
        {
            Title = (string)spec.Attribute("title"),
            Version = (string)spec.Attribute("treeVersion") ?? "3_6",
            ClassId = Number((string)spec.Attribute("classId")),
            AscendancyId = Number((string)spec.Attribute("ascendClassId")),
            SecondaryAscendancy = (string)spec.Attribute("secondaryAscendClassId")
        };
        if (String.IsNullOrWhiteSpace(preset.Title)) preset.Title = (index + 1).ToString(CultureInfo.InvariantCulture);
        string nodes = (string)spec.Attribute("nodes");
        if (nodes == null)
        {
            DecodeLegacyTree((string)spec.Element("URL"), preset);
        }
        else
        {
            foreach (string node in nodes.Split(','))
            {
                int id = Number(node);
                if (id > 0) preset.Allocated.Add(id);
            }
        }
        foreach (Match pair in MasteryPair.Matches((string)spec.Attribute("masteryEffects") ?? ""))
            preset.Masteries[Number(pair.Groups[1].Value)] = Number(pair.Groups[2].Value);
        foreach (XElement node in spec.Elements("Overrides").Elements("Override"))
        {
            int id = Number((string)node.Attribute("nodeId"));
            string name = (string)node.Attribute("dn");
            if (id > 0 && !String.IsNullOrWhiteSpace(name))
                preset.Overrides[id] = new PassiveDescription { Name = name, Stats = Lines(node.Value) };
        }
        preset.Allocated.UnionWith(preset.Masteries.Keys);
        return preset;
    }

    private static void DecodeLegacyTree(string url, PassivePreset preset)
    {
        if (String.IsNullOrWhiteSpace(url)) return;
        url = url.Trim().TrimEnd('/');
        byte[] code = DecodeBase64(url.Substring(url.LastIndexOf('/') + 1));
        if (code.Length < 7 || code[0] != 0 || code[1] != 0 || code[2] != 0 || code[3] > 6)
            throw new InvalidDataException();
        int version = code[3];
        preset.ClassId = code[4];
        preset.AscendancyId = version >= 4 ? code[5] & 3 : 0;
        preset.SecondaryAscendancy = ((code[5] >> 2) & 3).ToString(CultureInfo.InvariantCulture);
        int start = version >= 4 ? 7 : 6;
        int end = version >= 5 ? start + code[6] * 2 : code.Length;
        if (end > code.Length || (end - start) % 2 != 0) throw new InvalidDataException();
        for (int i = start; i < end; i += 2) preset.Allocated.Add(NodeId(code, i));
        if (version < 5 || end >= code.Length) return;
        start = end + 1;
        end = start + code[end] * 2;
        if (end > code.Length) throw new InvalidDataException();
        for (int i = start; i < end; i += 2) preset.Allocated.Add(NodeId(code, i) + 65536);
        if (version < 6 || end >= code.Length) return;
        start = end + 1;
        end = start + code[end] * 4;
        if (end > code.Length) throw new InvalidDataException();
        for (int i = start; i < end; i += 4) preset.Masteries[NodeId(code, i + 2)] = NodeId(code, i);
    }

    private static int NodeId(byte[] code, int offset) { return (code[offset] << 8) + code[offset + 1]; }

    private static int Number(string value)
    {
        int number;
        return Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static string[] Lines(string value)
    {
        return value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
    }
}

internal enum PassiveNodeKind { Small, Notable, Keystone, Mastery, Jewel, Start }

internal sealed class PassiveNode
{
    public int Id;
    public int? ClassId;
    public bool AscendancyStart;
    public string Ascendancy;
    public int Group;
    public int Orbit;
    public PointF Center;
    public PointF Position;
    public float Angle;
    public float Radius;
    public PassiveNodeKind Kind;
    public PassiveDescription Description;
    public Dictionary<int, string[]> Masteries;
    public int[] Links;
}

internal sealed class PassiveEdge
{
    public PassiveNode From;
    public PassiveNode To;
}

internal sealed class PassiveTreeData
{
    private static readonly int[] Angles16 = { 0, 30, 45, 60, 90, 120, 135, 150, 180, 210, 225, 240, 270, 300, 315, 330 };
    private static readonly int[] Angles40 = { 0, 10, 20, 30, 40, 45, 50, 60, 70, 80, 90, 100, 110, 120, 130, 135, 140, 150, 160, 170,
        180, 190, 200, 210, 220, 225, 230, 240, 250, 260, 270, 280, 290, 300, 310, 315, 320, 330, 340, 350 };

    public readonly Dictionary<int, PassiveNode> Nodes = new Dictionary<int, PassiveNode>();
    public readonly List<PassiveEdge> Edges = new List<PassiveEdge>();
    public readonly Dictionary<int, string[]> Ascendancies = new Dictionary<int, string[]>();
    public string[] SecondaryAscendancies;

    public static PassiveTreeData Decode(byte[] bytes)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = bytes.Length, RecursionLimit = 32 };
        var json = serializer.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
        var groups = Value(json, "groups") as Dictionary<string, object>;
        var nodes = Value(json, "nodes") as Dictionary<string, object>;
        var constants = Object(json, "constants");
        float[] radii = ArrayOf(constants, "orbitRadii").Select(item => Convert.ToSingle(item, CultureInfo.InvariantCulture)).ToArray();
        int[] counts = ArrayOf(constants, "skillsPerOrbit").Select(Integer).ToArray();
        if (groups == null || nodes == null || radii.Length == 0 || radii.Length != counts.Length)
            throw new InvalidDataException();
        var tree = new PassiveTreeData();
        int classId = 0;
        foreach (Dictionary<string, object> character in ArrayOf(json, "classes"))
            tree.Ascendancies[classId++] = AscendancyIds(character, "ascendancies");
        tree.SecondaryAscendancies = AscendancyIds(json, "alternate_ascendancies");
        foreach (var entry in nodes)
        {
            int id;
            if (!Int32.TryParse(entry.Key, out id)) continue;
            var data = (Dictionary<string, object>)entry.Value;
            int groupId = Integer(Value(data, "group"));
            var group = Object(groups, groupId.ToString(CultureInfo.InvariantCulture));
            var expansion = Object(data, "expansionJewel");
            if (group == null || Flag(data, "isProxy") || Flag(group, "isProxy") || Value(expansion, "parent") != null) continue;
            int orbit = Integer(Value(data, "orbit"));
            int index = Integer(Value(data, "orbitIndex"));
            if (orbit < 0 || orbit >= counts.Length || index < 0 || index >= counts[orbit]) continue;
            var center = new PointF(Convert.ToSingle(Value(group, "x"), CultureInfo.InvariantCulture),
                Convert.ToSingle(Value(group, "y"), CultureInfo.InvariantCulture));
            float angle = Angle(counts[orbit], index);
            float radius = radii[orbit];
            double radians = angle * Math.PI / 180;
            var node = new PassiveNode
            {
                Id = id,
                Group = groupId,
                Orbit = orbit,
                Center = center,
                Position = new PointF(center.X + radius * (float)Math.Sin(radians), center.Y - radius * (float)Math.Cos(radians)),
                Angle = angle,
                Radius = radius,
                Ascendancy = Value(data, "ascendancyName") as string,
                AscendancyStart = Flag(data, "isAscendancyStart"),
                ClassId = Value(data, "classStartIndex") == null ? (int?)null : Integer(Value(data, "classStartIndex")),
                Description = new PassiveDescription { Name = Value(data, "name") as string, Stats = Stats(data) },
                Links = ArrayOf(data, "out").Concat(ArrayOf(data, "in")).Select(Integer).Distinct().ToArray()
            };
            node.Kind = Kind(node, data);
            if (node.Kind == PassiveNodeKind.Mastery)
            {
                node.Masteries = new Dictionary<int, string[]>();
                foreach (Dictionary<string, object> effect in ArrayOf(data, "masteryEffects"))
                    node.Masteries[Integer(Value(effect, "effect"))] = Stats(effect);
            }
            tree.Nodes.Add(id, node);
        }
        if (tree.Nodes.Count == 0) throw new InvalidDataException();
        foreach (PassiveNode node in tree.Nodes.Values)
        {
            foreach (int id in node.Links)
            {
                PassiveNode other;
                if (id > node.Id && tree.Nodes.TryGetValue(id, out other) && node.Ascendancy == other.Ascendancy &&
                    node.Kind != PassiveNodeKind.Mastery && other.Kind != PassiveNodeKind.Mastery)
                    tree.Edges.Add(new PassiveEdge { From = node, To = other });
            }
        }
        return tree;
    }

    private static PassiveNodeKind Kind(PassiveNode node, Dictionary<string, object> data)
    {
        if (node.ClassId.HasValue || node.AscendancyStart) return PassiveNodeKind.Start;
        if (Flag(data, "isMastery")) return PassiveNodeKind.Mastery;
        if (Flag(data, "isJewelSocket")) return PassiveNodeKind.Jewel;
        if (Flag(data, "isKeystone")) return PassiveNodeKind.Keystone;
        return Flag(data, "isNotable") ? PassiveNodeKind.Notable : PassiveNodeKind.Small;
    }

    private static float Angle(int count, int index)
    {
        if (count == 16) return Angles16[index];
        if (count == 40) return Angles40[index];
        return index * 360f / count;
    }

    private static string[] AscendancyIds(Dictionary<string, object> data, string key)
    {
        return ArrayOf(data, key).Cast<Dictionary<string, object>>().Select(entry => Value(entry, "id") as string).ToArray();
    }

    private static string[] Stats(Dictionary<string, object> data)
    {
        return ArrayOf(data, "stats").OfType<string>().Where(stat => !String.IsNullOrWhiteSpace(stat)).ToArray();
    }

    private static int Integer(object value) { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
    private static bool Flag(Dictionary<string, object> data, string key) { return object.Equals(Value(data, key), true); }
    private static Dictionary<string, object> Object(Dictionary<string, object> data, string key) { return Value(data, key) as Dictionary<string, object>; }
    private static IEnumerable<object> ArrayOf(Dictionary<string, object> data, string key)
    {
        var array = Value(data, key) as IList;
        return array == null ? Enumerable.Empty<object>() : array.Cast<object>();
    }
    private static object Value(Dictionary<string, object> data, string key)
    {
        object value;
        return data != null && data.TryGetValue(key, out value) ? value : null;
    }
}
