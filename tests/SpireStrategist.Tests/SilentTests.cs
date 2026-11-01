using System.Text.Json;
using SpireStrategist.Core;

static class SilentTests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action check)=>tests.Add(("Silent draft: "+name,check));
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        CardSnapshot C(string id,bool up=false)=>SilentPolicy.Card(id,up)??throw new Exception(id);
        var start=new RunSnapshot{Character="Silent",Act=1,Hp=60,MaxHp=70,Energy=3,Ascension=10,
            Deck=[..Enumerable.Repeat(C("StrikeSilent"),5),..Enumerable.Repeat(C("DefendSilent"),5),C("Neutralize"),C("Survivor")],Relics=["RELIC.RING_OF_THE_SNAKE"]};
        RunSnapshot With(params string[] cards)=>start with{Deck=[..start.Deck,..cards.Select(x=>C(x))]};
        double Score(RunSnapshot s,string id,bool up=false)=>SilentPolicy.Evaluate(s,[new("pick",id,"card",Card:C(id,up)),new("skip","skip","skip")]).Options[0].Score!.Value;
        Test("Pyramid supports Bullet Time without valuing a redundant Retain upgrade",()=>{
            var bullet=With("BulletTime");
            var pyramid=bullet with{Relics=[..bullet.Relics,"RELIC.RUNIC_PYRAMID"]};
            Check(SilentRelics.Evaluate("RUNIC_PYRAMID",bullet)!.Value>SilentRelics.Evaluate("RUNIC_PYRAMID",start)!.Value+8,"bullet/pyramid combo");
            Check(Score(pyramid,"BulletTime")>Score(bullet,"BulletTime")+3,"reverse synergy");
            Check(SilentStrategy.Upgrade(pyramid,C("CalculatedGamble"))==0,"retain already covered");
            Check(SilentStrategy.Upgrade(bullet,C("CalculatedGamble"))>0,"retain useful without pyramid");
            Check(Score(pyramid,"CalculatedGamble",true)==Score(pyramid,"CalculatedGamble"),"no purchase premium for redundant retain");
            Check(SilentStrategy.Upgrade(pyramid,C("Adrenaline"))>0,"real energy upgrade retained");
        });
        Test("thick Silent decks can acquire multiple exhausting hand refreshes",()=>{
            var thick=With("BladeOfInk","Accuracy","KnifeTrap","CalculatedGamble") with{Act=3};
            thick=thick with{Deck=[..thick.Deck,..Enumerable.Repeat(C("LegSweep"),16)]};
            Check(Score(thick,"CalculatedGamble")>0,"second gamble in thick deck");
            Check(Score(thick with{Deck=[..thick.Deck,C("CalculatedGamble")]},"CalculatedGamble")>0,"third can still be useful");
            var saturated=thick with{Deck=[..thick.Deck,..Enumerable.Repeat(C("CalculatedGamble"),6)]};
            Check(Score(saturated,"CalculatedGamble")<0,"not unlimited refresh copies");
            Check(Score(thick,"CalculatedGamble")>Score(With("CalculatedGamble"),"CalculatedGamble"),"thickness matters");
        });
        Test("real November 1 late shiv rewards cannot pivot into forecast poison",()=>{
            var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","silent-0655-rewards.json")));
            var options=new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true};
            foreach(var row in json.RootElement.EnumerateArray()){
                var state=row.GetProperty("state").Deserialize<RunSnapshot>(options)!;
                var choices=row.GetProperty("options").Deserialize<DecisionOption[]>(options)!;
                var result=SilentPolicy.Evaluate(state,choices);
                if(state.Act==3&&state.Floor is 35 or 43){
                    foreach(var choice in choices.Where(o=>o.Card?.ClassName is "DeadlyPoison" or "Outbreak")){
                        Check(SilentPolicy.Analyze(state).PrimaryDamage=="小刀","actual sources and finisher are commitment");
                        Check(SilentForecast.Evaluate(state,choice.Card!).Value==0,"future poison cannot override current build");
                        Check(result.RecommendedKey!=choice.Key,"do not recommend pivot in actual reward");
                    }
                }
            }
        });
        Test("native coverage, multiplayer exclusions and finite scoring",()=>{
            Check(SilentPolicy.Profiles.Count==86,"86 non-multiplayer profiles");
            foreach(var id in SilentPolicy.Profiles.Keys)
                foreach(var s in new[]{start,With("BladeOfInk","Accuracy","KnifeTrap"),With("NoxiousFumes","Accelerant"),With("Acrobatics","CalculatedGamble","Reflex","Tactician","SerpentForm")})
                    foreach(var up in new[]{false,true})Check(double.IsFinite(Score(s,id,up)),id);
            foreach(var id in new[]{"BladeSymphony","Concoct","Fade","Flanking","Sneaky"}){
                var result=SilentPolicy.Evaluate(start,[new("mp",id,"card",Card:C(id)),new("skip","skip","skip")]);
                Check(result.Options[0].Score==null&&result.RecommendedKey=="skip","multiplayer must be ignored: "+id);
            }
        });
        Test("unknown cards and modifications block a false complete recommendation",()=>{
            foreach(var c in new[]{C("Adrenaline") with{ClassName="UnknownModCard"},C("Adrenaline") with{Modified=true,Enchantment="ENCHANTMENT.UNKNOWN"},C("Adrenaline") with{Modified=true}}){
                var r=SilentPolicy.Evaluate(start,[new("x","x","card",Card:c),new("skip","skip","skip")]);
                Check(r.RecommendedKey==null&&r.Options[0].Score==null,"unknown must not become zero");
            }
        });
        Test("initial strong independent support beats weak redundant transition",()=>{
            Check(Score(start,"Adrenaline")>Score(start,"Slice"),"adrenaline vs slice");
            Check(Score(start,"Afterimage")>Score(start,"DaggerThrow"),"afterimage vs dagger throw");
            Check(Score(start,"BladeOfInk")>Score(start,"SuckerPunch"),"ink supplies actual damage and weakness");
        });
        Test("Knife Trap and Accuracy respond to actual shiv production",()=>{
            var knives=With("BladeOfInk","CloakAndDagger","LeadingStrike");
            Check(Score(knives,"KnifeTrap")>Score(start,"KnifeTrap")+3,"replay needs shivs");
            Check(Score(knives,"Accuracy")>Score(start,"Accuracy")+3,"damage amplification needs shivs");
            Check(Score(knives,"KnifeTrap")>Score(knives,"DaggerThrow"),"finish an enabled damage chain");
        });
        Test("poison prerequisites cannot bootstrap themselves",()=>{
            Check(Score(start,"BubbleBubble")<0,"conditional poison is not its own seed");
            Check(Score(start,"Accelerant")<0,"no poison to amplify");
            Check(Score(With("NoxiousFumes","BouncingFlask"),"Accelerant")>Score(start,"Accelerant")+4,"real poison investment");
            Check(Score(start,"Outbreak")>0,"outbreak independently applies poison before triggering");
        });
        Test("discard support means active discard, not natural end turn flush",()=>{
            var discard=With("Acrobatics","CalculatedGamble","Prepared");
            Check(Score(discard,"Reflex")>Score(start,"Reflex")+3,"reflex actual discard gate");
            Check(Score(discard,"Tactician")>Score(start,"Tactician")+3,"tactician actual discard gate");
            Check(SilentPolicy.Analyze(With("Reflex","Tactician")).DamageEngines==0,"utility is not damage");
        });
        Test("large support deck buys a functioning damage outlet",()=>{
            var s=With("Acrobatics","Backflip","CalculatedGamble","HiddenDaggers","Reflex","Tactician","Afterimage");
            Check(Score(s,"SerpentForm")>Score(s,"Backflip"),"serpent over second backflip with engine present");
            Check(SilentPolicy.Analyze(s).DamageEngines==0,"auxiliary engine alone not a win plan");
            Check(SilentPolicy.Analyze(s with{Deck=[..s.Deck,C("SerpentForm")]}).DamageEngines>0,"engine now has a real output");
        });
        Test("Envenom does not turn knives into permission to build a poison package",()=>{
            var s=With("BladeOfInk","LeadingStrike","Accuracy");
            var bridge=s with{Deck=[..s.Deck,C("Envenom")]};
            Check(Score(s,"Envenom")>Score(start,"Envenom"),"an individual hit-trigger benefit remains real");
            Check(SilentPolicy.Analyze(bridge).PrimaryDamage=="小刀","bridge is not poison direction evidence");
            Check(Score(bridge,"KnifeTrap")>Score(bridge,"Accelerant"),"complete main output rather than expanding incidental poison");
        });
        Test("committed poison prefers poison output and shared support over new knives",()=>{
            var s=With("NoxiousFumes","BouncingFlask","Accelerant","Backflip","LegSweep") with{Act=2};
            Check(SilentPolicy.Analyze(s).PrimaryDamage=="毒","real poison investment");
            Check(Score(s,"Outbreak")>Score(s,"FanOfKnives"),"do not launch a second knife package");
            Check(Score(s,"Adrenaline")>0,"shared support is not rejected");
            Check(SilentPolicy.Analyze(With("CloakAndDagger")).PrimaryDamage=="未确定","one defensive transition is not a permanent commitment");
        });
        Test("late support-heavy deck cannot mistake more thorns for an output plan",()=>{
            var s=With("DaggerThrow","DaggerThrow","Backflip","Backflip","Backflip","Acrobatics","Acrobatics","Footwork","Deflect","Prepared","Prepared","ToolsOfTheTrade","Afterimage") with{Act=3};
            Check(Score(s,"SerpentForm")>Score(s,"Abrasive"),"usable repeat-play damage over more defense when defense already present");
        });
        Test("Grand Finale requires plausible draw control even as a rare",()=>{
            Check(Score(start,"GrandFinale")<0,"cannot play without empty draw pile");
            var tuned=start with{Deck=[C("Acrobatics",true),C("CalculatedGamble",true),C("Reflex",true),C("Tactician",true),C("WellLaidPlans",true),C("Backflip"),C("Survivor"),C("DefendSilent")]};
            Check(Score(tuned,"GrandFinale")>Score(start,"GrandFinale")+5,"credible controlled draw can unlock finale");
        });
        Test("self-contained Fan of Knives is not blocked by no existing shivs",()=>{
            Check(Score(start,"FanOfKnives")>0,"it supplies its own four shivs");
        });
        Test("duplicates and support saturation remain meaningful",()=>{
            foreach(var id in new[]{"Afterimage","WellLaidPlans","MasterPlanner","Slice","Backflip","KnifeTrap"})
                Check(Score(With(id,id),id)<Score(start,id),"third copy: "+id);
            Check(Score(start,"StrikeSilent")<0&&Score(start,"DefendSilent")<0,"do not acquire more basics");
        });
        Test("upgrades describe real current-version effects",()=>{
            Check(C("BladeDance").Keywords.Contains("Exhaust"),"blade dance exhausts in current version");
            Check(C("Reflex").Cost==3&&C("Reflex").Keywords.Contains("Sly"),"current reflex");
            Check(C("WellLaidPlans").Cost==2&&C("WellLaidPlans").Rarity=="Rare","current WLP");
            Check(C("CalculatedGamble",true).Keywords.Contains("Exhaust")&&C("CalculatedGamble",true).Keywords.Contains("Retain"),"current gamble upgrade adds retain and still exhausts");
            Check(Score(With("Reflex","Tactician"),"CalculatedGamble",true)>Score(With("Reflex","Tactician"),"CalculatedGamble"),"upgrade valuable to a real engine");
        });
        Test("model never uses future rewards as owned combat strength",()=>{
            var future=start with{Foresight=new("x",true,[new("p",[new("n","Monster",1,1,[C("BladeOfInk"),C("Accuracy")])])],"conditional")};
            Check(DeckReadiness.Read(future)==DeckReadiness.Read(start),"future acquisition is not current combat strength");
        });
        Test("hunter dispatch supports shared actions without changing other character dispatch",()=>{
            var o=new[]{new DecisionOption("card","Adrenaline","card",Card:C("Adrenaline"))};
            Check(new Policy().Evaluate(start,o).RecommendedKey=="card","hunter live dispatch enabled with adapters");
            Check(SilentPolicy.Evaluate(start with{Character="Defect"},o).RecommendedKey==null,"no cross-character scoring");
            Check(SilentPolicy.Evaluate(start,[new("smith","smith","smith")]).RecommendedKey=="smith","hunter camp adapter enabled");
        });
    }
}
