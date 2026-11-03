using System.Diagnostics;
using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
    Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping,WriteIndented=true};
using var input=JsonDocument.Parse(File.ReadAllText(args[0]));
var decisions=new List<object>();var routes=new List<object>();var changes=0;var routeChanges=0;var maxMs=0d;
var shops=new List<object>();
var events=new List<object>();
if(input.RootElement.TryGetProperty("events",out var eventRows))foreach(var row in eventRows.EnumerateArray())
{
    var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var choices=row.GetProperty("choices").Deserialize<EventChoice[]>(json)!;
    var eventId=row.GetProperty("eventId").GetString()!;
    if(eventId.EndsWith("HUNGRY_FOR_MUSHROOMS"))
    {
        var keys=row.GetProperty("textKeys").Deserialize<string[]>(json)!;
        choices=choices.Select((c,i)=>keys[i].EndsWith("BIG_MUSHROOM")||keys[i].EndsWith("FRAGRANT_MUSHROOM")?
            EventRules.Describe("HUNGRY_FOR_MUSHROOMS",keys[i],c.Name,s,new Dictionary<string,double>())
            with{Key=c.Key,Enabled=c.Enabled,Lethal=c.Lethal}:c).ToArray();
    }
    events.Add(new{s.Floor,eventId,old=row.GetProperty("old").Deserialize<Evaluation>(json),result=EventPolicy.Evaluate(s,choices)});
}
if(input.RootElement.TryGetProperty("shops",out var shopRows))foreach(var row in shopRows.EnumerateArray())
{
    var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var stock=row.GetProperty("stock").Deserialize<ShopCard[]>(json)!;
    var result=ShopPolicy.Evaluate(s,stock,row.TryGetProperty("removalPrice",out var price)&&price.ValueKind!=JsonValueKind.Null?price.GetInt32():null,s.Gold);
    shops.Add(new{s.Floor,result});
}
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
var upgradeAudit=new List<object>();
if(args.Length>2)foreach(var file in new[]{"cards","silent-cards","defect-cards","regent-cards","necrobinder-cards"})
{
    using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(args[2],file+"-v0.111.0.json")));
    foreach(var row in native.RootElement.EnumerateArray())
    {
        if(row.TryGetProperty("multiplayerConstraint",out var mp)&&mp.GetString()=="MultiplayerOnly")continue;
        var basic=row.GetProperty("basic").Deserialize<CardSnapshot>(json)!;
        var upgraded=row.GetProperty("upgraded").Deserialize<CardSnapshot>(json)!;
        if(!basic.CanUpgrade)continue;
        var hero=file switch{"silent-cards"=>"Silent","defect-cards"=>"Defect","regent-cards"=>"Regent","necrobinder-cards"=>"Necrobinder",_=>"Ironclad"};
        var starter=UpgradeReview.Native("Strike"+hero,false);var defend=UpgradeReview.Native("Defend"+hero,false);
        var deck=new[]{starter,starter,starter,defend,defend,defend,basic}.Where(c=>c!=null).Select(c=>c!).ToArray();
        var s=new RunSnapshot{Character=hero,Act=2,Hp=60,MaxHp=80,Energy=3,Deck=deck};
        var score=CardMechanics.Upgrade(s,basic);
        if(!double.IsFinite(score))throw new Exception("Invalid upgrade score: "+basic.ClassName);
        var vars=basic.Variables.Keys.Union(upgraded.Variables.Keys).Where(k=>basic.Variables.GetValueOrDefault(k)!=upgraded.Variables.GetValueOrDefault(k))
            .Select(k=>new{key=k,before=basic.Variables.GetValueOrDefault(k),after=upgraded.Variables.GetValueOrDefault(k)}).ToArray();
        upgradeAudit.Add(new{hero,pool=row.TryGetProperty("pool",out var pool)?pool.GetString():null,basic.ClassName,basic.Name,basic.Rarity,profileAvailable=CardCatalog.Find(basic)!=null,referenceScore=score,context="Act 2, 60/80 HP, 3 energy, three Strikes + three Defends + candidate; no synergy or relics. Not a universal ranking. A zero score or missing profile is not proof of a modeled card.",
            costBefore=basic.Cost,costAfter=upgraded.Cost,starBefore=basic.StarCost,starAfter=upgraded.StarCost,keywordsBefore=basic.Keywords,keywordsAfter=upgraded.Keywords,variables=vars,
            runtimeBranchReview=vars.Length==0&&basic.Cost==upgraded.Cost&&basic.StarCost==upgraded.StarCost&&basic.Keywords.SequenceEqual(upgraded.Keywords)});
    }
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{version=Policy.Version,decisions,routes,shops,events,upgradeAudit,
    summary=new{decisionCount=decisions.Count,changedDecisions=changes,routeCount=routes.Count,changedRoutes=routeChanges,maxRouteMilliseconds=maxMs}},json));
Console.WriteLine($"Decisions {decisions.Count}, changes {changes}; routes {routes.Count}, changes {routeChanges}; max route {maxMs:F1}ms. No battles replayed.");
