using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpireStrategist.Core;

/// <summary>Presentation-only translation. Policy values, IDs and persisted raw records stay unchanged.</summary>
public static class Localization
{
    private sealed record Template(string Source, string Target, Regex Pattern, string[] Anchors);
    public static IReadOnlyDictionary<string,string> Catalog { get; } = Load();
    private static readonly Regex Slots = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);
    private static readonly Regex Han = new(@"[\u3400-\u9fff]", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string,string> DisplayCatalog = MakeDisplayCatalog();
    private static readonly Template[] Templates = MakeTemplates();
    private static readonly Regex Literals = new(string.Join("|", DisplayCatalog.Keys.Where(k=>!Slots.IsMatch(k))
        .OrderByDescending(k=>k.Length).Select(Regex.Escape)), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly ConcurrentDictionary<string,string> Cache = new(StringComparer.Ordinal);
    private static Dictionary<string,string> Load()
    {
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("SpireStrategist.English.json")
            ?? throw new InvalidOperationException("Missing English localization resource.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    private static Dictionary<string,string> MakeDisplayCatalog()
    {
        var result=new Dictionary<string,string>(Catalog,StringComparer.Ordinal);
        foreach(var (key,value) in Catalog)
        {
            result.TryAdd(key.Trim(),value.Trim());
            var a=key.Split('\n');var b=value.Split('\n');
            if(a.Length!=b.Length)continue;
            for(var i=0;i<a.Length;i++)
                if(Han.IsMatch(a[i]) && Slots.Matches(a[i]).Select(m=>m.Value).Order().SequenceEqual(Slots.Matches(b[i]).Select(m=>m.Value).Order()))
                    result.TryAdd(a[i].Trim(),b[i].Trim());
        }
        result.Remove("");return result;
    }
    private static Template[] MakeTemplates() => DisplayCatalog.Where(kv=>Slots.IsMatch(kv.Key))
        .SelectMany(kv=>new[]{kv,new KeyValuePair<string,string>(kv.Key.Trim('\r','\n'),kv.Value.Trim('\r','\n'))})
        .DistinctBy(kv=>kv.Key).OrderByDescending(kv=>Slots.Replace(kv.Key,"").Length)
        .Select(kv=>{
            var pieces=Slots.Split(kv.Key);var pattern="";var anchors=new List<string>();
            for(var i=0;i<pieces.Length;i++)
                if(i%2==0){pattern+=Regex.Escape(pieces[i]);if(pieces[i].Length>1)anchors.Add(pieces[i]);}
                else pattern+="(?<p"+pieces[i]+">[^\\r\\n]*?)";
            return new Template(kv.Key,kv.Value,new Regex("\\A"+pattern+"\\z",RegexOptions.Singleline|RegexOptions.CultureInvariant|RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(500)),anchors.ToArray());
        }).ToArray();

    public static string English(string text)
    {
        if(string.IsNullOrEmpty(text)||!Han.IsMatch(text))return text;
        if(Cache.TryGetValue(text,out var cached))return cached;
        var translated=Translate(text,0);
        if(Cache.Count>4096)Cache.Clear();
        Cache.TryAdd(text,translated);return translated;
    }
    private static string Translate(string text,int depth)
    {
        if(depth>8||!Han.IsMatch(text))return text;
        if(DisplayCatalog.TryGetValue(text,out var exact))return exact;
        var trimmed=text.Trim();
        if(trimmed.Length!=text.Length)
        {
            var start=text.Length-text.TrimStart().Length;
            return text[..start]+Translate(trimmed,depth+1)+text[(start+trimmed.Length)..];
        }
        var scored=Regex.Match(text,@"\A([+-]?\d+(?:[.,]\d+)?\s{2,})(.+)\z");
        if(scored.Success)return scored.Groups[1].Value+Translate(scored.Groups[2].Value,depth+1);
        foreach(var template in Templates)
        {
            if(template.Anchors.Any(a=>!text.Contains(a,StringComparison.Ordinal)))continue;
            var match=template.Pattern.Match(text);
            if(match.Success)return Slots.Replace(template.Target,m=>Translate(match.Groups["p"+m.Groups[1].Value].Value,depth+1));
        }
        if(text.StartsWith("· ",StringComparison.Ordinal))return "· "+Translate(text[2..],depth+1);
        if(text.Contains('\n'))return string.Join("\n",text.Split('\n').Select(line=>Translate(line,depth+1)));
        // Prefixes/suffixes support concatenated status messages and embedded model titles.
        // Exact phrases win over short words; original numeric values and unknown names are preserved.
        return Literals.Replace(text,m=>DisplayCatalog[m.Value]);
    }
}
