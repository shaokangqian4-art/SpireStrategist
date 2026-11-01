using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

if(args.Length<2){Console.Error.WriteLine("AnalyzeCombat output.json batch-dir...");return 2;}
var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
var rows=new List<Row>();var policy=new Policy(allowExperimentalStoke:true);var releasedPolicy=new Policy();
Row Classify(Row r)
{
    if(r.Status!="Failed" || !r.CombatEnded || r.Error?.Contains("战斗结束，但仍存在未死亡敌人。")!=true)return r;
    var path=Path.Combine(Path.GetDirectoryName(r.ResultPath)!,"SpireStrategist","latest-state.json");
    if(!File.Exists(path))return r;
    var state=JsonSerializer.Deserialize<RunSnapshot>(File.ReadAllText(path),json)!;
    return state.Hp==0 && state.Room=="CombatRoom" && state.Character=="Ironclad"
        ?r with{Status="Defeat",RawSolverStatus="Failed",ActualFinalHp=0,HpSource="terminal-native-combat-and-recorded-zero-hp"}:r;
}
var observations=args.Skip(1).SelectMany(directory=>JsonSerializer.Deserialize<Row[]>(File.ReadAllText(Path.Combine(directory,"summary.json")),json)!.Select(r=>Classify(r with{Batch=directory}))).ToArray();
Row WithFixture(Row r)
{
    var path=Path.Combine(Path.GetDirectoryName(r.ResultPath)!,"combat_solver_test_request.json");
    // The native worker consumes its input file; the launcher retains the immutable request.
    if(!File.Exists(path))path=Path.Combine(r.Batch,$"{r.Seed}-{r.Variant}","request.json");
    using var request=JsonDocument.Parse(File.ReadAllText(path));var root=request.RootElement;
    using var result=JsonDocument.Parse(File.ReadAllText(r.ResultPath));
    if(root.TryGetProperty("preCombatPlayerCurrentHpOverride",out var hp))
    {
        var actualHp=hp.GetInt32();
        if(!result.RootElement.GetProperty("completedChecks").EnumerateArray().Any(c=>c.GetString()==$"PreCombatPlayerHp:{actualHp}"))
            throw new Exception("Unverified pre-combat HP: "+r.ResultPath);
        return r with{InitialHp=actualHp,MaxHp=root.GetProperty("initialPlayerMaxHp").GetInt32(),Ascension=root.GetProperty("ascension").GetInt32(),InitialHpSource="native-pre-combat-check"};
    }
    if(root.TryGetProperty("preserveNativeCombatStateForTest",out var preserve)&&preserve.GetBoolean())
    {
        // This mode ignores initialPlayerHp. Legacy requests must be reconstructed from evidence.
        foreach(var log in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(r.ResultPath)!,"SpireStrategist"),"session-*.jsonl").Order())
        foreach(var line in File.ReadLines(log))
        {
            using var record=JsonDocument.Parse(line);
            if(record.RootElement.GetProperty("type").GetString()!="state")continue;
            var snapshot=record.RootElement.GetProperty("payload").GetProperty("state").Deserialize<RunSnapshot>(json)!;
            if(snapshot.Room=="CombatRoom")return r with{InitialHp=snapshot.Hp,MaxHp=snapshot.MaxHp,Ascension=snapshot.Ascension,InitialHpSource="legacy-first-recorded-combat-state"};
        }
        throw new Exception("No native initial HP evidence: "+r.ResultPath);
    }
    throw new Exception("Unverified fixture mode: "+r.ResultPath);
}
observations=observations.Select(WithFixture).ToArray();
bool Valid(Row r)=>(r.Status is "Passed" or "Defeat") && r.CombatEnded && r.ActualFinalHp!=null && r.HiddenReadPassed && r.AdvisorErrors.Length==0;
var failedObservations=observations.Where(r=>!Valid(r)).ToArray();
foreach(var group in observations.Where(Valid).GroupBy(r=>(r.Seed,r.Encounter,r.Ascension,r.InitialHp,r.MaxHp)))
{
        if(group.Count()!=4 || group.Select(r=>r.Variant).Distinct().Count()!=4)throw new Exception("Incomplete/duplicate comparison group: "+group.Key);
        var baseline=group.SingleOrDefault(r=>r.Variant=="leave");
        if(baseline==null || baseline.ActualFinalHp==null)throw new Exception("Missing baseline: "+group.Key);
        var folder=Path.GetDirectoryName(baseline.ResultPath)!;
        RunSnapshot? state=null;
        foreach(var file in Directory.GetFiles(Path.Combine(folder,"SpireStrategist"),"session-*.jsonl"))
            foreach(var line in File.ReadLines(file))
            {
                using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
                if(root.GetProperty("type").GetString()!="state")continue;
                var current=root.GetProperty("payload").GetProperty("state").Deserialize<RunSnapshot>(json)!;
                // This experiment explicitly installs 15 cards; reject starter snapshots before injection.
                if(current.Deck.Length==15 && current.Deck.Any(c=>c.ClassName=="FeelNoPain"))state??=current with{Hp=baseline.InitialHp,MaxHp=baseline.MaxHp,Ascension=baseline.Ascension};
            }
        if(state==null)throw new Exception("No experiment deck snapshot: "+folder);
        foreach(var row in group)
        {
            if(!Valid(row))
                throw new Exception("Incomplete or invalid native observation: "+row.ResultPath);
            var cardClass=row.Variant switch{"recover_strike"=>"StrikeIronclad","add_stoke"=>"Stoke","duplicate_trance"=>"BattleTrance",_=>null};
            var card=cardClass==null?null:CardRegistry.All[cardClass].Basic;
            var marginal=card==null?0:policy.Evaluate(state,[new("pick",card.Name,"card",Card:card),new("skip","跳过","skip")]).Options[0].Score;
            var released=card==null?0:releasedPolicy.Evaluate(state,[new("pick",card.Name,"card",Card:card),new("skip","跳过","skip")]).Options[0].Score;
            var recovery=card==null?null:RecoveryPolicy.Evaluate(state,card);
            var shop=card==null?null:ShopPolicy.Evaluate(state with{Gold=300},[new("buy",card,75)],null,300);
            rows.Add(row with{Split=row.Seed==103?"holdout":"exploration",HpDelta=row.ActualFinalHp-baseline.ActualFinalHp,RewardScore=marginal,ReleasedRewardScore=released,
                RecoveryAction=recovery?.Action,ShopAtHypothetical75Gold=shop?.Recommendation?.Kind??"skip",Won=row.ActualFinalHp>0});
        }
}
var aggregate=rows.Where(r=>r.Variant!="leave").GroupBy(r=>(r.Split,r.Variant,r.Ascension,r.InitialHp,r.MaxHp)).Select(g=>new{g.Key.Split,g.Key.Variant,g.Key.Ascension,g.Key.InitialHp,g.Key.MaxHp,pairs=g.Count(),meanHpDelta=g.Average(r=>r.HpDelta),better=g.Count(r=>r.HpDelta>0),equal=g.Count(r=>r.HpDelta==0),worse=g.Count(r=>r.HpDelta<0),deaths=g.Count(r=>!r.Won)}).ToArray();
File.WriteAllText(args[0],JsonSerializer.Serialize(new{policy=Policy.Version,kind="paired-native-combat-pilot-not-full-run-winrate",limitations=new[]{"Small sample, one fixed deck. Wall-clock-limited combat search can vary with machine load.","Shop at 75 gold is an offline sensitivity probe, not an observed store purchase.","Marginal HP after one combat is not long-run value. No broad weight fitting is justified.","Failed attempts are retained separately and never treated as measured losses or wins."},aggregate,failedObservations,rows},json));
Console.WriteLine(JsonSerializer.Serialize(aggregate,json));return 0;

record Row(int Seed,string Variant,string Encounter,string Status,bool CombatEnded,int FinishedTurn,int? ActualFinalHp,bool HiddenReadPassed,string[] AdvisorErrors,string ResultPath)
{
    public int InitialHp{get;init;}public int MaxHp{get;init;}public int Ascension{get;init;}public string InitialHpSource{get;init;}="";
    public string Batch{get;init;}="";public string Split{get;init;}="";public int? HpDelta{get;init;}
    public double? RewardScore{get;init;}public string? RecoveryAction{get;init;}public string? ShopAtHypothetical75Gold{get;init;}public bool Won{get;init;}
    public double? ReleasedRewardScore{get;init;}
    public string? Error{get;init;}public string? RawSolverStatus{get;init;}public string HpSource{get;init;}="NativeOutcome";
}
