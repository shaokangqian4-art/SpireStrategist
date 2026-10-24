using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var output = args.Length > 0 ? args[0] : "artifacts/evaluation";
Directory.CreateDirectory(output);
CardSnapshot C(string id) => CardRegistry.All[id].Basic;
var starter = Enumerable.Repeat(C("StrikeIronclad"), 5).Concat(Enumerable.Repeat(C("DefendIronclad"), 4)).Append(C("Bash")).ToArray();
var decks = new[] { starter, starter.Concat(new[]{C("TrueGrit"), C("BurningPact"), C("FeelNoPain")}).ToArray(),
    starter.Concat(new[]{C("Inflame"),C("TwinStrike"),C("SwordBoomerang")}).ToArray(), starter.Concat(starter).ToArray() };
var policy = new Policy();
var failures = new List<string>();
var total = 0; var skips = 0; var selected = 0;
foreach (var card in CardRegistry.All.Values.SelectMany(d => new[] { d.Basic, d.Upgraded }))
foreach (var hp in new[] { 1, 15, 40, 65, 80 })
foreach (var energy in new[] { 2, 3, 5 })
foreach (var deck in decks)
{
    var run = new RunSnapshot { Character="Ironclad", Hp=hp, MaxHp=80, Energy=energy, Act=1, Deck=deck };
    DecisionOption[] choices = [new("card", card.Name, "card", Card:card), new("skip", "skip", "skip")];
    var before = JsonSerializer.Serialize(run, json);
    var result = policy.Evaluate(run, choices);
    total++;
    if (result.RecommendedKey == "skip") skips++; else if(result.RecommendedKey == "card") selected++;
    if (result.RecommendedKey == null || result.Options.Any(o => o.Score is not double score || !double.IsFinite(score))) failures.Add("Unscored:"+card.ClassName);
    if (JsonSerializer.Serialize(result,json) != JsonSerializer.Serialize(policy.Evaluate(run, choices),json)) failures.Add("Nondeterministic:"+card.ClassName);
    if (before != JsonSerializer.Serialize(run,json)) failures.Add("Mutation:"+card.ClassName);
    var reversed = policy.Evaluate(run, choices.Reverse().ToArray());
    if(reversed.RecommendedKey!=result.RecommendedKey) failures.Add("Ordering:"+card.ClassName);
}
// Real user records are replayed for coverage and recommendation changes, never assumed to be labels.
var replays = new List<object>();
if (args.Length > 1 && Directory.Exists(args[1]))
foreach (var file in Directory.EnumerateFiles(args[1], "session-*.jsonl"))
foreach (var line in File.ReadLines(file))
{
    using var doc = JsonDocument.Parse(line);
    if(doc.RootElement.GetProperty("type").GetString()!="decision") continue;
    var p=doc.RootElement.GetProperty("payload");
    var state=p.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var choices=p.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
    var old=p.GetProperty("evaluation").Deserialize<Evaluation>(json)!;
    var current=policy.Evaluate(state,choices);
    replays.Add(new {id=p.GetProperty("id").GetString(),hp=state.Hp,floor=state.Floor,
        previousRecommendation=old.RecommendedKey,currentRecommendation=current.RecommendedKey,
        previouslySupported=old.Options.Count(o=>o.Supported),currentlySupported=current.Options.Count(o=>o.Supported),
        unknownDeck=state.Deck.Where(c=>CardCatalog.Find(c)==null).Select(c=>c.ClassName).ToArray(),evaluation=current});
}
var reviewChecks=ReviewChecks.Run(output,json);
var report=new {utc=DateTimeOffset.UtcNow,policy=Policy.Version,game="0.111.0",kind="structural-and-replay-not-winrate",reviewChecks,
    ironclad=CardRegistry.All.Values.Count(c=>c.Pool=="IroncladCardPool"),colorless=CardRegistry.All.Values.Count(c=>c.Pool=="ColorlessCardPool"),
    cases=total,failures,selected,skips,realDecisionsReplayed=replays.Count};
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(report,json));
File.WriteAllText(Path.Combine(output,"replays.json"),JsonSerializer.Serialize(replays,json));
var coverage=CardRegistry.All.Values.Select(d=>new
{
    d.Pool,d.Basic.ClassName,d.Basic.Id,
    status=ExtraCardModels.MultiplayerOnly.Contains(d.Basic.ClassName)?"multiplayer-excluded":
        CardCatalog.Profiles.ContainsKey(d.Basic.ClassName)||ExtraCardModels.Find(d.Basic)!=null?"explicit-prior":"generic-estimate",
    basic=CardCatalog.Find(d.Basic),upgraded=CardCatalog.Find(d.Upgraded),
    validation="Mechanism-informed heuristic; not a win-rate-trained value."
});
File.WriteAllText(Path.Combine(output,"card-coverage.json"),JsonSerializer.Serialize(coverage,json));
Console.WriteLine(JsonSerializer.Serialize(report,json));
return failures.Count==0?0:1;
