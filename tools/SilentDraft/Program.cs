using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
if(args.Length==3&&args[0]=="evaluate")
{
    using var input=JsonDocument.Parse(File.ReadAllText(args[1]));
    var s=input.RootElement.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var o=input.RootElement.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
    File.WriteAllText(args[2],JsonSerializer.Serialize(new{composition=SilentPolicy.Analyze(s),evaluation=SilentPolicy.Evaluate(s,o)},json));
    return 0;
}
if(args.Length!=2||args[0]!="examples")throw new ArgumentException("SilentDraft examples <output.json> OR evaluate <state-options.json> <output.json>");
CardSnapshot C(string name,bool up=false)=>SilentPolicy.Card(name,up)!;
var start=new RunSnapshot{Character="Silent",Hp=60,MaxHp=70,Act=1,Energy=3,Ascension=10,
    Deck=[..Enumerable.Repeat(C("StrikeSilent"),5),..Enumerable.Repeat(C("DefendSilent"),5),C("Neutralize"),C("Survivor")],Relics=["RELIC.RING_OF_THE_SNAKE"]};
RunSnapshot S(int act,params string[] ids)=>start with{Act=act,Deck=[..start.Deck,..ids.Select(x=>C(x))]};
var samples=new[]{
    ("初始开放",start),
    ("产刀尚缺终端",S(1,"BladeOfInk","CloakAndDagger","LeadingStrike")),
    ("毒已有入口",S(2,"NoxiousFumes","BouncingFlask","Backflip","LegSweep")),
    ("运转成熟缺输出",S(2,"Acrobatics","CalculatedGamble","HiddenDaggers","Reflex","Tactician","Afterimage","Backflip")),
    ("小刀含涂毒但不转毒",S(2,"BladeOfInk","Accuracy","KnifeTrap","Envenom","Backflip","Footwork")),
    ("大牌组同功能饱和",S(3,"DaggerThrow","DaggerThrow","Backflip","Backflip","Backflip","Acrobatics","Acrobatics","Footwork","Deflect","Prepared","Prepared","ToolsOfTheTrade","Afterimage"))
};
var examples=samples.Select(sample=>new{name=sample.Item1,state=sample.Item2,composition=SilentPolicy.Analyze(sample.Item2),
    scores=SilentPolicy.Profiles.Keys.SelectMany(id=>new[]{false,true}.Select(up=>{
        var c=C(id,up);var r=SilentPolicy.Evaluate(sample.Item2,[new("card",c.Name,"card",Card:c),new("skip","跳过","skip")]);
        return new{className=id,name=c.Name,upgraded=up,score=r.Options[0].Score,parts=r.Options[0].Parts};
    })).ToArray()}).ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1],JsonSerializer.Serialize(examples,json));
Console.WriteLine($"Exported {examples.Length} scenarios, {examples.Sum(x=>x.scores.Length)} scores. Heuristics, not win rates.");
return 0;
