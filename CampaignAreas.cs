using System;
using System.Collections.Generic;
using System.IO;

internal sealed class CampaignArea
{
    public readonly string Id;
    private readonly string russianName;
    private readonly string englishName;
    public string Name { get { return UiText.Get(russianName, englishName); } }

    public CampaignArea(string id, string russianName, string englishName)
    {
        Id = id;
        this.russianName = russianName;
        this.englishName = englishName;
    }

    public override string ToString() { return Name; }
}

internal static class CampaignAreas
{
    private static readonly List<CampaignArea>[] ByAct = new List<CampaignArea>[10];
    private static readonly Dictionary<string, CampaignArea> ById = new Dictionary<string, CampaignArea>(StringComparer.Ordinal);

    static CampaignAreas()
    {
        for (int i = 0; i < ByAct.Length; i++) ByAct[i] = new List<CampaignArea>();
        using (var stream = typeof(CampaignAreas).Assembly.GetManifestResourceStream("CampaignAreas.tsv"))
        using (var reader = new StreamReader(stream))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] columns = line.Split('\t');
                var area = new CampaignArea(columns[1], columns[2], columns[3]);
                ByAct[Int32.Parse(columns[0]) - 1].Add(area);
                ById.Add(area.Id, area);
            }
        }
    }

    public static IEnumerable<CampaignArea> ForAct(int act) { return ByAct[act - 1]; }

    public static string NameOf(string id, string fallback = null)
    {
        if (String.IsNullOrEmpty(id)) return fallback ?? UiText.Get("Без локации", "No location");
        CampaignArea area;
        return ById.TryGetValue(id, out area) ? area.Name : fallback ?? id;
    }
}
