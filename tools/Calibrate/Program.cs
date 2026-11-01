using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

if(args.Length < 2) { Console.Error.WriteLine("Calibrate <output-dir> <batch-dir> [batch-dir ...]; seeds divisible by 3 are held out."); return 2; }
var json = new JsonSerializerOptions {PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
var groups = new List<Sample>();
foreach(var dir in args.Skip(1))
{
    var summary = JsonSerializer.Deserialize<Row[]>(File.ReadAllText(Path.Combine(dir,"summary.json")),json)!;
    foreach(var group in summary.GroupBy(r=>r.Seed))
    {
        var rows=group.ToArray();
        if(rows.Length!=3 || rows.Any(r=>r.Status!="Passed" || !r.CombatEnded || r.ActualFinalHp==null)) continue;
        var baseline=rows.Single(r=>r.Variant=="baseline");
        var stateDir=Path.Combine(Path.GetDirectoryName(baseline.ResultPath)!,"SpireStrategist");
        RunSnapshot? state=null;
        foreach(var line in Directory.EnumerateFiles(stateDir,"session-*.jsonl").SelectMany(File.ReadLines))
        {
            using var doc=JsonDocument.Parse(line);
            if(doc.RootElement.GetProperty("type").GetString()!="state") continue;
            state=doc.RootElement.GetProperty("payload").GetProperty("state").Deserialize<RunSnapshot>(json);
            break;
        }
        if(state!=null) groups.Add(new(Path.GetFileName(dir)+":"+group.Key,group.Key%3==0,state,rows));
    }
}
DecisionOption[] choices=[new("baseline","跳过","skip"),new("shrug","耸肩无视","card",Card:CardRegistry.All["ShrugItOff"].Basic),new("dismantle","拆解","card",Card:CardRegistry.All["Dismantle"].Basic)];
double Loss(PolicyWeights weights, IEnumerable<Sample> samples)
{
    var policy=new Policy(weights); double loss=0;var pairs=0;
    foreach(var sample in samples)
    {
        var predicted=policy.Evaluate(sample.State,choices).Options.ToDictionary(o=>o.Key,o=>o.Score!.Value);
        for(var i=0;i<sample.Rows.Length;i++) for(var j=i+1;j<sample.Rows.Length;j++)
        {
            var a=sample.Rows[i];var b=sample.Rows[j];var delta=a.ActualFinalHp!.Value-b.ActualFinalHp!.Value;
            if(delta==0) continue;
            pairs++;
            var margin=Math.Sign(delta)*(predicted[a.Variant]-predicted[b.Variant]);
            loss+=Math.Max(0,1-margin)*Math.Min(10,Math.Abs(delta));
        }
    }
    return pairs==0?0:loss/pairs;
}
var train=groups.Where(s=>!s.Holdout).ToArray();var holdout=groups.Where(s=>s.Holdout).ToArray();
var initial=new PolicyWeights();var best=initial;var baselineLoss=Loss(initial,train);var bestLoss=baselineLoss;var trials=0;
// Offline parameter trials use recorded outcomes; they never search game futures.
foreach(var scale in new[]{.5,.75,1.25,1.5})
foreach(var field in new[]{"damage","block","draw","tax"})
{
    var candidate=field switch {"damage"=>initial with{DamageNeed=initial.DamageNeed*scale},"block"=>initial with{BlockNeed=initial.BlockNeed*scale},"draw"=>initial with{Draw=initial.Draw*scale},_=>initial with{CardTax=initial.CardTax*scale}};
    var loss=Loss(candidate,train);trials++;
    if(loss<bestLoss-.00001){best=candidate;bestLoss=loss;}
}
var initialHoldout=Loss(initial,holdout);var candidateHoldout=Loss(best,holdout);
var passesGate=train.Length>=2 && holdout.Length>=1 && bestLoss<baselineLoss && candidateHoldout<initialHoldout;
Directory.CreateDirectory(args[0]);
File.WriteAllText(Path.Combine(args[0],"candidate-weights.json"),JsonSerializer.Serialize(best,json));
var report=new {policy=Policy.Version,scope="single-encounter post-combat HP ranking; not full-run win rate",trainingGroups=train.Length,holdoutGroups=holdout.Length,trials,
    baselineTrainingLoss=baselineLoss,candidateTrainingLoss=bestLoss,baselineHoldoutLoss=initialHoldout,candidateHoldoutLoss=candidateHoldout,
    passesGate,deployed=false,reason=passesGate?"局部候选通过留出检查；样本规模有限，仅输出候选，需扩大场景验证。":"没有同时改善训练组和留出组，保留默认权重。",
    groups=groups.Select(s=>new{s.Id,s.Holdout,outcomes=s.Rows.Select(r=>new{r.Variant,r.ActualFinalHp})}).ToArray()};
File.WriteAllText(Path.Combine(args[0],"calibration.json"),JsonSerializer.Serialize(report,json));
Console.WriteLine(JsonSerializer.Serialize(report,json));
return groups.Count>0?0:1;
record Row(int Seed,string Variant,string Status,bool CombatEnded,int? ActualFinalHp,string ResultPath);
record Sample(string Id,bool Holdout,RunSnapshot State,Row[] Rows);
