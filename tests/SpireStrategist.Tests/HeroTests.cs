using SpireStrategist.Core;

static class HeroTests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action check)=>tests.Add(("Remaining heroes: "+name,check));
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        CardSnapshot C(string id,bool up=false)=>HeroPolicy.Card(id,up)??throw new Exception(id);
        RunSnapshot Start(string hero)=>new(){Character=hero,Hp=60,MaxHp=75,Act=1,Ascension=10,Gold=150,
            Deck=[..Enumerable.Repeat(C("Strike"+hero),4),..Enumerable.Repeat(C("Defend"+hero),4),C(hero=="Regent"?"Venerate":"Bodyguard"),C(hero=="Regent"?"FallingStar":"Unleash")],
            Relics=[hero=="Regent"?"RELIC.DIVINE_RIGHT":"RELIC.BOUND_PHYLACTERY"]};
        RunSnapshot With(string hero,params string[] cards)=>Start(hero) with{Deck=[..Start(hero).Deck,..cards.Select(x=>C(x))]};
        double Score(RunSnapshot s,string id)=>new Policy().Evaluate(s,[new("pick",id,"card",Card:C(id)),new("skip","skip","skip")]).Options[0].Score!.Value;
        Test("172 explicit profiles, base/upgraded finite and native multiplayer excluded",()=>{
            Check(HeroPolicy.Profiles.Count==172,"profile count");
            foreach(var hero in new[]{"Regent","Necrobinder"})
            {
                var start=Start(hero);Check(new ClimbPreferences{Character=hero}.Supported,"enabled character");
                foreach(var id in HeroPolicy.Profiles.Keys)
                    foreach(var up in new[]{false,true})
                    {
                        var e=new Policy().Evaluate(start,[new("c",id,"card",Card:C(id,up))]);
                        Check(e.Options[0].Score is {} x&&double.IsFinite(x),id);Check(CardCatalog.Find(C(id,up))!=null,id+" catalog");
                    }
            }
            foreach(var id in new[]{"Constellation","HammerTime","Largesse","Plot","Tutor","Cacophony","GlimpseBeyond","LegionOfBone","Soulbound","Underworld"})
            {
                var e=new Policy().Evaluate(Start("Regent"),[new("c",id,"card",Card:C(id) with{MultiplayerOnly=false}),new("skip","skip","skip")]);
                Check(e.Options[0].Score==null&&e.RecommendedKey=="skip",id);
            }
        });
        Test("stars are not free energy and costs survive snapshot definitions",()=>{
            Check(C("Comet").Cost==0&&C("Comet").StarCost==5,"comet cost");
            Check(C("Stardust").CostsStarX&&C("Stardust").CostsX&&!C("HeavenlyDrill").CostsStarX,"X resources");
            var dry=Start("Regent");var supplied=With("Regent","Genesis","Glow","SolarStrike");
            Check(Score(supplied,"Comet")>Score(dry,"Comet")+2,"actual star supply improves finisher");
            Check(HeroPolicy.Analyze(dry with{Deck=[..dry.Deck,C("Comet"),C("SevenStars")]}).Missing.Any(x=>x.Contains("星消耗")),"resource debt visible");
        });
        Test("sword engine beats redundant defense and last engine cannot be deleted",()=>{
            var s=With("Regent","Furnace","SpoilsOfBattle","Bulwark");
            Check(Score(s,"SwordSage")>Score(s,"Glitterstream"),"supported sword core");
            Check(HeroPolicy.Analyze(s).DamageEngines>0,"forge is actual sword creation");
            Check(HeroStrategy.Remove(s,C("Furnace"))<HeroStrategy.Remove(s,C("StrikeRegent")),"protect engine");
            Check(HeroStrategy.Upgrade(s,C("SwordSage"))>HeroStrategy.Upgrade(s,C("DefendRegent")),"core cost upgrade");
        });
        Test("soul and doom terminals respond to sources instead of endless support",()=>{
            var soul=With("Necrobinder","CaptureSpirit","GraveWarden","Parse");
            Check(Score(soul,"Haunt")>Score(soul,"PullAggro"),"soul damage outlet");
            Check(Score(soul,"SoulStorm")>Score(Start("Necrobinder"),"SoulStorm")+3,"soul source dependency");
            var doom=With("Necrobinder","Countdown","Scourge","NegativePulse");
            Check(Score(doom,"NoEscape")>Score(doom,"Defy"),"doom scaling beats filler");
            Check(Score(Start("Necrobinder"),"TimesUp")<Score(doom,"TimesUp"),"not immediate zero-doom damage");
        });
        Test("Osty and ethereal engines are explicit and support is not damage",()=>{
            var pet=With("Necrobinder","Fetch","Poke","Spur");
            Check(Score(pet,"Calcify")>Score(pet,"DrainPower"),"pet damage scaling");
            var eth=With("Necrobinder","Parse","Defile","Defy","Fear");
            Check(Score(eth,"BansheesCry")>Score(Start("Necrobinder"),"BansheesCry")+3,"discount needs ethereal");
            Check(HeroPolicy.Analyze(With("Necrobinder","Demesne","Wisp","GraveWarden","Cleanse")).DamageEngines==0,"support not output");
            Check(HeroPolicy.Analyze(With("Regent","Genesis","Glow","Glimmer")).DamageEngines==0,"resources not output");
        });
        Test("duplicate decay, low health survival, neutral context and shared Neow",()=>{
            foreach(var hero in new[]{"Regent","Necrobinder"})
            {
                var id=hero=="Regent"?"SwordSage":"Haunt";var s=Start(hero);
                Check(Score(s with{Deck=[..s.Deck,C(id)]},id)<Score(s,id)-2,"duplicates");
                var camp=new Policy().Evaluate(s with{Hp=3},[new("heal","heal","heal",HealAmount:24),new("smith","smith","smith")]);
                Check(camp.RecommendedKey=="heal","critical healing");
                Check(NeowPolicy.Supports(s),"shared Neow gate");
                var neutral=CardRegistry.All["MasterOfStrategy"].Basic;
                Check(new StrategicValue(s).Add(neutral)>0,"role-specific colorless context");
                Check(new StrategicValue(s).Basics==8,"all-role basic removal");
            }
        });
        Test("foresight affects investment without RNG trust, budget or readiness leaks",()=>{
            var s=Start("Necrobinder");var c=C("Haunt");
            RewardForesight F(bool unchanged,int price=0)=>new("f",unchanged,[new("p",[new("1",price>0?"Shop":"Monster",1,1,[C("CaptureSpirit")],price>0?[price]:null)])],"fixture");
            var sight=s with{Foresight=F(true)};
            Check(HeroForecast.Evaluate(sight,c).Value>0,"source forecast creates a possible outlet");
            Check(Score(sight,"Haunt")>Score(s,"Haunt"),"forecast changes card score");
            Check(HeroStrategy.Readiness(sight)==HeroStrategy.Readiness(s),"future excluded from battle strength");
            Check(HeroForecast.Evaluate(s with{Foresight=F(false)},c).Value==0,"mutated source rejected");
            Check(HeroForecast.Evaluate(s with{Foresight=F(true,999)},c).Value==0,"unaffordable shop rejected");
            Check(HeroForecast.Evaluate(sight with{MultiplayerSession=true},c).Value==0,"multiplayer no foresight");
        });
        Test("unknown modifications abstain; role relics use current mechanisms",()=>{
            foreach(var hero in new[]{"Regent","Necrobinder"})
            {
                var r=new Policy().Evaluate(Start(hero),[new("c","c","card",Card:C("Haunt") with{Modified=true,Enchantment="ENCHANTMENT.NEW"}),new("s","s","skip")]);
                Check(r.RecommendedKey==null,"unknown modification abstention");
            }
            Check(RelicValue.Evaluate("BONE_FLUTE",With("Necrobinder","Poke","Fetch")).Value>RelicValue.Evaluate("BONE_FLUTE",Start("Necrobinder")).Value,"pet relic synergy");
            Check(RelicValue.Evaluate("VITRUVIAN_MINION",With("Regent","Charge")).Value>RelicValue.Evaluate("VITRUVIAN_MINION",Start("Regent")).Value,"minion relic synergy");
        });
        Test("starter resources and player Strength stay distinct from Osty",()=>{
            var regent=Start("Regent");var royal=regent with{Relics=["RELIC.DIVINE_DESTINY"]};
            Check(HeroPolicy.OpeningStars(royal)==7&&HeroPolicy.Analyze(royal).Stars==HeroPolicy.Analyze(regent).Stars,"opening Stars not renewable supply");
            Check(Score(royal,"Comet")>Score(regent,"Comet"),"upgraded starter supports real Star costs");
            var necro=With("Necrobinder","Poke","Fetch","HighFive");
            Check(HeroPolicy.Analyze(necro).Summon>HeroPolicy.Analyze(necro with{Relics=[]}).Summon,"native phylactery supply counted");
            Check(PotionPolicy.Evaluate(necro,new("StrengthPotion","Common"))!.Value==2,"player Strength is not Osty Strength");
        });
        Test("English presentation covers new priors and full evaluations",()=>{
            bool Han(string t)=>System.Text.RegularExpressions.Regex.IsMatch(t,@"[\u3400-\u9fff]");
            foreach(var p in HeroPolicy.Profiles.Values)Check(!Han(Localization.English(p.Note)),p.Note);
            foreach(var hero in new[]{"Regent","Necrobinder"})
            {
                Check(!Han(Localization.English(HeroPolicy.Scope(hero))),hero+" scope");
                var missing=HeroPolicy.Profiles.Keys.SelectMany(id=>new Policy().Evaluate(Start(hero),[new("c",id,"card",Card:C(id))]).Options[0].Parts)
                    .Select(p=>p.Reason).Distinct().Where(r=>Han(Localization.English(r))).ToArray();
                Check(missing.Length==0,string.Join("; ",missing));
            }
        });
    }
}
