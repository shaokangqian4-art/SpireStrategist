using SpireStrategist.Core;
using System.Text.Json;

static class Review0672Tests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action f)=>tests.Add(("0672: "+name,f));
        void Check(bool b,string why){if(!b)throw new Exception(why);}
        CardSnapshot C(string n)=>UpgradeReview.Native(n,false)!;
        RunSnapshot Live(string name)=>JsonSerializer.Deserialize<RunSnapshot>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-generation-"+name+".json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        Test("actual colorless investment is protected before a dedicated payoff",()=>{
            var r=Live("live-17");
            Check(RegentDirection.Analyze(r).Committed==RegentDirection.Plan.Generation,"Spectrum and Manifest constitute invested direction");
            var opts=new[]{"HeirloomHammer","BundleOfJoy","TheSmith"}.Select(n=>new DecisionOption(n,n,"card",Card:C(n))).Append(new("skip","skip","skip")).ToArray();
            var e=new Policy().Evaluate(r,opts);var smith=e.Options.Single(o=>o.Key=="TheSmith");
            Check(e.RecommendedKey!="TheSmith"&&smith.Score<0,"do not divert into sword");
            Check(!smith.Parts.Any(p=>p.Reason.Contains("接通现有组件")),"no fictitious existing component bonus");
            var empty=r with{Deck=[C("StrikeRegent"),C("Venerate")],Foresight=null};
            Check(RegentDirection.Analyze(empty with{Deck=[..empty.Deck,C("ManifestAuthority")]}).Committed==RegentDirection.Plan.Undecided,"one utility generator doesn't lock a plan");
            var alone=new Policy().Evaluate(empty,opts).Options.Single(o=>o.Key=="TheSmith");
            Check(!alone.Parts.Any(p=>p.Reason.Contains("接通现有组件")),"candidate cannot count as its own existing support");
            Check(RegentDirection.ConnectsOwned(empty with{Deck=[C("Furnace"),C("SummonForth")]},C("TheSmith"),RegentDirection.Analyze(empty)),"real sword support remains eligible");
        });
        Test("actual shop Stardust uses spare stars, not the same stars twice",()=>{
            var r=Live("live-28");var card=C("Stardust");
            Check(RegentDirection.Analyze(r).Committed==RegentDirection.Plan.Generation,"actual later deck remains generation");
            var score=new StrategicValue(r).Add(card);
            var result=ShopPolicy.Evaluate(r,[new("stardust",card,77)],null,r.Gold);
            Check(result.Recommendation?.Key!="stardust","cannot buy star finisher from overstated resource budget");
            var rich=r with{Deck=[..r.Deck,C("Genesis"),C("Genesis"),C("TheSealedThrone")]};
            Check(new StrategicValue(rich).Add(card)>score,"genuine extra star production can support a bridge");
        });
        var s=new RunSnapshot{Character="Regent",Hp=55,MaxHp=75,Act=2,Floor=24,Energy=3,
            Relics=["DIVINE_RIGHT"],Deck=[C("ManifestAuthority"),C("Quasar"),C("Orbit"),C("Tyranny"),C("WroughtInWar"),C("Venerate"),C("HiddenCache"),C("StrikeRegent"),C("DefendRegent")]};
        Test("native generated-card and energy upgrades beat flat transition stats",()=>{
            var filler=CardMechanics.Upgrade(s,C("WroughtInWar"));
            foreach(var name in new[]{"ManifestAuthority","Quasar","Orbit","Tyranny"})
                Check(CardMechanics.Upgrade(s,C(name))>filler+5,name+" mechanism matters beyond DynamicVars");
            Check(CardMechanics.Upgrade(s,C("Quasar"))>CardMechanics.Upgrade(s with{Deck=[C("Quasar")],Relics=[]},C("Quasar")),"star affordability remains meaningful");
            Check(CardMechanics.Upgrade(s with{Deck=[..s.Deck,new CardSnapshot{ClassName="Apotheosis",CanUpgrade=true}]},C("Orbit"))==0,"Apotheosis still suppresses other smith targets");
            Check(CardMechanics.Upgrade(s,C("PhotonCut"))>CardMechanics.Upgrade(s,C("WroughtInWar")),"draw upgrade is not flattened just because common");
        });
        Test("cross-character pure transition damage versus meaningful engine upgrades",()=>{
            foreach(var pair in new[]{("Ironclad","TwinStrike","DarkEmbrace"),("Silent","Slice","WellLaidPlans"),("Defect","SweepingBeam","Coolheaded"),("Regent","WroughtInWar","ManifestAuthority"),("Necrobinder","StrikeNecrobinder","Demesne")})
            {
                var state=s with{Character=pair.Item1,Act=3,Deck=[C(pair.Item2),C(pair.Item3)]};
                Check(CardMechanics.Upgrade(state,C(pair.Item3))>CardMechanics.Upgrade(state,C(pair.Item2)),pair.Item1+" engine beats transition");
            }
            Check(CardMechanics.Upgrade(s with{Character="Defect"},C("Coolheaded"))==16,"preserve reviewed draw upgrade");
        });
        Test("healthy humidifier does not manufacture healing need; real low HP still rests",()=>{
            DecisionOption[] Options(RunSnapshot r)=>[new("heal","rest","heal",HealAmount:r.MaxHp*.3,EffectValue:CampAndMushroomPolicy.HumidifierValue(r,5)),new("smith","smith","smith")];
            var healthy=s with{Hp=55,MaxHp=80,Act=1,Deck=[C("Orbit"),C("StrikeRegent"),C("DefendRegent")]};
            var low=healthy with{Hp=12};
            Check(new Policy().Evaluate(healthy,Options(healthy)).RecommendedKey=="smith","55/80 with Orbit must smith");
            Check(new Policy().Evaluate(low,Options(low)).RecommendedKey=="heal","12/80 still rests");
            Check(CampAndMushroomPolicy.HumidifierValue(low,5)>CampAndMushroomPolicy.HumidifierValue(healthy,5)*4,"healthy bonus discounted, low HP bonus kept");
        });
        Test("mushroom requires protected opening AND dangerous maximum, keeps lethal escape",()=>{
            Evaluation E(RunSnapshot r)=>EventPolicy.Evaluate(r,new[]{"BIG_MUSHROOM","FRAGRANT_MUSHROOM"}.Select(k=>EventRules.Describe("HUNGRY_FOR_MUSHROOMS",k,k,r,new Dictionary<string,double>())).ToArray());
            var good=s with{Act=3,Hp=73,MaxHp=75};
            foreach(var r in new[]{good,good with{Character="Silent"},good with{Relics=["BAG_OF_PREPARATION"]},good with{Hp=30,MaxHp=40}})
                Check(E(r).RecommendedKey=="FRAGRANT_MUSHROOM","normal or unprotected maximum should prefer upgrade option");
            foreach(var r in new[]{good with{Character="Silent",Hp=35,MaxHp=40},good with{Hp=35,MaxHp=40,Relics=["RELIC.BAG_OF_PREPARATION"]}})
                Check(E(r).RecommendedKey=="BIG_MUSHROOM","dangerous maximum plus opening compensation allows large mushroom");
            Check(E(good with{Hp=10}).RecommendedKey=="BIG_MUSHROOM","do not take lethal 15 damage");
        });
        Test("five rings precedes an affordable desired card, never steals its budget",()=>{
            var r=s with{Gold=400,Act=1,Deck=[C("StrikeRegent"),C("DefendRegent")]};
            var book=new ShopRelic("book","BOOK_OF_FIVE_RINGS","book",100);
            var card=new ShopCard("card",C("Tyranny"),100);
            var pair=PriorityShopPolicy.Evaluate(r,[book],[card]);
            Check(pair is {RelicFirst:true,FirstCard:null,TotalPrice:200}&&pair.Card?.Key=="card","book then card with full pair budget");
            Check(PriorityShopPolicy.Evaluate(r with{Gold=190},[book],[card])==null,"cannot steal budget from core");
            Check(PriorityShopPolicy.Evaluate(r with{Gold=250},[book],[card])==null,"preserve 100 for later relic spending");
            Check(PriorityShopPolicy.Evaluate(r with{Gold=250},[book],[card],relicReserve:0)?.RelicFirst==true,"last shop may use released reserve");
            Check(PriorityShopPolicy.Evaluate(r,[book],[],boughtCards:2)==null,"no extra card to farm five rings");
            Check(PriorityShopPolicy.Evaluate(r with{Relics=["RELIC.BOOK_OF_FIVE_RINGS"]},[book],[card])==null,"already owned cannot repurchase");
        });
        Test("Regent elite adjustment retains low-health safety",()=>{
            var weak=s with{Act=1,Hp=8,MaxHp=75,Deck=[C("StrikeRegent"),C("DefendRegent")]};
            var result=RoutePolicy.Evaluate(weak,[new("elite","Elite",[]),new("camp","RestSite",[])],["elite","camp"],new());
            Check(result.RecommendedNode=="camp","modest elite appetite never overrides lethal risk");
        });
    }
}
