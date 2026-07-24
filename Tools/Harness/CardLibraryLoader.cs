using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

// Headless replica of GameManager.ParseOfficialCardLibrary + NormalizeEffectText +
// NormalizeFeatures, so the real card stats/effect-text from official-card-library.json
// are fed into CardData.Library exactly as the running game does. Uses System.Text.Json
// (Unity's JsonUtility isn't available here). Must stay in sync with GameManager's parsing.
static class CardLibraryLoader
{
    static readonly string[] ClauseTags =
    {
        "[On Play]", "[Activate: Main]", "[When Attacking]", "[On Block]", "[On K.O.]",
        "[Main]", "[Counter]", "[Trigger]", "[End of Your Turn]", "[Start of Your Turn]",
        "[On Your Opponent's Attack]", "[End of Your Opponent's Turn]", "[End of Opponent's Turn]",
    };

    public static int Load(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(json);
        var seen = new HashSet<string>();
        int n = 0;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string id = Str(el, "id");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            int life = Int(el, "life");
            CardData.UpsertCard(
                id,
                Str(el, "name"),
                Str(el, "type"),
                Str(el, "color"),
                Int(el, "cost"),
                Int(el, "power"),
                life > 0 ? life : (int?)null,
                Int(el, "counter"),
                StrArray(el, "keywords"),
                NormalizeEffectText(Str(el, "effect")),
                NormalizeEffectText(Str(el, "trigger")),
                NormalizeFeatures(el),
                Str(el, "rarity"),
                Str(el, "attribute"),
                Str(el, "block"));
            n++;
        }
        return n;
    }

    static string NormalizeEffectText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var tag in ClauseTags)
            text = Regex.Replace(text, "(?<=[.)”\"])" + @"\s*" + Regex.Escape(tag), "\n" + tag);
        return Regex.Replace(text, "\n+", "\n").Trim();
    }

    static string[] NormalizeFeatures(JsonElement el)
    {
        var values = new List<string>();
        if (el.TryGetProperty("features", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var f in arr.EnumerateArray())
                if (f.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(f.GetString())) values.Add(f.GetString().Trim());
        string feature = Str(el, "feature");
        if (!string.IsNullOrWhiteSpace(feature))
            foreach (var f in feature.Split('/'))
                if (!string.IsNullOrWhiteSpace(f)) values.Add(f.Trim());
        return values.Distinct().ToArray();
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
    static int Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
    static string[] StrArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToArray();
    }
}
