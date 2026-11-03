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
        Test("real Regent loss takes opening defense over second Conqueror and Furnace",()=>{
            var json=new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true};
            using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-0668-duplicates.json")));
            foreach(var row in doc.RootElement.EnumerateArray()){
                var s=System.Text.Json.JsonSerializer.Deserialize<RunSnapshot>(row.GetProperty("state"),json)!;
                var options=System.Text.Json.JsonSerializer.Deserialize<DecisionOption[]>(row.GetProperty("options"),json)!;
                var e=new Policy().Evaluate(s,options);
                var repeat=options.Single(o=>o.Card?.ClassName==(s.Floor==21?"Conqueror":"Furnace"));
                Check(e.Options.Single(o=>o.Key==repeat.Key).Score<0,"redundant setup loses to skip at floor "+s.Floor);
                Check(e.RecommendedKey!=repeat.Key,"real snapshot recommendation changes");
            }
            using var shop=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-0668-last-shop.json")));
            var state=System.Text.Json.JsonSerializer.Deserialize<RunSnapshot>(shop.RootElement.GetProperty("state"),json)!;
            var stock=System.Text.Json.JsonSerializer.Deserialize<ShopCard[]>(shop.RootElement.GetProperty("stock"),json)!;
            var plan=ShopPolicy.Evaluate(state,stock,null,state.Gold,reserveOverride:0);
            Check(plan.Choices.Single().Score<0&&plan.Recommendation==null,"late Pyramid shop does not buy Bombardment as engine");
        });
        Test("temporary Tooth storage saturates acquisition without changing combat readiness",()=>{
            foreach(var hero in new[]{"Ironclad","Silent","Defect","Regent","Necrobinder"}){
                CardSnapshot card=hero switch{"Ironclad"=>CardRegistry.All["PommelStrike"].Basic,"Silent"=>SilentPolicy.Card("BladeDance")!,"Defect"=>DefectPolicy.Card("BallLightning")!,_=>C(hero=="Regent"?"WroughtInWar":"Poke")};
                var actual=new RunSnapshot{Character=hero,Hp=60,MaxHp=75,Act=2,Deck=Enumerable.Repeat(card,4).ToArray()};
                var held=actual with{PendingToothCards=[card,card]};
                DecisionOption[] opts=[new("c",card.Name,"card",Card:card),new("skip","skip","skip")];
                var normal=new Policy().Evaluate(actual,opts).Options[0].Score!.Value;
                var pending=new Policy().Evaluate(held,opts).Options[0].Score!.Value;
                var returned=new Policy().Evaluate(actual with{Deck=[..actual.Deck,card,card]},opts).Options[0].Score!.Value;
                Check(pending<=normal&&pending<=returned,"future redundancy "+hero);
                Check(held.Deck.Length==4,"no deck mutation "+hero);
            }
            var s=Start("Regent");var heldRegent=s with{PendingToothCards=[C("Furnace"),C("SwordSage"),C("SummonForth")]};
            Check(HeroStrategy.Readiness(s)==HeroStrategy.Readiness(heldRegent),"pending cards never grant current combat strength");
            Check(HeroPolicy.Analyze(heldRegent).SwordEngines==0,"pending output is not live output");
        });
        Test("Regent duplicate setup, pyramid congestion and opening defense",()=>{
            var s=With("Regent","SwordSage","SummonForth","Conqueror","Furnace","Parry","BlackHole","PillarOfCreation");
            var pyramid=s with{Relics=[..s.Relics,"RELIC.RUNIC_PYRAMID"]};
            Check(Score(pyramid,"Furnace")<Score(s,"Furnace"),"pyramid does not supply setup energy");
            Check(Score(pyramid,"Conqueror")<0,"redundant double damage discouraged");
            Check(Score(pyramid,"Furnace")<0,"duplicate slow setup discouraged");
            Check(Score(pyramid,"GatherLight")>Score(pyramid,"Furnace"),"opening defense beats excess setup");
            var defended=s with{Deck=[..s.Deck,C("GatherLight"),C("Glitterstream"),C("Bulwark"),C("GammaBlast")]};
            Check(RegentEconomy.Adjust(s,HeroPolicy.Analyze(s),C("GatherLight")).Sum(p=>p.Value)>RegentEconomy.Adjust(defended,HeroPolicy.Analyze(defended),C("GatherLight")).Sum(p=>p.Value),"defense need saturates");
            Check(HeroPolicy.Analyze(With("Regent","Bombardment")).DamageEngines==0,"Bombardment is not a boss engine alone");
            Check(Score(Start("Regent") with{Act=1},"Bombardment")>Score(defended with{Act=3},"Bombardment"),"early transition not blanket banned");
        });
        Test("native enchantment coverage, royal target and Kingly Punch context",()=>{
            foreach(var id in NativeEnchantments.Ids)
                Check(!CardCatalog.HasUnmodelledModification(C("KinglyPunch") with{Modified=true,Enchantment="ENCHANTMENT."+id}),id);
            Check(CardCatalog.HasUnmodelledModification(C("KinglyPunch") with{Modified=true,Enchantment="ENCHANTMENT.THIRD_PARTY"}),"unknown stays guarded");
            foreach(var hero in new[]{"Ironclad","Silent","Defect","Regent","Necrobinder"}){
                var card=hero switch{"Ironclad"=>CardRegistry.All["Offering"].Basic,"Silent"=>SilentPolicy.Card("BladeDance")!,"Defect"=>DefectPolicy.Card("MultiCast")!,_=>C(hero=="Regent"?"KinglyPunch":"Haunt")};
                var state=new RunSnapshot{Character=hero,Hp=60,MaxHp=75,Deck=[card]};
                var evaluation=new Policy().Evaluate(state,[new("c","c","card",Card:card with{Modified=true,Enchantment="ENCHANTMENT.GLAM"}),new("s","s","skip")]);
                Check(evaluation.Options[0].Supported,"glam "+hero);
                var v=new StrategicValue(state);var basic=new CardSnapshot{ClassName="Defend"+hero,Type="Skill",Rarity="Basic",Cost=1};
                if(card.Type is "Skill" or "Attack")Check(v.Enchant(card,"ROYALLY_APPROVED")>v.Enchant(basic,"ROYALLY_APPROVED"),"royal "+hero);
            }
            foreach(var card in new[]{DefectPolicy.Card("MultiCast")!,DefectPolicy.Card("Voltaic")!,C("ParticleWall"),CardRegistry.All["Offering"].Basic}){
                var hero=card.ClassName=="ParticleWall"?"Regent":card.ClassName=="Offering"?"Ironclad":"Defect";
                var v=new StrategicValue(new RunSnapshot{Character=hero,Hp=60,MaxHp=75,Deck=[card]});
                Check(v.Enchant(card,"ROYALLY_APPROVED")>0,"royal timing/startup "+card.ClassName);
                Check(v.Enchant(card with{Keywords=[..card.Keywords,"Innate","Retain"]},"ROYALLY_APPROVED")==0,"redundant royal");
            }
            var early=Start("Regent");var late=early with{Act=3,Deck=[..early.Deck,..Enumerable.Repeat(C("Parry"),14)]};
            Check(Score(early,"KinglyPunch")>Score(late,"KinglyPunch")+3,"thin early vs thick late");
        });
        Test("three Regent plans separate output while preserving support",()=>{
            var transition=With("Regent","WroughtInWar","Bulwark","SpoilsOfBattle","RefineBlade");
            Check(RegentDirection.Analyze(transition).Committed!=RegentDirection.Plan.Sword,"commons do not commit sword");
            Check(HeroPolicy.Analyze(transition).SwordEngines==0,"no phantom sword engine");
            var generation=With("Regent","Supermassive","SpectrumShift","BundleOfJoy");
            Check(RegentDirection.Analyze(generation).Committed==RegentDirection.Plan.Generation,"third plan recognized");
            foreach(var id in new[]{"Furnace","SevenStars","TheSmith"})Check(Score(generation,id)<0,id+" off plan");
            foreach(var id in new[]{"Glow","ParticleWall","Arsenal","Stardust","BigBang"})Check(!RegentDirection.OffPlan(RegentDirection.Analyze(generation),C(id)),"support/bridge "+id);
            var first=Start("Regent");
            RewardForesight F(bool valid,int price)=>new("f",valid,[new("p",[new("1","Shop",1,1,[C("SwordSage")],[price])])],"test");
            Check(!RegentDirection.SwordOpportunity(first),"no blind sword investment");
            Check(RegentDirection.SwordOpportunity(first with{Foresight=F(true,100)}),"real affordable future core");
            Check(!RegentDirection.SwordOpportunity(first with{Foresight=F(true,1000)})&&!RegentDirection.SwordOpportunity(first with{Foresight=F(false,1)}),"budget and trust gates");
            Check(!RegentDirection.SwordOpportunity(first with{Foresight=F(true,1),MultiplayerSession=true}),"multiplayer gate");
        });
        Test("Necrobinder intent excludes default pet and retains actual bridges",()=>{
            Check(NecrobinderDirection.Analyze(With("Necrobinder","SicEm"))==NecrobinderDirection.Plan.Undecided,"starting attack plus one pet card is not a plan");
            Check(!NecrobinderDirection.PetReady(With("Necrobinder","Squeeze","SicEm")),"no growth core");
            Check(!NecrobinderDirection.PetReady(With("Necrobinder","Calcify","Snap","Poke")),"ordinary pet transitions plus a buff do not lock the plan");
            Check(!NecrobinderDirection.PetReady(With("Necrobinder","NecroMastery","Rattle","Poke")),"reflection is not attack growth");
            Check(NecrobinderDirection.ReflectionReady(With("Necrobinder","NecroMastery","DevourLife","Dirge")),"soul and summon reflection bridge");
            Check(!NecrobinderDirection.ReflectionReady(With("Necrobinder","NecroMastery","Rattle","Poke")),"attacks are not summon supply");
            var doom=With("Necrobinder","Countdown","NoEscape","Scourge");
            Check(NecrobinderDirection.Analyze(doom)==NecrobinderDirection.Plan.Doom,"doom intent");
            Check(Score(doom,"Calcify")<0&&Score(doom,"Haunt")<0,"reject unrelated output engines");
            Check(!NecrobinderDirection.OffPlan(doom,C("CaptureSpirit"))&&!NecrobinderDirection.OffPlan(doom,C("Bodyguard")),"soul draw and summon defense survive");
            var soul=With("Necrobinder","Haunt","CaptureSpirit","Reave");
            Check(NecrobinderDirection.Analyze(soul)==NecrobinderDirection.Plan.Souls,"soul intent");
            Check(HeroForecast.Evaluate(doom with{Foresight=new("f",true,[new("p",[new("1","Monster",1,1,[C("Rattle"),C("SicEm")])])],"test")},C("Calcify")).Value==0,"forecast cannot pivot to pet");
        });
        Test("Borrowed Time boosts actual energy X outlets without erasing surcharge",()=>{
            var s=Start("Necrobinder");var x=With("Necrobinder","Eradicate");
            Check(Score(x,"BorrowedTime")>Score(s,"BorrowedTime")+1,"existing X outlet boosts energy");
            Check(Score(With("Necrobinder","BorrowedTime"),"Eradicate")>Score(s,"Eradicate"),"existing energy boosts X outlet");
            Check(NecrobinderDirection.BorrowedX(s,C("BorrowedTime"))==0,"no nonexistent X synergy");
        });
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
            var s=With("Regent","Furnace","SwordSage","SpoilsOfBattle","Bulwark");
            var needsCore=s with{Deck=s.Deck.Where(c=>c.ClassName!="SwordSage").ToArray()};
            Check(Score(needsCore,"SwordSage")>Score(needsCore,"Glitterstream"),"first supported sword core");
            Check(Score(s,"Glitterstream")>Score(s,"SwordSage"),"opening defense before redundant sword setup");
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
        Test("reviewed native runs prefer Doom execution and avoid false ethereal foresight",()=>{
            var json=new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true};
            using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/necrobinder-0665-review.json")));
            foreach(var row in doc.RootElement.GetProperty("cards").EnumerateArray())
            {
                var state=System.Text.Json.JsonSerializer.Deserialize<RunSnapshot>(row.GetProperty("state"),json)!;
                var options=System.Text.Json.JsonSerializer.Deserialize<DecisionOption[]>(row.GetProperty("options"),json)!;
                var result=new Policy().Evaluate(state,options);
                var picked=options.Single(o=>o.Key==result.RecommendedKey).Card?.ClassName;
                Check(picked==row.GetProperty("expectedCard").GetString(),$"{row.GetProperty("seed")}: {picked}");
                Check(HeroStrategy.Readiness(state)==HeroStrategy.Readiness(state with{Foresight=null}),"no future combat strength");
            }
            var flame=System.Text.Json.JsonSerializer.Deserialize<RunSnapshot>(doc.RootElement.GetProperty("storybookState"),json)!;
            Check(flame.Hp==57&&flame.MaxHp==66,"actual starting health");
            Check(RelicValue.Evaluate("STORYBOOK",flame).Value<RelicValue.Evaluate("GOLDEN_COMPASS",flame).Value,"do not trade away a thin max-HP reserve");
        });
        Test("Ethereal draw, play and replay damage are different triggers",()=>{
            var curse=new CardSnapshot{ClassName="AscendersBane",Type="Curse",Rarity="Curse",Keywords=["Ethereal","Unplayable","Eternal"]};
            var s=With("Necrobinder","Defy") with{Deck=[C("Defy"),curse]};
            Check(NecrobinderMechanics.PayoffSupply(s,C("Pagestorm"))==2,"native AfterCardDrawn includes ethereal curses");
            Check(NecrobinderMechanics.PayoffSupply(s,C("SpiritOfAsh"))==1,"unplayable curses never trigger on-play block");
            var utility=s with{Deck=[..s.Deck,C("Lethality"),C("Eidolon"),C("Pagestorm")]};
            Check(HeroPolicy.Analyze(utility).DamageEngines==0,"replaying defense and a buff is not a standalone damage plan");
            var output=utility with{Deck=[..utility.Deck,C("Fear")]};
            Check(HeroPolicy.Analyze(output).DamageEngines>0,"actual replayable attacks can complete the plan");
            Check(HeroStrategy.Upgrade(s,C("SpiritOfAsh"))<HeroStrategy.Upgrade(output,C("SpiritOfAsh")),"smith uses playable supply too");
        });
        Test("Storybook weighs remaining exposure and max HP for all five characters",()=>{
            foreach(var hero in new[]{"Ironclad","Silent","Defect","Regent","Necrobinder"})
            {
                var s=new RunSnapshot{Character=hero,Hp=57,MaxHp=66,Act=2,Map=new(3,15,0,15,"StandardActMap",false,0,0,2,1,1,1,null)};
                var low=RelicValue.Evaluate("STORYBOOK",s);
                var rich=RelicValue.Evaluate("STORYBOOK",s with{Hp=160,MaxHp=160});
                Check(low.Value<0&&rich.Value>0,"shared policy must remain conditional, "+hero);
                Check(RelicValue.Evaluate("STORYBOOK",s with{Act=3,Map=s.Map with{CurrentRow=14}}).Value>low.Value,"short exposure is less costly");
                Check(RelicValue.Evaluate("STORYBOOK",s with{Hp=8}).Value<low.Value,"current low HP also matters");
                Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(low.Note),@"[\u3400-\u9fff]"),"English explanation");
            }
        });
    }
}
