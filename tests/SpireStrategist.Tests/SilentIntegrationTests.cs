using System.Text.Json;
using SpireStrategist.Core;

static class SilentIntegrationTests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action test)=>tests.Add(("Silent integration: "+name,test));
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        CardSnapshot C(string id)=>SilentPolicy.Card(id)!;
        var start=new RunSnapshot{Character="Silent",Act=1,Hp=60,MaxHp=70,Gold=300,Energy=3,Ascension=10,
            Deck=[..Enumerable.Repeat(C("StrikeSilent"),5),..Enumerable.Repeat(C("DefendSilent"),5),C("Neutralize"),C("Survivor")]};
        RunSnapshot With(params string[] ids)=>start with{Deck=[..start.Deck,..ids.Select(C)]};
        double Score(RunSnapshot s,string id)=>SilentPolicy.Evaluate(s,[new("card",id,"card",Card:C(id))]).Options[0].Score!.Value;
        Test("one reusable KnifeTrap before another expensive payoff",()=>{
            var s=With("BladeOfInk","CloakAndDagger","LeadingStrike","KnifeTrap","Backflip") with{Act=2};
            var e=SilentPolicy.Evaluate(s,[new("trap","trap","card",Card:C("KnifeTrap")),new("skip","skip","skip")]);
            Check(e.RecommendedKey=="skip","normal supported knife deck must not acquire the second trap");
            Check(Score(s,"KnifeTrap")<Score(s with{Deck=s.Deck.Where(c=>c.ClassName!="KnifeTrap").ToArray()},"KnifeTrap")-8,"distinct repeatable-finisher redundancy");
            Check(SilentStrategy.Upgrade(s,C("KnifeTrap"))>SilentStrategy.Upgrade(s,C("HiddenDaggers")),"first supported trap eats an upgrade");
            var upgraded=s with{Deck=s.Deck.Select(c=>c.ClassName=="KnifeTrap"?SilentPolicy.Card("KnifeTrap",true)!:c).ToArray()};
            foreach(var id in new[]{"HiddenDaggers","StormOfSteel"})
                Check(SilentStrategy.Upgrade(upgraded,C(id))<SilentStrategy.Upgrade(s,C(id))*.5,"only knife quality overlaps: "+id);
            foreach(var id in new[]{"BladeDance","BladeOfInk","CloakAndDagger","UpMySleeve","InfiniteBlades"})
                Check(SilentStrategy.Upgrade(upgraded,C(id))==SilentStrategy.Upgrade(s,C(id)),"keep independent knife count or Innate benefit: "+id);
        });
        Test("actual lifts and mittens support knives without pretending self exhaust cleans junk",()=>{
            var knives=With("BladeOfInk","CloakAndDagger","LeadingStrike");
            var empty=knives with{Relics=["RELIC.GIRYA"],GiryaLifts=0};var trained=empty with{GiryaLifts=3};
            Check(Score(trained,"BladeDance")>Score(empty,"BladeDance"),"spent camps count, ownership alone does not");
            Check(SilentMechanics.StrengthSupport(empty,3)==0&&SilentMechanics.StrengthSupport(empty with{GiryaLifts=null},3)==0,"old unknown counter never fabricates strength");
            Check(SilentPolicy.Analyze(trained).Immediate>SilentPolicy.Analyze(empty).Immediate,"actual strength reaches route readiness");
            Check(RelicValue.Evaluate("TOASTY_MITTENS",knives).Value>RelicValue.Evaluate("TOASTY_MITTENS",With("NoxiousFumes","Accelerant")).Value,"knife deck values both cleanup and strength");
            var once=With("Adrenaline","PiercingWail","CalculatedGamble");
            Check(!SilentMechanics.CanCleanJunk(once),"discard and self Exhaust are not targeted exhaustion");
            Check(SilentMechanics.CanCleanJunk(once with{Relics=["RELIC.TOASTY_MITTENS"]}),"actual mittens exception");
            Check(SilentMechanics.CanCleanJunk(once with{Deck=[..once.Deck,new CardSnapshot{ClassName="Purity"}]}),"actual neutral exception");
            Check(new StrategicValue(once).Remove(C("StrikeSilent"))>new StrategicValue(once with{Relics=["RELIC.TOASTY_MITTENS"]}).Remove(C("StrikeSilent")),"permanent removal matters more without cleanup");
        });
        Test("reported enchant choices use actual native attack rather than generated knives",()=>{
            var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","silent-enchant-0652.json")));
            foreach(var row in json.RootElement.EnumerateArray())
            {
                var s=row.GetProperty("state").Deserialize<RunSnapshot>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                var enchant=row.GetProperty("enchantment").GetString()!;var amount=row.GetProperty("amount").GetDouble();var v=new StrategicValue(s);
                var best=s.Deck.OrderByDescending(c=>v.Enchant(c,enchant,amount)).First();
                Check(best.ClassName==row.GetProperty("expected").GetString(),enchant+" should prefer "+row.GetProperty("expected")+", got "+best.ClassName);
                Check(v.BestEnchant(enchant,amount)==v.Enchant(best,enchant,amount),"event and Ancient preview match selected target");
            }
            var k=With("BladeOfInk","KnifeTrap");var value=new StrategicValue(k);
            Check(EnchantmentValue.Hits(C("LeadingStrike"))==1,"generated knives never inherit parent Sharp");
            Check(value.Enchant(C("Adrenaline"),"IMBUED")>value.Enchant(C("KnifeTrap"),"IMBUED"),"empty opening exhaust pile cannot enable automated KnifeTrap");
            Check(value.Enchant(C("Backflip"),"SWIFT",2)>value.Enchant(C("DefendSilent"),"SWIFT",2),"useful reusable support before soon-removed basic");
        });
        RewardForesight Future(string room,CardSnapshot[] cards,int[]? prices=null,bool valid=true)=>new("copy",valid,[new("one",[new("next",room,1,1,cards,prices)])],"test");
        Test("foresight changes card investment but never current readiness",()=>{
            var future=start with{Foresight=Future("Monster",[C("BladeOfInk")])};
            var before=JsonSerializer.Serialize(future);
            Check(Score(future,"Accuracy")>Score(start,"Accuracy")+4,"confirmed conditional producer changes payoff investment");
            Check(DeckReadiness.Read(start)==DeckReadiness.Read(future),"no borrowed future combat strength");
            Check(before==JsonSerializer.Serialize(future),"pure scoring leaves all inputs unchanged");
        });
        Test("foresight respects validity shop budget multiplayer and commitment",()=>{
            var bad=start with{Foresight=Future("Shop",[C("BladeOfInk")],[500])};
            Check(SilentForecast.Evaluate(bad,C("Accuracy")).Value==0,"unaffordable shop cannot enable core");
            Check(SilentForecast.Evaluate(start with{Foresight=Future("Shop",[C("BladeOfInk")],[100])},C("Accuracy")).Value>0,"affordable shop matters");
            Check(SilentForecast.Evaluate(start with{Foresight=Future("Monster",[C("BladeOfInk")],valid:false)},C("Accuracy")).Value==0,"invalid snapshot abstains");
            Check(SilentForecast.Evaluate(start with{PlayerCount=2,Foresight=Future("Monster",[C("BladeOfInk")])},C("Accuracy")).Value==0,"no multiplayer hidden forecast");
            Check(SilentForecast.Evaluate(With("NoxiousFumes","Accelerant") with{Foresight=Future("Monster",[C("BladeOfInk")])},C("Accuracy")).Value==0,"no forecast-driven incompatible second package");
        });
        Test("future alternatives on the same page never combine",()=>{
            var a=start with{Foresight=Future("Monster",[C("BladeOfInk"),C("BladeDance")])};
            var b=start with{Foresight=Future("Monster",[C("BladeOfInk")])};
            Check(SilentForecast.Evaluate(a,C("Accuracy")).Value==SilentForecast.Evaluate(b,C("Accuracy")).Value,"one reward means one pick");
        });
        Test("all native hunter relics and Ancient replacements use hunter mechanisms",()=>{
            foreach(var id in new[]{"RING_OF_THE_SNAKE","HELICAL_DART","NINJA_SCROLL","PAPER_KRANE","SNECKO_SKULL","TINGSHA","TOUGH_BANDAGES","TWISTED_FUNNEL","TOUCH_OF_OROBAS","DUSTY_TOME","ARCHAIC_TOOTH"})
                Check(SilentRelics.Evaluate(id,start) is {Supported:true},id);
            Check(RelicValue.Evaluate("TOUGH_BANDAGES",With("Acrobatics","CalculatedGamble")).Value>RelicValue.Evaluate("TOUGH_BANDAGES",start).Value,"actual discard synergy");
            Check(RelicValue.Evaluate("SNECKO_SKULL",With("NoxiousFumes","BouncingFlask")).Value>RelicValue.Evaluate("SNECKO_SKULL",start).Value,"actual poison synergy");
            Check(RelicValue.Evaluate("UNKNOWN",start,true).Supported==false,"unknown ancient not invented");
        });
        Test("remove protects core not a hypothetical extra copy",()=>{
            var s=With("BladeOfInk","Accuracy","KnifeTrap");var v=new StrategicValue(s);
            Check(v.Remove(C("StrikeSilent"))>v.Remove(C("Accuracy"))+10,"basic before output core");
            Check(BridgePolicy.Removal(s,C("Accuracy"))<0,"bridge protects core");
            Check(NeowPolicy.RemovalPlan(start,start.Deck,2).Select(i=>start.Deck[i].ClassName).SequenceEqual(new[]{"StrikeSilent","DefendSilent"}),"correct character pair");
        });
        Test("event decisions compare removal upgrades and reject lethal costs",()=>{
            var e=EventPolicy.Evaluate(With("Accuracy","BladeOfInk"),[new("remove","remove basic",Remove:1),new("basic","weak upgrade",Benefit:2),new("death","death",HpLoss:70,Benefit:100)]);
            Check(e.RecommendedKey=="remove","use hunter removal values");
            Check(e.Options.Single(x=>x.Key=="death").Score< -9999,"never pay lethal blood");
        });
        Test("shops buy enabled knife core before more filler",()=>{
            var s=With("BladeOfInk","CloakAndDagger","LeadingStrike");
            var shop=ShopPolicy.Evaluate(s,[new("trap",C("KnifeTrap"),90),new("slice",C("Slice"),50)],150,300,0);
            Check(shop.Recommendation?.Key=="trap","output payoff wins affordable shop comparison");
        });
        Test("Neow transformations value actual results rather than gold rarity alone",()=>{
            Check(NeowPolicy.TransformScore(start,[C("Adrenaline")],false)>NeowPolicy.TransformScore(start,[C("StormOfSteel")],false),"different rare cards need different acquisition value");
            var lowMax=start with{Hp=32,MaxHp=40};
            Check(NeowPolicy.TransformScore(lowMax,[C("Adrenaline"),C("Backflip")],true)<NeowPolicy.TransformScore(lowMax,[C("Adrenaline"),C("Backflip")],false),"shared low maximum HP safeguard");
        });
        Test("shared Neow priorities preserve a known knife core opening",()=>{
            var s=start with{Hp=56,Gold=99,Deck=[..start.Deck,new CardSnapshot{ClassName="AscendersBane",Type="Curse",Cost=-1,Keywords=["Eternal","Ethereal","Unplayable"]}]};
            var score=NeowPolicy.ScrollScore(s,C("KnifeTrap"));
            var e=EventPolicy.Evaluate(s,[new("lava","熔岩石",Benefit:NeowPolicy.Evaluate("LAVA_ROCK",s).Value),
                new("scroll","奥术卷轴",Benefit:score),new("sacrifice","涅奥的牺牲",Benefit:20)]);
            Check(e.RecommendedKey=="scroll","known KnifeTrap must beat lava and the middle-tier alternative in the reported opening");
            var acquired=s with{Deck=[..s.Deck,C("KnifeTrap")]};
            Check(SilentPolicy.Analyze(acquired).PrimaryDamage=="小刀","acquired rare payoff establishes knife investment direction");
            Check(SilentPolicy.Analyze(acquired).DamageEngines==0,"a payoff without knives is not already a working engine");
            Check(Score(acquired,"BladeOfInk")>Score(s,"BladeOfInk"),"next rewards seek actual knife production");
        });
        Test("all supported characters share the user ranked Neow tiers",()=>{
            foreach(var character in new[]{"Ironclad","Defect","Silent"})
            {
                CardSnapshot Starter(string kind)=>character=="Ironclad"?CardRegistry.All[kind+character].Basic:
                    character=="Defect"?DefectPolicy.Card(kind+character)!:C(kind+character);
                var strike=Starter("Strike");var defend=Starter("Defend");
                CardSnapshot[] signature=character=="Ironclad"?[CardRegistry.All["Bash"].Basic]:
                    character=="Defect"?[DefectPolicy.Card("Zap")!,DefectPolicy.Card("Dualcast")!]:[C("Neutralize"),C("Survivor")];
                var s=start with{Character=character,Hp=70,MaxHp=70,Deck=[..Enumerable.Repeat(strike,5),..Enumerable.Repeat(defend,5),..signature]};
                double N(string id)=>NeowPolicy.Evaluate(id,s).Value;
                Check(N("PRECISE_SCISSORS")==32&&N("PRECARIOUS_SHEARS")==32.5,character+" shared removal tier");
                Check(N("SILVER_CRUCIBLE")==28&&N("WINGED_BOOTS")==26,character+" shared second tier");
                Check(N("NEW_LEAF")==24&&N("LEAFY_POULTICE")==24&&N("ARCANE_SCROLL")==22,character+" shared middle tiers");
                foreach(var id in new[]{"NUTRITIOUS_OYSTER","BOOMING_CONCH","FISHING_ROD","DOWSING_ROD","LAVA_ROCK","NEOWS_TALISMAN","POMANDER"})
                    Check(N(id)<=3,character+" low tier "+id);
                Check(NeowPolicy.TransformScore(s,[C("Accuracy")],false)>=31&&NeowPolicy.TransformScore(s,[C("Adrenaline")],false)>32.5,character+" shared transformation promotion");
                Check(NeowPolicy.RemovalPlan(s,s.Deck,2).Select(i=>s.Deck[i].ClassName).SequenceEqual(new[]{"Strike"+character,"Defend"+character}),character+" own starter pair");
                Check(NeowPolicy.Evaluate("KALEIDOSCOPE",s).Supported==false,character+" foreign reward stays excluded");
            }
        });
        Test("camps use hunter upgrades and emergency recovery",()=>{
            var s=With("Accuracy","BladeOfInk");
            Check(CardMechanics.Upgrade(s,C("Accuracy"))>CardMechanics.Upgrade(s,C("StrikeSilent")),"core upgrade priority");
            var low=SilentPolicy.Evaluate(s with{Hp=5},[new("heal","heal","heal",HealAmount:21),new("smith","smith","smith")]);
            Check(low.RecommendedKey=="heal","critical HP is not blindly spent on upgrades");
        });
        Test("known enchants and neutral support remain usable",()=>{
            var c=C("BladeOfInk") with{Modified=true,Enchantment="ENCHANTMENT.SWIFT",EnchantmentAmount=2};
            Check(SilentPolicy.Evaluate(start,[new("c",c.Name,"card",Card:c)]).RecommendedKey=="c","known Swift no stall");
            var neutral=new CardSnapshot{ClassName="MasterOfStrategy",Name="谋略",Type="Skill",Rarity="Rare",Cost=0};
            Check(SilentPolicy.Evaluate(start,[new("n",neutral.Name,"card",Card:neutral)]).Options[0].Supported,"neutral uses hunter features");
        });
        Test("English presentation covers hunter scoring and mechanism notes",()=>{
            var han=new System.Text.RegularExpressions.Regex("[\\u3400-\\u9fff]");
            var missing=new HashSet<string>();
            foreach(var s in new[]{start,With("BladeOfInk","Accuracy","KnifeTrap"),With("NoxiousFumes","Accelerant","Backflip"),With("Acrobatics","Reflex","Tactician","CalculatedGamble")})
            {
                var e=SilentPolicy.Evaluate(s,SilentPolicy.Profiles.Keys.Select(id=>new DecisionOption(id,id,"card",Card:C(id))).ToArray());
                foreach(var text in e.Warnings.Concat(e.Options.SelectMany(o=>o.Parts.Select(p=>p.Reason).Concat(o.Notes))))
                    if(han.IsMatch(Localization.English(text)))missing.Add(text);
            }
            Check(missing.Count==0,"Missing English: "+string.Join(" | ",missing));
        });
    }
}
