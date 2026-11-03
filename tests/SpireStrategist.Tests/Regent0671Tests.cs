using System.Text.Json;
using SpireStrategist.Core;

static class Regent0671Tests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string n,Action f)=>tests.Add(("0671: "+n,f));
        void Check(bool b,string why){if(!b)throw new Exception(why);}
        var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        using var live=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-0671-live.json")));
        var rows=live.RootElement.EnumerateArray().Select(r=>r.Clone()).ToArray();
        RunSnapshot S(JsonElement r)=>r.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        CardSnapshot C(string n)=>HeroPolicy.Card(n)??DefectPolicy.Card(n)??SilentPolicy.Card(n)??CardRegistry.All[n].Basic;
        Test("complete tier chart is a global prior, independent of A10 evidence and rarity",()=>{
            using var chart=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-tier-review.json")));
            var root=chart.RootElement;var entries=root.GetProperty("cards").EnumerateArray().ToArray();
            Check(entries.Length==86&&entries.Select(x=>x.GetProperty("className").GetString()).Distinct().Count()==86,"complete single player chart, no duplicate mapping");
            foreach(var entry in entries){
                var id=entry.GetProperty("className").GetString()!;
                var expected=root.GetProperty("tiers").GetProperty(entry.GetProperty("tier").GetString()!).GetProperty("base").GetDouble();
                Check(HeroPolicy.Profiles[id].Base==expected,id+" must use reviewed base itself, not a statistical nudge");
            }
            Check(HeroPolicy.Profiles["GuidingStar"].Base>HeroPolicy.Profiles["SevenStars"].Base,"a good common can beat a weak rare; no blind rarity ordering");
            var s=S(rows[0]) with{Foresight=null};
            foreach(var core in new[]{"BigBang","Tyranny","Orbit"}){
                var opts=new[]{new DecisionOption("strong",core,"card",Card:C(core)),new DecisionOption("filler","CrushUnder","card",Card:HeroPolicy.Card("CrushUnder",true)),new DecisionOption("skip","skip","skip")};
                var e=new Policy().Evaluate(s,opts);
                Check(e.Options.Single(o=>o.Key=="strong").Score>e.Options.Single(o=>o.Key=="filler").Score,core+" beats upgraded filler in real supported deck");
            }
        });
        Test("real floor30 respects owned strength outlet with and without foresight",()=>{
            var row=rows.First(r=>S(r).Floor==30);var state=S(row);
            var options=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
            foreach(var s in new[]{state,state with{Foresight=null}}){
                Check(RegentDirection.Analyze(s).Committed==RegentDirection.Plan.StrengthAttacks,"identify Volley/Jade/Mittens instead of undecided");
                Check(HeroPolicy.Analyze(s).DamageEngines>0,"missing auxiliary draw is not missing damage core");
                Check(HeroForecast.Evaluate(s,C("WroughtInWar")).Value==0,"future sword cannot vote a new core");
                var e=new Policy().Evaluate(s,options);
                Check(e.Options.Single(x=>x.Name==options.Single(o=>o.Card?.ClassName=="WroughtInWar").Name).Score<0,"incidental forging is not marginal output");
            }
            var weak=state with{Deck=[C("StrikeRegent"),C("WroughtInWar")],Relics=["RELIC.VAJRA"]};
            Check(RegentDirection.Analyze(weak).Committed==RegentDirection.Plan.Undecided,"one strength plus transition cannot prematurely lock");
        });
        Test("real floor31 scores LunarBlast strength hits instead of isolated printed damage",()=>{
            var row=rows.First(r=>S(r).Floor==31);var s=S(row) with{Foresight=null};
            var options=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
            var e=new Policy().Evaluate(s,options);
            Console.WriteLine("0671 floor31 "+string.Join("; ",options.Select(o=>$"{o.Card?.ClassName??o.Kind}={e.Options.Single(x=>x.Key==o.Key).Score}")));
            Check(options.Single(x=>x.Key==e.RecommendedKey).Card?.ClassName=="LunarBlast","existing cheap skills and strength support multi hit outlet");
            Check(e.Options.Single(x=>x.Key==options.Single(o=>o.Card?.ClassName=="CrushUnder").Key).Score<0,"redundant upgraded filler must also lose to skip, not only to LunarBlast");
        });
        Test("book compares native legal targets; Volley sharp changes with payable X",()=>{
            using var book=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-0671-book.json")));
            var s=book.RootElement.GetProperty("decision").GetProperty("state").Deserialize<RunSnapshot>(json)!;
            var keys=new[]{"READ_THE_BACK","READ_PASSAGE","READ_ENTIRE_BOOK"};
            EventChoice[] Choices(RunSnapshot x)=>keys.Select(k=>EventRules.Describe("SELF_HELP_BOOK",k,k,x,new Dictionary<string,double>())).ToArray();
            var choices=Choices(s);var e=EventPolicy.Evaluate(s,choices);
            Check(e.RecommendedKey=="READ_THE_BACK","real multi hit deck prefers Sharp on Volley");
            Console.WriteLine("0671 book "+string.Join("; ",choices.Select(x=>$"{x.Key}={x.Benefit:0.000} ({string.Join(' ',x.Notes??[])})")));
            Check(choices[0].Notes!.Any(n=>n.Contains(s.Deck.Single(c=>c.ClassName=="Volley").Name)),"name recommended Volley target");
            Check(choices[0].Benefit>Choices(s with{Energy=1})[0].Benefit,"X hits require available energy");
            var skill=s with{Deck=[C("MasterOfStrategy"),C("StrikeRegent")]};
            Check(Choices(skill)[2].Benefit==0,"book Swift cannot use a skill even if its draw score is higher");
            var lack=s with{Deck=[C("BlackHole"),C("StrikeRegent")]};
            var rich=lack with{Deck=[..lack.Deck,..Enumerable.Repeat(C("MasterOfStrategy"),6)]};
            Check(Choices(lack)[2].Benefit>Choices(rich)[2].Benefit,"Swift gains value when draw is scarce");
            Check(EventPolicy.Evaluate(lack,Choices(lack)).RecommendedKey=="READ_ENTIRE_BOOK","draw-starved single-hit deck prefers Swift");
            Check(new StrategicValue(s).EnchantTargets("SHARP",2,"Attack")[0].Card.ClassName=="Volley","preview and actual selector use same scorer");
        });
        Test("shared transition cost covers all five roles and preserves early/new coverage",()=>{
            var cases=new[]{("Ironclad","SwordBoomerang","Thunderclap"),("Silent","Dash","SuckerPunch"),("Defect","Sunder","BallLightning"),("Regent","Volley","WroughtInWar"),("Necrobinder","BlightStrike","Reap")};
            foreach(var (hero,output,filler) in cases){
                // Construct a sufficiently grown output to exercise a mature deck,
                // rather than claiming these numbers are an unmodified native card.
                var c=C(filler);var core=C(output) with{Variables=new(C(output).Variables){["Damage"]=70}};
                var s=new RunSnapshot{Character=hero,Act=3,Hp=50,MaxHp=80,Energy=3,Relics=["RELIC.SWORD_OF_JADE"],
                    Deck=[core,core,c,C("MasterOfStrategy"),C("SecretTechnique"),C("SecretWeapon"),C("Panache")]};
                if(hero=="Silent")s=s with{Deck=[..s.Deck,C("Accuracy"),C("BladeDance")]};
                if(hero=="Necrobinder")s=s with{Deck=[..s.Deck,C("Countdown")]};
                // This checks selective eligibility, not a blanket card blacklist.
                Check(TransitionMargin.Assess(s with{Act=1},c)==null,hero+" early output allowed");
                Check(TransitionMargin.Assess(s,c)!=null,hero+" redundant filler is compared to owned output");
            }
            var state=S(rows[0]);var fresh=state with{Deck=state.Deck.Where(c=>!HeroPolicy.Tag(c,"aoe")).ToArray()};
            Check(TransitionMargin.Assess(fresh,C("CrushUnder"))==null,"new AoE coverage is not banned by single target core");
        });
    }
}
