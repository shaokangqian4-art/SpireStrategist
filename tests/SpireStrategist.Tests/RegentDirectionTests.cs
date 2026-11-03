using SpireStrategist.Core;

static class RegentDirectionTests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action check)=>tests.Add(("Regent 0656: "+name,check));
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        CardSnapshot C(string id,bool up=false)=>HeroPolicy.Card(id,up)??throw new Exception(id);
        RunSnapshot S(params string[] cards)=>new(){Character="Regent",Act=3,Hp=60,MaxHp=75,Ascension=10,Gold=200,
            Relics=["RELIC.DIVINE_RIGHT"],Deck=[..Enumerable.Repeat(C("StrikeRegent"),4),..Enumerable.Repeat(C("DefendRegent"),4),C("Venerate"),C("FallingStar"),..cards.Select(x=>C(x))]};
        RunSnapshot Stars()=>S("Genesis","Glow","Comet","SevenStars","Stardust","Glimmer","GatherLight");
        RunSnapshot Sword()=>S("Furnace","SwordSage","SpoilsOfBattle","Bulwark","RefineBlade","SummonForth");
        OptionScore Score(RunSnapshot s,string id,bool up=false)=>new Policy().Evaluate(s,[new("pick",id,"card",Card:C(id,up)),new("skip","skip","skip")]).Options[0];
        RewardForesight Future(params string[][] pages)=>new("constructed",true,[new("path",pages.Select((ids,i)=>new FutureReward(i.ToString(),"Monster",i+1,1,ids.Select(x=>C(x)).ToArray())).ToArray())],"constructed regression, not natural gameplay");

        Test("output commitment excludes supply and incidental forging",()=>{
            var supply=S("Genesis","Glow","GatherLight","HiddenCache","Alignment","SolarStrike");
            Check(RegentDirection.Analyze(supply).Committed==RegentDirection.Plan.Undecided,"resources alone do not select Stars");
            Check(HeroPolicy.Analyze(supply).Primary=="未确定","supply is not a damage plan");
            var stars=Stars();var bridge=stars with{Deck=[..stars.Deck,C("BigBang"),C("Bulwark"),C("SpoilsOfBattle")]};
            Check(RegentDirection.Analyze(bridge).Committed==RegentDirection.Plan.Stars&&HeroPolicy.Analyze(bridge).Primary=="星星","incidental forge must not override actual output");
            Check(RegentDirection.Analyze(S("AstralPulse","CrescentSpear","Devastate")).Committed==RegentDirection.Plan.Stars,"ordinary star attacks also establish a direction");
            Check(RegentDirection.Analyze(S("GuidingStar","DyingStar","GammaBlast","MeteorShower")).Committed==RegentDirection.Plan.Undecided,"utility attacks alone are not a hard pivot");
        });
        Test("near-formed Stars rejects even upgraded sword cores before a complete engine",()=>{
            var s=S("Comet","SevenStars","Stardust");
            Check(HeroPolicy.Analyze(s).DamageEngines==0,"fixture intentionally lacks sustainable supply");
            foreach(var act in new[]{1,2,3})foreach(var id in new[]{"SummonForth","SwordSage","SeekingEdge","Conqueror","Furnace","TheSmith","Parry"})foreach(var up in new[]{false,true})
            {
                var state=s with{Act=act};var result=new Policy().Evaluate(state,[new("c",id,"card",Card:C(id,up)),new("skip","skip","skip")]);
                Check(result.RecommendedKey=="skip",$"act {act} {id} upgraded={up}: output investment should seek supply, not a sword pivot");
            }
            Check(Score(s,"Genesis").Score>0&&Score(s,"Glow").Score>0,"fill missing star supply instead of forbidding unfinished decks");
        });
        Test("sword cores meet distinct missing roles and do not stack without limit",()=>{
            var s=S("Furnace","TheSmith","SpoilsOfBattle","Bulwark","RefineBlade");
            foreach(var id in new[]{"SummonForth","SwordSage","SeekingEdge","Conqueror"})
            {
                Check(Score(s,id).Score>Score(S(),id).Score+2,id+" should respond to actual forging");
                Check(Score(s,id).Score>Score(s,"Glitterstream").Score,id+" should beat redundant defense");
                Check(Score(s with{Deck=[..s.Deck,C(id)]},id).Score<Score(s,id).Score,id+" duplicate payoff must decline");
            }
            var reliable=s with{Deck=[..s.Deck,C("SummonForth")]};
            Check(Score(reliable,"Conqueror").Score>Score(s,"Conqueror").Score,"same-turn double damage values actual sword access");
        });
        Test("shared support remains usable in both plans",()=>{
            var s=Sword();var support=s with{Deck=[..s.Deck,C("Genesis"),C("Glow"),C("GatherLight"),C("Alignment")]};
            Check(RegentDirection.Analyze(support).Committed==RegentDirection.Plan.Sword&&HeroPolicy.Analyze(support).Primary=="铸剑","star support must not change sword intent");
            foreach(var id in new[]{"Glow","Alignment","BigBang","DyingStar","GuidingStar","GammaBlast","MeteorShower","HeirloomHammer"})
                Check(!RegentDirection.OffPlan(RegentDirection.Analyze(s),C(id)),id+" utility should not be blanket-banned");
            foreach(var id in new[]{"BigBang","SpoilsOfBattle","Bulwark","HeirloomHammer"})
            {
                var score=Score(Stars(),id);
                Check(!RegentDirection.OffPlan(RegentDirection.Analyze(Stars()),C(id))&&score.Score>0,id+" standalone draw/block/copy remains useful");
            }
            foreach(var id in new[]{"Comet","SevenStars","BlackHole","Radiate"})
                Check(Score(s,id).Score<0,id+" should not start a second output line");
        });
        Test("foresight cannot recruit an opposing candidate or opposing future page",()=>{
            var stars=Stars();var sight=stars with{Foresight=Future(["Furnace","TheSmith"],["SummonForth","SwordSage"],["SeekingEdge","Conqueror"])};
            foreach(var id in new[]{"SummonForth","SwordSage","SeekingEdge","Conqueror","Furnace","TheSmith"})
            {
                Check(HeroForecast.Evaluate(sight,C(id)).Value==0,id+" current off-plan candidate");
                Check(Score(sight,id).Score==Score(stars,id).Score,id+" must not receive a forecast reward");
            }
            foreach(var id in new[]{"BigBang","SpoilsOfBattle","Bulwark","Glow"})
                Check(HeroForecast.Evaluate(sight,C(id)).Value==0,id+" bridge cannot smuggle in future sword cores");
            var sword=Sword();var opposing=sword with{Foresight=Future(["Genesis"],["BlackHole","Comet"],["SevenStars","Stardust"])};
            Check(HeroForecast.Evaluate(opposing,C("SevenStars")).Value==0,"sword plan also rejects off-plan candidate");
            Check(HeroStrategy.Readiness(sight)==HeroStrategy.Readiness(stars),"forecast never supplies current battle strength");
        });
        Test("early investment, genuinely mixed decks and compatible forecasts remain open",()=>{
            var early=S() with{Act=1};
            Check(Score(early,"Furnace").Score<0&&!RegentDirection.OffPlan(RegentDirection.Analyze(early),C("SwordSage")),"isolated furnace is no longer a reason to select sword");
            var mixed=S("Genesis","Comet","SevenStars","Furnace","SummonForth","SwordSage");
            Check(RegentDirection.Analyze(mixed).Committed==RegentDirection.Plan.Undecided,"both lines truly invested remain mixed");
            var sight=early with{Foresight=Future(["SummonForth"])};
            Check(HeroForecast.Evaluate(sight,C("SwordSage")).Value>0,"future same-line entry remains meaningful before commitment");
            Check(HeroForecast.Evaluate(sight with{MultiplayerSession=true},C("SwordSage")).Value==0,"multiplayer gate");
            Check(HeroForecast.Evaluate(sight with{Foresight=sight.Foresight! with{SourceUnchanged=false}},C("SwordSage")).Value==0,"RNG trust gate");
            var near=S("BlackHole","SevenStars","Stardust","DefendRegent","DefendRegent","DefendRegent","DefendRegent","DefendRegent");
            Check(HeroForecast.Evaluate(near with{Foresight=Future(["Glow"])},C("GatherLight")).Value>0,"compatible supply can complete a committed star plan");
        });
        Test("event and shop acquisition share direction, and explanations translate",()=>{
            var s=Stars();var sword=Sword();
            foreach(var state in new[]{s,sword})
            {
                var options=HeroPolicy.Profiles.Keys.Select(id=>new DecisionOption(id,id,"card",Card:C(id))).ToArray();
                var e=new Policy().Evaluate(state,options);
                foreach(var text in e.Warnings.Concat(e.Options.SelectMany(o=>o.Parts.Select(p=>p.Reason))))
                    Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(text),@"[\u3400-\u9fff]"),text);
            }
            foreach(var id in new[]{"SwordSage","SummonForth","SeekingEdge","Conqueror"})
                Check(new StrategicValue(s).Add(C(id))<0,id+" shared acquisition value must also reject the pivot");
            Check(HeroPolicy.Version("Regent")=="regent-draft-0.6"&&HeroPolicy.Version("Necrobinder")=="necrobinder-draft-0.3","independent role policy versions");
        });
    }
}
