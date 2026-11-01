using System.Diagnostics;
using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
    Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping,WriteIndented=true};
using var input=JsonDocument.Parse(File.ReadAllText(args[0]));
var decisions=new List<object>();var routes=new List<object>();var changes=0;var routeChanges=0;var maxMs=0d;
foreach(var row in input.RootElement.GetProperty("decisions").EnumerateArray())
{
    var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var result=new Policy().Evaluate(s,row.GetProperty("options").Deserialize<DecisionOption[]>(json)!);
    var old=row.GetProperty("old").Deserialize<Evaluation>(json)!;
    if(result.RecommendedKey!=old.RecommendedKey)changes++;
    var upgrades=s.Deck.Where(c=>c.CanUpgrade).OrderByDescending(c=>CardMechanics.Upgrade(s,c))
        .Select(c=>new{c.Name,value=CardMechanics.Upgrade(s,c)}).Take(5).ToArray();
    decisions.Add(new{seed=row.GetProperty("seed").GetString(),id=row.GetProperty("id").GetString(),s.Floor,s.Hp,
        kind=row.GetProperty("kind").GetString(),old,result,upgrades});
}
foreach(var row in input.RootElement.GetProperty("routes").EnumerateArray())
{
    var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var graph=row.GetProperty("nodes").Deserialize<RouteNode[]>(json)!;
    var legal=row.GetProperty("legal").Deserialize<string[]>(json)!;
    var economy=row.GetProperty("economy").Deserialize<RouteEconomy>(json)!;
    var timer=Stopwatch.StartNew();var result=RoutePolicy.Evaluate(s,graph,legal,economy);timer.Stop();
    maxMs=Math.Max(maxMs,timer.Elapsed.TotalMilliseconds);
    if(result.Options.Any(o=>!double.IsFinite(o.Score)||o.Path.Length>12)||result.RecommendedNode is {} n&&!legal.Contains(n))
        throw new Exception("Invalid route result");
    var old=row.GetProperty("old").Deserialize<RouteEvaluation>(json)!;
    if(result.RecommendedNode!=old.RecommendedNode)routeChanges++;
    routes.Add(new{seed=row.GetProperty("seed").GetString(),s.Floor,s.Hp,old,result,milliseconds=timer.Elapsed.TotalMilliseconds});
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{version=Policy.Version,decisions,routes,
    summary=new{decisionCount=decisions.Count,changedDecisions=changes,routeCount=routes.Count,changedRoutes=routeChanges,maxRouteMilliseconds=maxMs}},json));
Console.WriteLine($"Decisions {decisions.Count}, changes {changes}; routes {routes.Count}, changes {routeChanges}; max route {maxMs:F1}ms. No battles replayed.");
