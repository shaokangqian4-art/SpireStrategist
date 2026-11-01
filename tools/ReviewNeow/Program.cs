using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,WriteIndented=true,
    Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
using var fixture=JsonDocument.Parse(File.ReadAllText("tests/SpireStrategist.Tests/Fixtures/feedback-0612.json"));
var neow=fixture.RootElement.GetProperty("NEOW").GetProperty("decision").GetProperty("state").Deserialize<RunSnapshot>(json)!;
using var labels=JsonDocument.Parse(File.ReadAllText("artifacts/review-0.6.12/neow-labels.json"));
var v=new StrategicValue(neow);
var relics=labels.RootElement.EnumerateArray().Select(l=>new {
    Id=l.GetProperty("id").GetString()!,Name=l.GetProperty("name").GetString(),
    Value=RelicValue.Evaluate(l.GetProperty("id").GetString()!,neow,true).Value,
    ExtraTen=RelicValue.Evaluate(l.GetProperty("id").GetString()!,neow with{
        Map=neow.Map! with{Rows=26,BossRow=26,ExtraLayers=10}},true).Value
}).OrderByDescending(r=>r.Value).ToArray();
var wellspring=fixture.RootElement.GetProperty("WELLSPRING").GetProperty("decision").GetProperty("state").Deserialize<RunSnapshot>(json)!;
var choices=new[]{"BATHE","BOTTLE"}.Select(k=>EventRules.Describe("WELLSPRING",k,k,wellspring,new Dictionary<string,double>())).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new{NeowState=neow,Remaining=v.Remaining,Horizon=v.Horizon,
    Remove=v.RemoveBest(1),Upgrade=v.UpgradeBest(1),RandomUpgrade=v.RandomUpgrade(1),
    Relics=relics,Wellspring=EventPolicy.Evaluate(wellspring,choices)},json));
