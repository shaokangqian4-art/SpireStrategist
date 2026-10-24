using System.Text.Json;
using SpireStrategist.Core;

internal static class ReviewChecks
{
    internal static object Run(string output,JsonSerializerOptions json)
    {
        var relics=JsonSerializer.Deserialize<string[]>(File.ReadAllText("data/relic-test-ids.json"))!;
        if(relics.Length!=102)throw new InvalidOperationException("Relic fixture incomplete: "+relics.Length);
        CardSnapshot C(string id)=>CardRegistry.All[id].Basic;
        var starters=Enumerable.Repeat(C("StrikeIronclad"),5).Concat(Enumerable.Repeat(C("DefendIronclad"),4)).Append(C("Bash")).ToArray();
        string[][] engines=[[],["Inflame","TwinStrike","SwordBoomerang"],["Corruption","FeelNoPain","DarkEmbrace","BurningPact"],
            ["Barricade","Impervious","Entrench","BodySlam"],["Offering","Bloodletting","Hemokinesis","Rupture"],["PommelStrike","BattleTrance","BurningPact","MasterOfStrategy"]];
        var failures=new List<string>();var cases=0;
        foreach(var engine in engines)foreach(var hp in new[]{1,10,30,60,80})foreach(var gold in new[]{0,80,200,800})foreach(var rows in new[]{16,26,316})
        {
            var state=new RunSnapshot{Character="Ironclad",Hp=hp,MaxHp=80,Gold=gold,Act=2,Floor=31,Energy=3,Deck=starters.Concat(engine.Select(C)).ToArray(),PotionSlots=new string?[2],Map=new(3,rows,5,rows-1,"fixture")};
            var before=JsonSerializer.Serialize(state,json);
            foreach(var id in relics)
            {
                cases++;var score=RelicValue.Evaluate(id,state,true);
                if(!score.Supported||!double.IsFinite(score.Value))failures.Add("unsupported/nonfinite:"+id);
                if(score!=RelicValue.Evaluate(id,state with{Floor=9999},true))failures.Add("hardcoded absolute floor:"+id);
            }
            if(before!=JsonSerializer.Serialize(state,json))failures.Add("input mutation");
            if(new StrategicValue(state with{Relics=["RELIC.SOZU"]}).Potion(2)!=0)failures.Add("Sozu potion value");
        }
        var report=new{kind="structural-context-matrix-not-training-or-winrate",relics=relics.Length,engines=engines.Length,cases,failures=failures.Distinct().ToArray(),source="data/relic-test-ids.json"};
        var probes=relics.Select(id=>new{id,examples=engines.Select((engine,index)=>
        {
            var state=new RunSnapshot{Character="Ironclad",Hp=60,MaxHp=80,Gold=200,Act=2,Energy=3,Deck=starters.Concat(engine.Select(C)).ToArray(),PotionSlots=new string?[2],Map=new(3,26,5,25,"fixture")};
            return new{engine=new[]{"初始牌组","力量多段","消耗","格挡转伤","自伤","过牌"}[index],score=Math.Round(RelicValue.Evaluate(id,state,true).Value,3)};
        }).ToArray()}).ToArray();
        File.WriteAllText(Path.Combine(output,"relic-probes.json"),JsonSerializer.Serialize(probes,json));
        File.WriteAllText(Path.Combine(output,"review-checks.json"),JsonSerializer.Serialize(report,json));
        if(failures.Count>0)throw new InvalidOperationException(string.Join(';',failures.Take(5)));
        return report;
    }
}
