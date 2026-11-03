using SpireStrategist.Core;
using System.Text.Json;

var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Check(bool value, string message) { if (!value) throw new Exception(message); }
CardSnapshot Card(string type, int cost = 1, bool upgradeable = true) => new()
{
    Id = type, ClassName = type, Name = type, Cost = cost, CanUpgrade = upgradeable
};
var startDeck = Enumerable.Repeat(Card("StrikeIronclad"), 5)
    .Concat(Enumerable.Repeat(Card("DefendIronclad"), 4)).Append(Card("Bash", 2)).ToArray();
var run = new RunSnapshot { Character = "Ironclad", Hp = 70, MaxHp = 80, Act = 1, Deck = startDeck };
var policy = new Policy();
DecisionOption Pick(CardSnapshot card) => new("pick", card.Name, "card", Card: card);
DecisionOption Skip() => new("skip", "跳过", "skip");
double Score(RunSnapshot state, CardSnapshot card) => policy.Evaluate(state, [Pick(card), Skip()]).Options[0].Score!.Value;
var rest = new[] { new DecisionOption("heal", "休息", "heal", HealAmount: 24), new DecisionOption("smith", "升级", "smith") };

Test("critical health chooses recovery; healthy state chooses an upgrade", () =>
{
    Check(policy.Evaluate(run with { Hp = 12 }, rest).RecommendedKey == "heal", "Must prioritize recovery at 12/80");
    Check(policy.Evaluate(run with { Hp = 78 }, rest).RecommendedKey == "smith", "Should upgrade at 78/80");
});
Test("heal value is capped at missing health and zero at full HP", () =>
{
    Check(policy.Evaluate(run with { Hp = 80 }, rest).Options[0].Score == 0, "No overheal credit");
    var small = policy.Evaluate(run with { Hp = 79 }, rest).Options[0].Score;
    Check(small < 1, "One HP must not score as 24 HP");
});
Test("real modified healing amount affects recommendation", () =>
{
    var weakHeal = new[] { rest[0] with { HealAmount = 1 }, rest[1] };
    Check(policy.Evaluate(run with { Hp = 30 }, rest).RecommendedKey == "heal", "Normal healing useful");
    Check(policy.Evaluate(run with { Hp = 30 }, weakHeal).RecommendedKey == "smith", "Actual healing must be used");
});
Test("unknown reward prevents false certainty", () =>
{
    var result = policy.Evaluate(run, [Pick(Card("UnknownModCard")), Skip()]);
    Check(result.RecommendedKey == null && result.Options[0].Score == null, "Unknown must not be assigned zero or compared");
});
Test("modified cards preserve base evaluation with explicit limitation", () =>
{
    var result = policy.Evaluate(run, [Pick(Card("Anger") with { Modified = true }), Skip()]);
    Check(result.Options[0].Score != null && result.Options[0].Notes.Any(n => n.Contains("额外效果未计分")), "Modified base estimate must be labelled");
});
Test("complete versioned card registry and both upgrade states", () =>
{
    Check(CardRegistry.All.Values.Count(c => c.Pool == "IroncladCardPool") == 90, "Ironclad card coverage");
    Check(CardRegistry.All.Values.Count(c => c.Pool == "ColorlessCardPool") == 65, "Colorless card coverage");
    foreach (var d in CardRegistry.All.Values)
        foreach (var c in new[] { d.Basic, d.Upgraded })
            Check(MultiplayerAdvice.Ignore(c)?CardCatalog.Find(c)==null:CardCatalog.Find(c) != null && double.IsFinite(Score(run, c)), "Missing: " + c.ClassName);
});
Test("window clamp handles offscreen positions and invalid saved data", () =>
{
    var p = new PanelLayout(-10, 2000).Clamp(365, 450, 1920, 1080);
    Check(p.X == 0 && p.Y == 630, "Must remain in viewport");
    p = new PanelLayout(float.NaN, float.PositiveInfinity).Clamp(365, 450, 1920, 1080);
    Check(float.IsFinite(p.X) && float.IsFinite(p.Y), "Invalid saved positions");
});
Test("card counts are not automatically interpreted as drawing", () =>
{
    Check(CardCatalog.Find(CardRegistry.All["Purity"].Basic)!.Draw == 0, "Purity exhausts instead of draws");
    Check(CardCatalog.Find(CardRegistry.All["DrumOfBattle"].Basic)!.Energy == 0, "Drum only grants energy when exhausted");
});
Test("Sown and Swift enchantments add benefits without treating once-only as repeatable", () =>
{
    var tremble = CardRegistry.All["Tremble"].Basic;
    var enchanted = tremble with { Modified=true, Enchantment="ENCHANTMENT.SOWN", EnchantmentAmount=1 };
    Check(Score(run, enchanted) > Score(run, tremble), "Known energy enchantment must affect evaluation");
    var plain = CardRegistry.All["StrikeIronclad"].Basic;
    Check(CardCatalog.Find(plain with {Modified=true,Enchantment="ENCHANTMENT.SWIFT",EnchantmentAmount=1})!.Draw == .25, "Once-only discount");
});
Test("disabled unknown option cannot block valid decisions", () =>
{
    var r = policy.Evaluate(run, [new("x", "unknown", "unknown", false), Skip()]);
    Check(r.RecommendedKey == "skip", "Disabled options must not participate");
});
Test("no fabricated skip in mandatory rewards", () =>
{
    var result = policy.Evaluate(run, [Pick(Card("StrikeIronclad"))]);
    Check(result.Options.Length == 1 && result.RecommendedKey == "pick", "Only legal supplied options may be chosen");
});
Test("duplicate basic attacks can lose to skip", () =>
{
    Check(policy.Evaluate(run, [Pick(Card("StrikeIronclad")), Skip()]).RecommendedKey == "skip", "Must support skipping weak additions");
});
Test("exhaust payoffs improve with actual enablers", () =>
{
    var payoff = Card("FeelNoPain");
    var supported = run with { Deck = startDeck.Append(Card("TrueGrit")).Append(Card("BurningPact")).ToArray() };
    Check(Score(supported, payoff) > Score(run, payoff) + 3, "Exhaust support must raise payoff value");
});
Test("low HP raises self-damage penalty", () =>
{
    Check(Score(run with { Hp = 10 }, Card("Offering", 0)) < Score(run with { Hp = 75 }, Card("Offering", 0)), "HP risk must be nonlinear");
});
Test("additional energy reduces high-cost burden", () =>
{
    Check(Score(run with { Energy = 5 }, Card("Bludgeon", 3)) > Score(run with { Energy = 3 }, Card("Bludgeon", 3)), "Energy budget must matter");
});
Test("unsupported characters abstain while multiplayer supports personal advice", () =>
{
    Check(policy.Evaluate(run with { Character = "UnknownModHero" }, rest).RecommendedKey == null, "Unsupported character");
    Check(policy.Evaluate(run with { PlayerCount = 2 }, rest).RecommendedKey != null, "Multiplayer personal camp advice");
});
Test("unknown camp options abstain while supported multi-upgrade is evaluated", () =>
{
    Check(policy.Evaluate(run, [rest[0], new("dig", "挖掘", "unknown")]).RecommendedKey == null, "Must not ignore digging");
    Check(policy.Evaluate(run, [rest[0], rest[1] with { SmithCount = 2 }]).RecommendedKey == "smith", "Multi upgrade supported");
});
Test("fully upgraded decks do not invent upgrade targets", () =>
{
    Check(policy.Evaluate(run with { Deck = startDeck.Select(c => c with { CanUpgrade = false }).ToArray() }, [rest[1]]).RecommendedKey == null, "No target");
});
Test("deterministic, finite scores and immutable input across health range", () =>
{
    var before = JsonSerializer.Serialize(run);
    foreach (var hp in new[] { 0, 1, 20, 40, 79, 80 })
    {
        var sample = run with { Hp = hp };
        foreach (var name in CardCatalog.Profiles.Keys)
        {
            var options = new[] { Pick(Card(name)), Skip() };
            var a = policy.Evaluate(sample, options);
            var b = policy.Evaluate(sample, options);
            Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b), "Determinism failed");
            Check(a.Options.All(o => o.Score == null || double.IsFinite(o.Score.Value)), "Non-finite score");
        }
    }
    Check(before == JsonSerializer.Serialize(run), "Policy mutated its input");
});
Test("invalid weights rejected", () =>
{
    try { new Policy(new() { HpRisk = double.NaN }); throw new Exception("Accepted NaN"); }
    catch (ArgumentException) { }
});
Test("recorded state and options can replay identical evaluation", () =>
{
    var recovered = JsonSerializer.Deserialize<RunSnapshot>(JsonSerializer.Serialize(run))!;
    Check(JsonSerializer.Serialize(policy.Evaluate(recovered, rest)) == JsonSerializer.Serialize(policy.Evaluate(run, rest)), "Replay mismatch");
});

Test("action requires stable ready decision and submits once", () =>
{
    var gate=new ActionGate();
    Check(!gate.TryClaim("a",0,true),"Default disabled"); gate.Enable();
    Check(!gate.TryClaim("a",10,true) && !gate.TryClaim("a",2009,true),"Delay");
    Check(gate.TryClaim("a",2010,true),"Submit");
    gate.Pause();gate.Enable();
    Check(!gate.TryClaim("a",5000,true),"No retry even after resume");
});
Test("pause, unreadiness and changing decision cancel pending action", () =>
{
    var gate=new ActionGate();gate.Enable();gate.TryClaim("a",0,true);
    gate.Pause();Check(!gate.TryClaim("a",3000,true),"Pause");gate.Enable();
    Check(!gate.TryClaim("a",4000,true),"Fresh delay");
    gate.TryClaim("a",5500,false);
    Check(!gate.TryClaim("a",6000,true),"Readiness restart");
    Check(!gate.TryClaim("b",8000,true),"Changed option restart");
    Check(gate.TryClaim("b",10000,true),"Stable new option");
});

Test("route priorities follow health, deck strength and completed key upgrades", () =>
{
    var nodes=new[]{new RouteNode("fire","RestSite",[]),new RouteNode("elite","Elite",[]),new RouteNode("event","Unknown",[]),new RouteNode("fight","Monster",[])};
    var low=RoutePolicy.Evaluate(run with{Hp=20},nodes,nodes.Select(n=>n.Id).ToArray(),new());
    Check(low.RecommendedNode=="fire","Low HP prioritizes camp");
    Check(low.Options.Single(o=>o.Node=="event").Score>low.Options.Single(o=>o.Node=="fight").Score,"Question above normal combat");
    var deck=new[]{"Offering","Inflame","Uppercut","ShrugItOff","Impervious","PommelStrike","BattleTrance"}.Select(n=>CardRegistry.All[n].Upgraded).ToArray();
    var healthy=RoutePolicy.Evaluate(run with{Hp=80,Deck=deck},nodes,["fire","elite"],new());
    Check(healthy.RecommendedNode=="elite","Healthy strong upgraded deck prefers elite");
    var unupgraded=RoutePolicy.Evaluate(run with{Hp=80,Deck=deck.Select(c=>c with{UpgradeLevel=0,CanUpgrade=true}).ToArray()},nodes,["fire","elite"],new());
    Check(unupgraded.RecommendedNode=="fire","Remaining key upgrades prefer fire");
});
Test("route forecasts arrival money including the shop-entry bank payout", () =>
{
    var nodes=new[]{new RouteNode("fight","Monster",["shop"]),new RouteNode("shop","Shop",[])};
    var e=new RouteEconomy{GoldPerRoom=12,GoldPerCombat=15,GoldMultiplier=1.25,NormalGoldMin=10,NormalGoldMax=20};
    var r=RoutePolicy.Evaluate(run with{Gold=100},nodes,["fight"],e);
    Check(r.Options[0].Shops.Single().Expected==168,"100 + (12+15+15+12)*1.25, rounded");
    var blocked=RoutePolicy.Evaluate(run with{Gold=100},nodes,["fight"],e with{GoldMultiplier=0});
    Check(blocked.Options[0].Shops.Single().Expected==100,"Ectoplasm blocks gains");
});
Test("shop weight grows with budget and discounts without assuming a lucky relic", () =>
{
    var nodes=new[]{new RouteNode("shop","Shop",[])};
    var poor=RoutePolicy.Evaluate(run with{Gold=20},nodes,["shop"],new()).Options[0];
    var rich=RoutePolicy.Evaluate(run with{Gold=300},nodes,["shop"],new()).Options[0];
    var discounted=RoutePolicy.Evaluate(run with{Gold=100},nodes,["shop"],new(){PriceMultiplier=.5}).Options[0];
    var plain=RoutePolicy.Evaluate(run with{Gold=100},nodes,["shop"],new()).Options[0];
    Check(rich.Score>poor.Score && discounted.Score>plain.Score,"Wealth and buying power");
    var route=new[]{new RouteNode("elite","Elite",["shop"]),nodes[0]};
    var potential=RoutePolicy.Evaluate(run,route,["elite"],new(){PossibleRelicGold=10}).Options[0].Shops.Single();
    Check(potential.PossibleRelicBonus==10 && potential.Expected==run.Gold+30,"Potential relic is separate from cash");
});
Test("route projection remains bounded and sees danger after shops", () =>
{
    var nodes=Enumerable.Range(0,20).Select(i=>new RouteNode(i.ToString(),i==2?"Shop":"Unknown",i==19?[]:[(i+1).ToString()])).ToArray();
    var r=RoutePolicy.Evaluate(run,nodes,["0"],new());
    Check(r.Options[0].Path.Length==12,"A shop must not truncate the twelve-node horizon");
    nodes=nodes.Select(n=>n with{Kind="Unknown"}).ToArray();
    Check(RoutePolicy.Evaluate(run,nodes,["0"],new()).Options[0].Path.Length==12,"Twelve-node horizon");
});
Test("unavoidable danger downstream cannot be removed by ending the forecast early", () =>
{
    var nodes=new[]{new RouteNode("a","Unknown",["danger"]),new RouteNode("danger","Elite",[]),new RouteNode("b","Unknown",["safe"]),new RouteNode("safe","RestSite",[])};
    var result=RoutePolicy.Evaluate(run with{Hp=10},nodes,["a","b"],new());
    Check(result.RecommendedNode=="b" && result.Options.Single(o=>o.Node=="a").Path.Contains("danger"),"Forced dangerous continuation must be scored");
});

Test("route scores do not gain value solely from a longer visible horizon",()=>
{
    var nodes=Enumerable.Range(0,6).Select(i=>new RouteNode("t"+i,"Treasure",i==5?[]:["t"+(i+1)])).Append(new RouteNode("shop","Shop",[])).ToArray();
    var result=RoutePolicy.Evaluate(run with{Gold=204},nodes,["shop","t0"],new());
    Check(Math.Abs(result.Options.Single(o=>o.Node=="shop").Score-result.Options.Single(o=>o.Node=="t0").Score)<.00001,"Equal 80-point rooms must not favor six treasure rooms over a shop just because forecast length differs");
});
Test("full health alone does not make a weak unupgraded deck elite ready",()=>
{
    var nodes=new[]{new RouteNode("elite","Elite",["fire"]),new RouteNode("fire","RestSite",["q"]),new RouteNode("q","Unknown",["fight"]),new RouteNode("fight","Monster",[]),new RouteNode("shop","Shop",[])};
    var result=RoutePolicy.Evaluate(run with{Hp=80,Gold=181,Deck=startDeck.Append(CardRegistry.All["IronWave"].Basic).Append(CardRegistry.All["Bludgeon"].Basic).ToArray()},nodes,["elite","shop"],new());
    Check(result.RecommendedNode=="shop","Weak healthy deck should not chase an elite because later rooms inflate its total");
});
Test("Iron Wave keeps early utility but does not receive two full coverage bonuses",()=>
{
    var wave=CardRegistry.All["IronWave"].Basic;
    var result=policy.Evaluate(run,[Pick(wave),Skip()]);
    Check(result.Options[0].Score>0&&result.Options[0].Parts.Any(p=>p.Reason.Contains("同时兑现")&&p.Value<0),"Early copy remains usable with conditional block value");
    var repeated=run with{Deck=run.Deck.Concat(Enumerable.Repeat(wave,2)).ToArray()};
    Check(Score(repeated,wave)<Score(run,wave),"Repeated transitional card has declining marginal utility");
    var strong=run with{Deck=new[]{"Uppercut","Whirlwind","PommelStrike","Hemokinesis","Bludgeon","ShrugItOff","Impervious","FlameBarrier","BattleTrance"}.Select(n=>CardRegistry.All[n].Upgraded).ToArray()};
    Check(Score(strong,wave)<0,"Already sufficient attack and defense should skip a plain Iron Wave");
});

Test("shop prioritizes basics, respects affordability and omits unsupported cards",()=>
{
    var s=run with{Gold=400};
    Check(ShopPolicy.Evaluate(s,[],100,400).Recommendation?.Kind=="remove","Default basic removal");
    Check(ShopPolicy.Evaluate(s with{Gold=20},[new("x",Card("UnknownModCard"),1)],100,20).Recommendation==null,"No unaffordable or unknown purchases");
    Check(ShopPolicy.Evaluate(s with{Deck=[Card("Bash")]},[],100,400).Recommendation==null,"Never remove nonbasic cards automatically");
});
Test("ghost seed, draw and exhaust moderately lower removal benefit",()=>
{
    var s=run with{Gold=400,Deck=startDeck.Concat(new[]{Card("BattleTrance"),Card("BurningPact"),Card("Corruption"),Card("Offering")}).ToArray()};
    var normal=ShopPolicy.Evaluate(s,[],100,400).Choices.Max(c=>c.Score);
    var ghost=ShopPolicy.Evaluate(s with{Relics=["RELIC.GHOST_SEED"]},[],100,400).Choices.Max(c=>c.Score);
    Check(ghost>0 && ghost<normal,"Ghost reduces but does not erase removal benefit");
    Check(ShopPolicy.Evaluate(s,[],100,400).Choices.All(c=>c.Reasons.Any(r=>r.Contains("强过牌=True")&&r.Contains("强消耗=True"))),"Engine mitigation recognized");
});
Test("shop values price, duplicate marginal benefit, reserve and purchase cap",()=>
{
    var s=run with{Gold=500};var card=Card("BattleTrance",0);
    var cheap=ShopPolicy.Evaluate(s,[new("x",card,25)],null,500);
    var dear=ShopPolicy.Evaluate(s,[new("x",card,450)],null,500);
    Check(cheap.Recommendation?.Key=="x" && dear.Recommendation==null,"Price and reserve affect purchase");
    var repeated=ShopPolicy.Evaluate(s with{Deck=s.Deck.Concat(Enumerable.Repeat(card,3)).ToArray()},[new("x",card,25)],null,500);
    Check(repeated.Choices[0].Score<cheap.Choices[0].Score,"Repeated engine cards have diminishing value");
    Check(ShopPolicy.Evaluate(s,[new("x",card,25)],null,500,2).Recommendation==null,"No uncontrolled courier purchase loop");
});

Test("recovery preserves necessary basics but leaves obsolete transitional cards",()=>
{
    var missing=run with{Deck=startDeck.Skip(1).ToArray()};
    Check(RecoveryPolicy.Evaluate(missing,Card("StrikeIronclad")).Action=="recover","Early damage floor protected");
    var strong=run with{Deck=Enumerable.Repeat(Card("TwinStrike"),5).Concat(Enumerable.Repeat(Card("ShrugItOff"),5)).Append(Card("StrikeIronclad")).ToArray()};
    Check(RecoveryPolicy.Evaluate(strong,Card("StrikeIronclad")).Action=="leave","Obsolete starter can be left behind");
    Check(RecoveryPolicy.Evaluate(strong,Card("TwinStrike")).Action=="leave","Redundant nonbasic transitional attack can be left behind");
    Check(RecoveryPolicy.Evaluate(strong,Card("Offering")).Action=="recover","Unique useful engine retained");
    Check(RecoveryPolicy.Evaluate(strong,Card("UnknownModCard")).Action=="manual","Unknown effect not permanently discarded");
    var saturated=strong with{Deck=Enumerable.Repeat(Card("TwinStrike"),15).Concat(Enumerable.Repeat(Card("ShrugItOff"),15)).ToArray()};
    Check(RecoveryPolicy.Evaluate(saturated,CardRegistry.All["TearAsunder"].Basic).Action=="leave","Now explicitly modelled: unsupported self-damage payoff can be left in a saturated deck");
    Check(RecoveryPolicy.Evaluate(strong,Card("StrikeIronclad") with{Modified=true}).Action=="manual","Modified card requires review");
    Check(RecoveryPolicy.Evaluate(strong with{Deck=[Card("Bash")]},Card("StrikeIronclad")).Action=="recover","Thin deck protected");
});
Test("Stoke models exhaust synergy without inventing draw or energy",()=>
{
    var c=CardRegistry.All["Stoke"].Basic;var p=CardCatalog.Find(c)!;
    Check(p.ExhaustSource>0 && p.Draw==0 && p.Energy==0,"Random generated cards are not deck draw");
    var supported=run with{Deck=startDeck.Append(Card("FeelNoPain")).ToArray()};
    Check(Score(supported,c)>Score(run,c),"Actual exhaust payoff improves Stoke evaluation");
    Check(Score(run,c)>0,"Early unformed deck can now choose Stoke under user-authorized feedback policy");
    var mature=run with{Act=3,Deck=new[]{"Offering","BattleTrance","DemonForm","Impervious","FlameBarrier","Uppercut","SwordBoomerang","BurningPact"}.Select(x=>CardRegistry.All[x].Basic).ToArray()};
    Check(Score(mature,c)<0,"No unconditional preference in formed late deck");
    Check(new Policy(allowExperimentalStoke:true).Evaluate(supported,[Pick(c),Skip()]).Options[0].Score==Score(supported,c),"Offline and live contextual policy are consistent");
});

Test("route decisions follow actual graph rather than total floor number",()=>
{
    var nodes=Enumerable.Range(0,320).Select(i=>new RouteNode("n"+i,i==319?"Boss":"Unknown",i==319?[]:["n"+(i+1)])).ToArray();
    var a=RoutePolicy.Evaluate(run with{Floor=5},nodes,["n301"],new());
    var b=RoutePolicy.Evaluate(run with{Floor=5000},nodes,["n301"],new());
    Check(a.RecommendedNode=="n301" && b.RecommendedNode=="n301" && a.Options[0].Score==b.Options[0].Score,"No hardcoded floor cutoff or scaling");
    Check(b.Options[0].Path.Length==12,"Long maps keep bounded lookahead");
});
Test("skull rooms retain continuation and boss economy, separate from elites",()=>
{
    var nodes=new[]{new RouteNode("skull","MiniBoss",["shop"]),new RouteNode("elite","Elite",["shop"]),new RouteNode("shop","Shop",[])};
    var e=new RouteEconomy{GoldPerRoom=12,CombatRewardMultiplier=1.5};
    var result=RoutePolicy.Evaluate(run with{Gold=0,Hp=80},nodes,["skull","elite"],e);
    var skull=result.Options.Single(o=>o.Node=="skull");var elite=result.Options.Single(o=>o.Node=="elite");
    Check(skull.Path.Contains("shop") && skull.Shops[0].Expected==174,"Boss reward scaled, bank room income not scaled, continuation preserved");
    Check(skull.ImmediateScore<elite.ImmediateScore,"Skull is not valued as an ordinary elite");
    var normal=RoutePolicy.Evaluate(run with{Hp=80},nodes,["skull"],e);
    var risky=RoutePolicy.Evaluate(run with{Hp=80},nodes,["skull"],e with{CombatRiskMultiplier=10});
    Check(risky.Options[0].RiskPenalty>normal.Options[0].RiskPenalty,"Extra-act danger applies before choosing risk without being diluted");
});

Test("event lethal actions and unaffordable choices never become recommendations",()=>
{
    foreach(var hp in new[]{1,5,30,80})foreach(var gold in new[]{0,50,200})
    {
        var s=run with{Hp=hp,Gold=gold};
        var choices=new[]{new EventChoice("fatal","fatal",HpLoss:hp,Benefit:100000),new EventChoice("cost","cost",Gold:-gold-1,Benefit:100000),new EventChoice("safe","safe")};
        Check(EventPolicy.Evaluate(s,choices).RecommendedKey=="safe","No lethal or over-budget action");
    }
    Check(EventPolicy.Evaluate(run with{Hp=80},[new("shrink","shrink",MaxHp:-70,HpLoss:10,Benefit:1000),new("leave","leave")]).RecommendedKey=="leave","Max HP reduction clamps current HP before subsequent damage");
});
Test("event healing caps, unknown branches abstain, locked branches do not block",()=>
{
    Check(EventPolicy.Evaluate(run with{Hp=80},[new("heal","heal",Heal:100)]).Options[0].Score==0,"No imaginary healing");
    Check(EventPolicy.Evaluate(run,[new("known","known"),new("mod","mod",Supported:false)]).RecommendedKey==null,"Unknown active branch blocks automation");
    Check(EventPolicy.Evaluate(run,[new("known","known"),new("mod","mod",Enabled:false,Supported:false)]).RecommendedKey=="known","Disabled branch does not block");
});
Test("event time horizon reads actual map rather than absolute floor",()=>
{
    var s=run with{Map=new(3,26,10,25,"Long",true,10)};
    Check(new StrategicValue(s).Horizon==new StrategicValue(s with{Floor=9999}).Horizon,"Absolute floor ignored");
    Check(new StrategicValue(s with{Act=3,Map=new(3,326,10,325,"Long",true,300)}).Horizon>new StrategicValue(s with{Act=3,Map=new(3,16,14,15,"Normal")}).Horizon,"Actual remaining opportunities matter");
});
Test("relic constraints respond to engine and potion capacity without arbitrary thresholds",()=>
{
    var slow=run with{Deck=startDeck};var fast=run with{Deck=startDeck.Concat(Enumerable.Repeat(Card("BattleTrance",0),4)).ToArray()};
    Check(RelicValue.Evaluate("VELVET_CHOKER",fast).Value<RelicValue.Evaluate("VELVET_CHOKER",slow).Value,"High play engine dislikes choker");
    Check(RelicValue.Evaluate("DELICATE_FROND",run with{PotionSlots=new string?[2]}).Value>0,"Two potion slots remain valuable");
    Check(!RelicValue.Evaluate("UNKNOWN_ANCIENT",run,true).Supported,"Unknown ancient is not assigned a fabricated known effect");
    Check(RelicValue.Evaluate("FAKE_SNECKO_EYE",run).Value<0,"Low cost starter deck does not buy confusion without draw");
});
Test("ten legacy event cards are registered without changing normal pools",()=>
{
    Check(CardRegistry.All.Values.Count(c=>c.Pool=="EventLegacy")==10,"Legacy count");
    var block=run with{Deck=startDeck.Concat(new[]{Card("Barricade",3),Card("Impervious",2),Card("ShrugItOff"),Card("ShrugItOff")}).ToArray()};
    var entrench=CardRegistry.All["Entrench"].Basic;
    Check(Score(block,entrench)>Score(run,entrench),"Block engine improves Entrench value");
});
Test("multi-upgrade and relic alternative compare with ordinary actions",()=>
{
    var one=policy.Evaluate(run,[new("smith","smith","smith")]).Options[0].Score;
    var many=policy.Evaluate(run,[new("smith","smith","smith",SmithCount:4)]).Options[0].Score;
    Check(many>one,"Multiple upgrade benefits count");
    Check(policy.Evaluate(run,[Skip(),new("sacrifice","sacrifice","effect",EffectValue:4)]).RecommendedKey=="sacrifice","Known alternative can execute");
});

Test("free redraw cards are not penalized for drawing themselves or inert deck size",()=>
{
    foreach(var name in new[]{"ThinkingAhead","Finesse","FlashOfSteel","MasterOfStrategy"})
    {
        var card=CardRegistry.All[name].Basic;
        var large=run with{Deck=startDeck.Concat(Enumerable.Repeat(Card("AscendersBane"),20)).ToArray()};
        var smallScore=Score(run,card);var largeScore=Score(large,card);
        Check(smallScore>0&&Math.Abs(smallScore-largeScore)<.001,name+" should retain self-replacement value");
    }
    Check(CardMechanics.DrawLoad(CardRegistry.All["ShrugItOff"].Basic,CardCatalog.Find(Card("ShrugItOff"))!)>0,"Paid redraw still needs energy");
});
Test("third damage copy and third slow power no longer feed their own synergy",()=>
{
    var s=run with{Deck=startDeck.Concat(new[]{Card("Inflame"),Card("Inflame"),Card("TwinStrike"),Card("TwinStrike"),Card("SwordBoomerang")}).ToArray()};
    Check(Score(s,CardRegistry.All["TwinStrike"].Basic)<0,"Third Twin Strike should lose to skip with existing multi coverage");
    var slow=s with{Deck=s.Deck.Concat(new[]{Card("DemonForm",3),Card("DemonForm",3)}).ToArray()};
    Check(Score(slow,CardRegistry.All["DemonForm"].Basic)<0,"Third Demon Form should lose to skip");
    Check(Score(s,CardRegistry.All["BurningPact"].Basic)>Score(s,CardRegistry.All["TwinStrike"].Basic),"Unsupplied cycling should outrank duplicate damage");
    var attackStarved=run with{Deck=[Card("SwordBoomerang"),Card("SwordBoomerang"),Card("Inflame"),Card("Inflame")]};
    Check(Score(attackStarved,CardRegistry.All["SwordBoomerang"].Basic)>0,"Diminishing returns must not become an unconditional third-copy ban for useful multihit damage");
});
Test("energy follows draw demand, existing supply, and actual HP cost",()=>
{
    var c=CardRegistry.All["Bloodletting"].Basic;
    var draw=run with{Deck=startDeck.Concat(new[]{Card("BurningPact"),Card("BattleTrance",0),Card("PommelStrike")}).ToArray()};
    Check(Score(draw,c)>Score(run,c)+1,"Draw raises marginal energy demand");
    Check(Score(draw,c)>0,"Healthy draw deck can pick Bloodletting");
    Check(Score(draw with{Hp=2},c)<Score(draw,c)-3,"Near-death self damage remains dangerous");
    Check(CardMechanics.EnergyDemand(draw with{Energy=6})<CardMechanics.EnergyDemand(draw),"Existing supply reduces demand");
});
Test("block, exhaust and self damage cores have independent conditional value",()=>
{
    var block=run with{Deck=startDeck.Concat(new[]{Card("Impervious",2),Card("FlameBarrier",2),Card("ShrugItOff"),Card("BodySlam")}).ToArray()};
    Check(Score(block,CardRegistry.All["Barricade"].Basic)>0,"Supported Barricade is viable without strength");
    Check(Score(block,CardRegistry.All["Unmovable"].Basic)>Score(run,CardRegistry.All["Unmovable"].Basic)+2,"Block engine values Unmovable");
    var burn=run with{Deck=startDeck.Concat(new[]{Card("Brand",0),Card("BurningPact"),Card("TrueGrit")}).ToArray()};
    Check(Score(burn,CardRegistry.All["PactsEnd"].Basic)>Score(run,CardRegistry.All["PactsEnd"].Basic)+3,"Pacts End needs enablers");
    var hurt=run with{Deck=startDeck.Concat(new[]{Card("Bloodletting",0),Card("Offering",0),Card("Brand",0)}).ToArray()};
    Check(Score(hurt,CardRegistry.All["Rupture"].Basic)>Score(run,CardRegistry.All["Rupture"].Basic)+3,"Self damage engine separate from raw strength count");
});
Test("perfect fit requires a card that survives until reshuffle",()=>
{
    var v=new StrategicValue(run);
    Check(v.Enchant(CardRegistry.All["DemonForm"].Basic,"PERFECT_FIT")==0,"Power never returns to discard");
    Check(v.Enchant(CardRegistry.All["Offering"].Basic,"PERFECT_FIT")==0,"Exhaust never returns to discard");
    Check(v.Enchant(CardRegistry.All["ThinkingAhead"].Upgraded,"PERFECT_FIT")>0,"Upgraded non-exhausting redraw can benefit");
});
Test("shop can remove curses with no basic cards and respects permanent cards",()=>
{
    var bad=Card("Normality") with{Type="Curse",Keywords=[]};
    var light=Card("Clumsy") with{Type="Curse",Keywords=["Ethereal"]};
    var eternal=Card("AscendersBane") with{Type="Curse",Keywords=["Ethereal","Eternal"]};
    var s=run with{Gold=200,Deck=[..startDeck,bad,light,eternal]};
    var plan=ShopPolicy.Evaluate(s,[],75,200);
    Check(plan.Recommendation?.DeckIndex==startDeck.Length,"Normality ahead of basic");
    Check(plan.Choices.All(c=>c.DeckIndex!=startDeck.Length+2),"Never offer permanent curse");
    Check(plan.Choices.Single(c=>c.DeckIndex==0).Score>plan.Choices.Single(c=>c.DeckIndex==startDeck.Length+1).Score,"Ethereal curse behind expendable basic");
    Check(ShopPolicy.Evaluate(s with{Deck=[bad]},[],75,200).Recommendation?.DeckIndex==0,"Remove curse after basics gone");
});
Test("courier can value replacing full slots without claiming money already received",()=>
{
    var full=run with{PotionSlots=["POTION.BLOCK_POTION","POTION.STRENGTH_POTION"],Commerce=new(1,2,0,0)};
    var grab=EventRules.Describe("POTION_COURIER","POTION_COURIER.pages.INITIAL.options.GRAB_POTIONS","grab",full,new Dictionary<string,double>{{"FoulPotions",3}});
    Check(grab.Benefit>0&&grab.Gold==0,"Replacement possible but cash not current gold");
    var denied=EventRules.Describe("POTION_COURIER","POTION_COURIER.pages.INITIAL.options.GRAB_POTIONS","grab",full with{Relics=["RELIC.SOZU"]},new Dictionary<string,double>{{"FoulPotions",3}});
    Check(denied.Benefit==0,"Sozu prevents pickup, so must not discard");
});

Test("recent combat healing adjustment needs three clean same-act samples and uses gross damage",()=>
{
    var sample=new CombatSummary(1,"Elite","fixture",4,50,0,80);
    var s=run with{Hp=65,RecentBattles=[sample,sample,sample]};
    Check(CardMechanics.RecentCombatHealingMultiplier(s)<1,"Three low damage samples reduce healthy rest pressure");
    Check(CardMechanics.RecentCombatHealingMultiplier(s with{RecentBattles=[sample,sample]})==1,"Do not extrapolate two samples");
    Check(CardMechanics.RecentCombatHealingMultiplier(s with{RecentBattles=[sample,sample,sample with{DamageTaken=30}]})==1,"Large gross damage remains risky even if healed");
    Check(CardMechanics.RecentCombatHealingMultiplier(s with{Act=2})==1,"Previous act cannot prove current act strength");
    Check(CardMechanics.RecentCombatHealingMultiplier(s with{Hp=10})==1,"History never removes critical healing urgency");
});

Test("all native ironclad and colorless cards have explicit mechanism profiles or multiplayer exclusions",()=>
{
    foreach(var d in CardRegistry.All.Values.Where(d=>d.Pool is "IroncladCardPool" or "ColorlessCardPool"))
        foreach(var c in new[]{d.Basic,d.Upgraded})
            Check(CardCatalog.Profiles.ContainsKey(c.ClassName)||ExtraCardModels.Find(c)!=null,"Still generic: "+c.ClassName);
    Check(ExtraCardModels.MultiplayerOnly.Count==17,"Audited multiplayer exclusions");
    Check(CardCatalog.Find(CardRegistry.All["Inferno"].Basic)!.SelfDamage>0,"Inferno must support Rupture");
    Check(ExtraCardModels.StrikeTags.Contains("SeekerStrike"),"Native tag, not translated-name guessing");
});
Test("paid draw-one is not free cycling and redundant transition cards decline",()=>
{
    var shrug=CardRegistry.All["ShrugItOff"].Basic;var pommel=CardRegistry.All["PommelStrike"].Basic;
    Check(CardMechanics.DrawLoad(shrug,CardCatalog.Find(shrug)!)>CardMechanics.DrawLoad(CardRegistry.All["Finesse"].Basic,CardCatalog.Find(CardRegistry.All["Finesse"].Basic)!),"Paid and free self replacement differ");
    var s=run with{Deck=startDeck.Concat(new[]{CardRegistry.All["FlameBarrier"].Basic,CardRegistry.All["Bludgeon"].Basic}).ToArray()};
    Check(Score(s,shrug)>Score(s with{Deck=s.Deck.Concat(new[]{shrug,pommel,shrug}).ToArray()},shrug)+3,"Redundant paid cyclers cannot perpetually reinforce one another");
    Check(CardCatalog.Find(CardRegistry.All["PommelStrike"].Upgraded)!.Draw>CardCatalog.Find(pommel)!.Draw,"Upgrade draws two");
});
Test("active exhaust has cleanup value without an exhaust payoff and ignores attack-only exhaust",()=>
{
    var curse=Card("Shame") with{Type="Curse"};
    var s=run with{Deck=startDeck.Append(curse).ToArray()};
    var second=policy.Evaluate(s,[Pick(CardRegistry.All["SecondWind"].Basic)]).Options[0];
    Check(second.Parts.Any(p=>p.Reason.Contains("主动清理")&&p.Value>0),"Standalone cleanup");
    Check(second.Parts.Any(p=>p.Reason.Contains("多次格挡")&&p.Value>0),"Multiple block gains");
    Check(!policy.Evaluate(s,[Pick(CardRegistry.All["Thrash"].Basic)]).Options[0].Parts.Any(p=>p.Reason.Contains("主动清理")),"Thrash cannot burn curses");
});
Test("conditional rare cards get actual mechanism value, not a rarity bonus",()=>
{
    var s=run with{Deck=startDeck.Concat(new[]{CardRegistry.All["Uppercut"].Upgraded,CardRegistry.All["Shockwave"].Basic}).ToArray()};
    Check(ExtraCardModels.Context(s,CardRegistry.All["Dominate"].Basic).Single().Value>0,"Vulnerable to permanent strength");
    Check(ExtraCardModels.Context(s,CardRegistry.All["Colossus"].Basic).Single().Value>
        ExtraCardModels.Context(s with{Deck=[CardRegistry.All["Anger"].Basic]},CardRegistry.All["Colossus"].Basic).Single().Value,"Colossus needs vulnerable");
    Check(ExtraCardModels.Context(s,CardRegistry.All["Stomp"].Basic).Single().Value>0,"Stomp discounts printed cost");
    Check(Score(s,CardRegistry.All["TheGambit"].Basic)<0,"Lethal downside not overwritten by rare-card enthusiasm");
});
Test("climb ledger counts only complete autonomous outcomes once",()=>
{
    var b=new ClimbBatch();Check(!b.Enabled,"Opt in");
    b.Enable();b.Start("joined",false);b.Finish(true);Check(b.Wins==0,"Joined run excluded");
    b.Start("win",true);b.Finish(true);Check(b.Finish(false)==null&&b.Wins==1&&b.Losses==0,"Duplicate terminal event ignored");
    b.Start("assisted",true);b.MarkAssisted("manual");b.Finish(true);Check(b.Wins==1,"Assisted excluded");
    b.Start("loss",true);b.RequestStopAfterRun(true);Check(b.Enabled,"Do not stop midrun");b.Finish(false);
    Check(!b.Enabled&&b.Losses==1&&b.Wins==1,"Complete then stop");
    b.Enable();b.Start("interrupted",true);b.Stop("user");Check(b.Results.Last().Outcome=="interrupted"&&b.Losses==1,"Interruption is not a loss or discarded row");
});
Test("final shop reserve uses real final-act topology including extended maps",()=>
{
    RouteNode[] graph=[new("shop","Shop",["rest"]),new("rest","RestSite",["boss"]),new("boss","Boss",[])];
    var s=run with{Act=3,Map=new(3,16,12,16,"native")};
    Check(ShopBudget.Reserve(s,graph,"shop")==0,"Final preboss shop spends reserve");
    Check(ShopBudget.Reserve(s with{Act=2},graph,"shop")==100,"Not final act");
    Check(ShopBudget.Reserve(s with{Map=s.Map with{BossRow=36,Rows=36}},graph,"shop")==100,"Extra layers not treated as end");
    Check(ShopBudget.Reserve(s with{Act=4,Map=s.Map with{TotalActs=4}},graph,"shop")==0,"Extra final act supported");
    Check(ShopBudget.Reserve(s,[graph[0],new("rest","Shop",["boss"]),graph[2]],"shop")==100,"A later shop keeps budget");
});

Test("random curse transforms cannot masquerade as removing a curse",()=>
{
    var curse=new CardSnapshot{ClassName="Doubt",Type="Curse",Rarity="Curse"};
    var basic=CardRegistry.All["StrikeIronclad"].Basic;
    var v=new StrategicValue(run);
    Check(v.Remove(curse)>v.Remove(basic),"Curse removal remains valuable");
    Check(v.Transform(curse)<v.Transform(basic),"Curse transform stays in curse pool");
});

Test("explicit newly modelled recovery is not mislabeled generic",()=>
{
    var taunt=CardRegistry.All["Taunt"].Basic;
    var s=run with{Deck=run.Deck.Concat(Enumerable.Repeat(CardRegistry.All["Anger"].Basic,5))
        .Concat(Enumerable.Repeat(CardRegistry.All["ShrugItOff"].Basic,3)).Concat(Enumerable.Repeat(taunt,3)).ToArray()};
    Check(RecoveryPolicy.Evaluate(s,taunt).Action=="leave","Redundant modelled card can be left");
});

Test("Paels tooth values upgrades and protects the remaining engine",()=>
{
    var basics=startDeck;
    var baseline=PaelsToothPolicy.Plan(run with{Deck=basics});
    var options=new[]{"BattleTrance","Inflame","ShrugItOff","TwinStrike","PommelStrike","Bash","Offering"}
        .Select(id=>CardRegistry.All[id].Basic).ToArray();
    var s=run with{Hp=80,MaxHp=80,Deck=basics.Concat(options).Concat(options).ToArray()};
    var candidates=s.Deck.Where(c=>c.CanUpgrade).ToArray();
    var plan=PaelsToothPolicy.Plan(s);
    Check(plan.Indices.Length==5&&plan.Indices.Distinct().Count()==5,"Five distinct legal cards");
    Check(plan.Indices.Count(i=>candidates[i].Rarity!="Basic")>=3,"Meaningful upgrades rather than five basic removals");
    Check(plan.Value>baseline.Value,"Thin initial deck does not get fake deletion value");
    Check(PaelsToothPolicy.Evaluate(s).Selection=="pael-upgrade","Dedicated selection purpose");
    Check(PaelsToothPolicy.Plan(s with{Hp=10}).Value<plan.Value,"Low HP increases temporary-loss risk");
    var only=PaelsToothPolicy.Plan(s,[CardRegistry.All["Bash"].Basic]);
    Check(only.Indices.Length==0&&only.Value<0,"Insufficient eligible cards cannot form a plan");
});
Test("autonomous solver concessions count once as loss with a reason",()=>
{
    var b=new ClimbBatch();b.Enable();b.Start("fresh",true);b.RequestStopAfterRun(true);
    b.Finish(false,"solver_death_turn");
    Check(b.Losses==1&&!b.Enabled&&b.Results.Single().Reason=="solver_death_turn","Concession tracked explicitly");
    Check(b.Finish(false)==null&&b.Losses==1,"Native duplicate end ignored");
});
Test("healthy camp upgrades at 55 of 80 while critical health still rests",()=>
{
    var s=run with{Hp=55,Deck=[..startDeck,CardRegistry.All["BattleTrance"].Basic]};
    Check(policy.Evaluate(s,rest).RecommendedKey=="smith","55/80 should smith a useful card");
    Check(policy.Evaluate(s with{Hp=12},rest).RecommendedKey=="heal","Do not trade critical survival for upgrade");
    Check(policy.Evaluate(s with{Hp=55,MaxHp=140},[rest[0] with{HealAmount=42},rest[1]]).RecommendedKey=="heal","55 HP is not a universal healthy threshold");
});
Test("question combat consumes the same normal sequence and predicts its gold",()=>
{
    RouteNode[] nodes=[new("q","Unknown",["m"]),new("m","Monster",["shop"]),new("shop","Shop",[])];
    var e=new RouteEconomy{UnknownOdds=[1,-1,0,0],NormalEncounters=[new(true,4,4),new(false,40,40)]};
    var r=RoutePolicy.Evaluate(run with{Gold=0},nodes,["q"],e);
    Check(r.Options[0].Shops[0].Expected==44,"Question must use first encounter; normal uses second");
    var safe=RoutePolicy.Evaluate(run with{Gold=0},nodes,["q"],e with{UnknownAllowed=[false,true,true,true]});
    Check(safe.Options[0].Shops[0].Expected==4,"Blocked question combat must not advance normal pool");
    var mixed=RoutePolicy.Evaluate(run with{Gold=0},nodes,["q"],e with{UnknownOdds=[.5,-1,0,0]});
    Check(mixed.Options[0].Shops[0].Expected==24,"Half chance mixture advances pool probabilistically");
});
Test("strong question fights lower route scores without pretending the event is certain",()=>
{
    RouteNode[] nodes=[new("q","Unknown",[])];
    var weak=new RouteEconomy{UnknownOdds=[1,-1,0,0],NormalEncounters=[new(true,10,20)]};
    var strong=weak with{NormalEncounters=[new(false,10,20)]};
    var s=run with{Hp=20};
    Check(RoutePolicy.Evaluate(s,nodes,["q"],strong).Options[0].Score<RoutePolicy.Evaluate(s,nodes,["q"],weak).Options[0].Score,"Strong encounters carry more risk");
});
Test("question fallback follows native room ordering when events are forbidden",()=>
{
    var e=new RouteEconomy{UnknownOdds=[-1,-1,0,0],UnknownAllowed=[false,false,true,true],UnknownAllowsEvent=false};
    var r=RoutePolicy.Evaluate(run,[new("q","Unknown",[])],["q"],e);
    Check(r.Options[0].Score==80,"Native treasure fallback before shop");
});
Test("boots are saved on equivalent routes but can unlock a substantially better route",()=>
{
    RouteNode[] same=[new("a","Unknown",[]),new("b","Unknown",[])];
    var e=new RouteEconomy{FlightRoots=["a"],FlightCharges=3};
    Check(RoutePolicy.Evaluate(run,same,["a","b"],e).RecommendedNode=="b","Do not spend boots on a tie");
    RouteNode[] better=[new("a","Unknown",["q2"]),new("q2","Unknown",["q3"]),new("q3","Unknown",[]),new("b","Monster",[])];
    Check(RoutePolicy.Evaluate(run,better,["a","b"],e).RecommendedNode=="a","Extra question chain can justify flight");
    var normal=RoutePolicy.Evaluate(run,same,["a","b"],e);
    var last=RoutePolicy.Evaluate(run,same,["a","b"],e with{FlightCharges=1});
    Check(last.Options.Single(o=>o.Node=="a").Score<normal.Options.Single(o=>o.Node=="a").Score,"Last charge has more option value");
});
Test("late shops scale with actual row and available money",()=>
{
    var s=run with{Gold=220,Map=new(3,30,1,30,"test",true,10)};
    RouteNode[] nodes=[new("early","Shop",[],5),new("late","Shop",[],26)];
    var r=RoutePolicy.Evaluate(s,nodes,["early","late"],new());
    Check(r.RecommendedNode=="late","Prefer later shop at equal budget");
    var poor=RoutePolicy.Evaluate(s with{Gold=0},nodes,["early","late"],new());
    Check(poor.Options[0].Score==poor.Options[1].Score,"Late shop has no free purchasing value when broke");
});
Test("priority relic whitelist respects actual act, money and existing ownership",()=>
{
    var s=run with{Gold=300};
    ShopRelic R(string id,int price=250)=>new(id,id,id,price);
    Check(PriorityShopPolicy.Evaluate(s,[R("MINIATURE_TENT")],[])!=null,"Tent first act");
    Check(PriorityShopPolicy.Evaluate(s with{Act=2},[R("MEMBERSHIP_CARD")],[])!=null,"Discount second act");
    Check(PriorityShopPolicy.Evaluate(s with{Act=3},[R("MINIATURE_TENT"),R("MEMBERSHIP_CARD")],[])==null,"No early relic priority after second act");
    Check(PriorityShopPolicy.Evaluate(s with{Act=4},[R("ICE_CREAM")],[])!=null,"Ice cream always");
    Check(PriorityShopPolicy.Evaluate(s,[R("ICE_CREAM"),R("MEMBERSHIP_CARD")],[])?.Relic.Id=="MEMBERSHIP_CARD","Discount before other buys");
    Check(PriorityShopPolicy.Evaluate(s,[R("VAJRA")],[])==null,"Unlisted relic keeps ordinary order");
    Check(PriorityShopPolicy.Evaluate(s,[R("ICE_CREAM",301)],[])==null,"Never exceed cash");
    Check(PriorityShopPolicy.Evaluate(s with{Relics=["RELIC.ICE_CREAM"]},[R("ICE_CREAM")],[])==null,"Do not duplicate a unique relic");
});
Test("Chemical X requires an existing X card or a worthwhile affordable bundle",()=>
{
    var x=CardRegistry.All["Whirlwind"].Basic;
    ShopRelic[] relics=[new("chem","CHEMICAL_X","化学物 X",200)];
    ShopCard[] cards=[new("whirlwind",x,25)];
    var s=run with{Gold=225};
    Check(PriorityShopPolicy.Evaluate(s,relics,[])==null,"No X cards means no standalone priority");
    Check(PriorityShopPolicy.Evaluate(s,relics,cards)?.Card?.Key=="whirlwind","Worthwhile affordable bundle");
    Check(PriorityShopPolicy.Evaluate(s with{Gold=224},relics,cards)==null,"One coin short rejects bundle");
    Check(PriorityShopPolicy.Evaluate(s with{Deck=[..startDeck,x]},relics,[])?.Card==null&&PriorityShopPolicy.Evaluate(s with{Deck=[..startDeck,x]},relics,[])!=null,"Existing X supports standalone");
    Check(PriorityShopPolicy.Evaluate(s,relics,[new("unknown",Card("Unknown") with{CostsX=true},1)])==null,"Unknown X card is not invented value");
    Check(PriorityShopPolicy.Evaluate(s,relics,cards,2)==null,"Card purchase cap applies to bundle");
});
Test("mutually exclusive Offering beats a basic removal on a healthy early deck",()=>
{
    var s=run with{Gold=200};
    var offering=CardRegistry.All["Offering"].Basic;
    var plan=ShopPolicy.Evaluate(s,[new("offering",offering,150)],100,200);
    Check(plan.Recommendation?.Key=="offering","Only 200 gold: buy Offering 150 instead of removal 100");
    Check(plan.Recommendation!.Reasons.Any(r=>r.Contains("预算二选一")),"Explain the foregone removal");
    Check(plan.Recommendation.Reasons.Any(r=>r.Contains("预留金币")),"Strong first copy can cross the 70-gold reserve");
    Console.WriteLine($"SHOP069 Offering={plan.Recommendation.Score:F3}; removal={plan.Choices.Where(c=>c.Kind=="remove").Max(c=>c.Score):F3}");
});
Test("shop scarce gold keeps health risk, curse urgency and duplicate penalties",()=>
{
    var offering=CardRegistry.All["Offering"].Basic;
    var s=run with{Gold=200};
    ShopCard[] stock=[new("offering",offering,150)];
    Check(ShopPolicy.Evaluate(s with{Hp=10},stock,100,200).Recommendation?.Kind=="remove","Critical HP does not force self-damage engine");
    Check(ShopPolicy.Evaluate(s with{Deck=[..startDeck,offering,offering]},stock,100,200).Recommendation?.Kind=="remove","Redundant Offerings are not automatic priorities");
    var curse=Card("Regret") with{Type="Curse"};
    var cursed=ShopPolicy.Evaluate(s with{Deck=[..startDeck,curse]},stock,100,200);
    Check(cursed.Recommendation?.DeckIndex==startDeck.Length,"A severe removable curse may outrank Offering");
    Check(ShopPolicy.Evaluate(s with{Gold=149},stock,100,149).Recommendation?.Kind=="remove","Cannot buy an unaffordable Offering");
    Check(ShopPolicy.Evaluate(s,stock,100,200,2).Recommendation?.Kind=="remove","Purchase limit still applies");
});
Test("shop compares ordinary cards and removes after a good card when both fit",()=>
{
    var offering=CardRegistry.All["Offering"].Basic;
    var s=run with{Gold=300};
    Check(ShopPolicy.Evaluate(s,[new("offering",offering,150)],100,300).Recommendation?.Key=="offering","Good card first even when both fit");
    Check(ShopPolicy.Evaluate(s with{Gold=150,Deck=[..startDeck,offering]},[],100,300,1).Recommendation?.Kind=="remove","Re-evaluate remaining removal after purchase");
    Check(ShopPolicy.Evaluate(s,[new("wave",CardRegistry.All["IronWave"].Basic,150)],100,300).Recommendation?.Kind=="remove","Not every card beats removal");
    Check(ShopPolicy.Evaluate(s,[new("rare",CardRegistry.All["Barricade"].Basic,150)],100,300).Recommendation?.Kind=="remove","Rarity alone grants no preference");
    var thin=run with{Gold=300,Deck=[CardRegistry.All["StrikeIronclad"].Basic,CardRegistry.All["DefendIronclad"].Basic]};
    Check(ShopPolicy.Evaluate(thin,[],100,300).Recommendation==null,"Do not remove remaining basic damage or defense");
});
Test("GhostSeed buys the many-basics screenshot budget before removal",()=>
{
    var s=run with{Gold=207,Hp=56,MaxHp=87,Deck=[..startDeck,..Enumerable.Repeat(CardRegistry.All["Anger"].Basic,7)]};
    ShopRelic[] relics=[new("seed","RELIC.GHOST_SEED","幽灵种子",204)];
    Check(PriorityShopPolicy.Evaluate(s,relics,[])?.Relic.Key=="seed","207 gold buys seed 204 instead of removal 150");
    Check(PriorityShopPolicy.Evaluate(s with{Gold=203},relics,[])==null,"Cannot spend nonexistent gold");
    Check(PriorityShopPolicy.Evaluate(s with{Relics=["RELIC.GHOST_SEED"]},relics,[])==null,"Already owned");
    Check(PriorityShopPolicy.Evaluate(s with{Deck=[..startDeck.Take(4),..Enumerable.Repeat(Card("Anger"),6)]},relics,[])==null,"Only four basics does not trigger priority");
    Check(PriorityShopPolicy.Evaluate(s with{Deck=[..startDeck.Take(5),..Enumerable.Repeat(Card("Anger"),20)]},relics,[])==null,"Low basic density does not trigger priority");
    Check(PriorityShopPolicy.Evaluate(s with{Deck=s.Deck.Select(c=>c with{Keywords=["Ethereal"]}).ToArray()},relics,[])==null,"Already ethereal basics cannot be counted twice");
    Check(PriorityShopPolicy.Evaluate(s,[..relics,new("discount","MEMBERSHIP_CARD","打折卡",100)],[])?.Relic.Key=="discount","Discount before seed");
});
Test("TwinStrike is a weak transition and loses to Boomerang in strength decks",()=>
{
    var twin=CardRegistry.All["TwinStrike"].Basic;
    var boom=CardRegistry.All["SwordBoomerang"].Basic;
    var strong=run with{Deck=[..startDeck,CardRegistry.All["Inflame"].Basic]};
    Check(Score(run,twin)<1,"Initial TwinStrike remains only a marginal damage stopgap");
    Check(Score(strong,twin)<Score(strong,boom),"Boomerang preferred with actual strength");
    Check(Score(strong with{Deck=[..strong.Deck,boom,boom]},twin)<0,"Existing good multihits make TwinStrike skippable");
    Check(CardCatalog.Find(CardRegistry.All["TwinStrike"].Upgraded)!.Attack==1.4,"Native upgraded 7x2 damage");
    Check(CardCatalog.Find(CardRegistry.All["SwordBoomerang"].Upgraded)!.StrengthPayoff==2,"Native upgraded fourth hit gets actual strength scaling");
});
Test("MoltenFist values one-shot damage and actual vulnerability engine",()=>
{
    var fist=CardRegistry.All["MoltenFist"].Basic;
    var initial=run with{Deck=startDeck.Select(c=>CardRegistry.All[c.ClassName].Basic).ToArray()};
    Check(Score(initial,fist)>Score(initial,CardRegistry.All["TwinStrike"].Basic)&&Score(initial,fist)>0,"Good early one-shot damage above TwinStrike");
    var noSource=initial with{Deck=initial.Deck.Where(c=>c.ClassName!="Bash").ToArray()};
    var phantom=noSource with{Deck=[..noSource.Deck,fist]};
    var parts=policy.Evaluate(phantom,[Pick(fist)]).Options[0].Parts;
    Check(parts.Where(p=>p.Reason.Contains("已有易伤的延长")).All(p=>p.Value==0),"Fist cannot supply its own first vulnerability");
    var engine=initial with{Deck=[..initial.Deck,CardRegistry.All["Shockwave"].Basic,CardRegistry.All["Dominate"].Basic,CardRegistry.All["Bully"].Basic]};
    Check(Score(engine,fist)>Score(initial,fist),"Established vulnerability deck recognizes core payoff");
    Check(Score(engine with{Deck=[..engine.Deck,fist,fist]},fist)<Score(engine,fist),"Not an unlimited Fist collection");
    Check(CardMechanics.DrawLoad(fist,CardCatalog.Find(fist)!)>0&&CardMechanics.DrawLoad(fist,CardCatalog.Find(fist)!)<1,"Exhaust reduces later occupancy, never removes first draw cost");
    Check(CardMechanics.ActiveExhaust(CardCatalog.Find(fist)!)==0,"Self exhaust is not status or curse cleanup");
    Check(CardCatalog.Find(CardRegistry.All["MoltenFist"].Upgraded)!.Attack==1.4,"Native upgraded damage retained");
    Console.WriteLine($"CARD069 TwinStrike={Score(initial,CardRegistry.All["TwinStrike"].Basic):F3}; MoltenFist={Score(initial,fist):F3}; vulnerable-engine MoltenFist={Score(engine,fist):F3}");
});
Test("Unrest prefers max-HP trade at 56/87 but preserves large heals and urgent exceptions",()=>
{
    Evaluation Plan(RunSnapshot s)=>EventPolicy.Evaluate(s,new[]{"REST","KILL"}.Select(k=>EventRules.Describe(
        "UNREST_SITE",k,k,s,new Dictionary<string,double>{{"Heal",s.MaxHp-s.Hp},{"MaxHpLoss",8}})).ToArray());
    var s=run with{Hp=56,MaxHp=87,NextNodeTypes=["Elite"],RecentBattles=[new(1,"Elite",null,22,6,0,87)]};
    Check(Plan(s).RecommendedKey=="KILL","Reported event prefers relic without current HP loss");
    Check(Plan(s with{Hp=30}).RecommendedKey=="REST","65 percent missing HP qualifies for full heal");
    Check(Plan(s with{Hp=10}).RecommendedKey=="REST","Critical health permits healing");
    Check(Plan(s with{Hp=27}).RecommendedKey=="REST","Recent elite loss can justify urgent healing");
    Check(Plan(s with{Hp=25,MaxHp=38}).RecommendedKey=="REST","Do not force an eight max-HP trade on fragile low maximum");
});
Test("stone sword delays five elite kills and gold wins unless HP is tight",()=>
{
    Evaluation Plan(RunSnapshot s)=>EventPolicy.Evaluate(s,new[]{"DIVE_INTO_WATER","GRAB_SWORD"}.Select(k=>
        EventRules.Describe("SUNKEN_STATUE",k,k,s,new Dictionary<string,double>{{"Gold",111},{"HpLoss",7}})).ToArray());
    Check(Plan(run with{Hp=65,Gold=145}).RecommendedKey=="DIVE_INTO_WATER","Healthy act-one state takes money");
    Check(Plan(run with{Hp=10}).RecommendedKey=="GRAB_SWORD","Low HP avoids damage");
    Check(Plan(run with{Hp=7}).RecommendedKey=="GRAB_SWORD","Lethal money option rejected");
});
Test("Stoke phase, solver support and explicit synergies are contextual",()=>
{
    var c=CardRegistry.All["Stoke"].Basic;
    Check(Score(run,c)>Score(run with{Act=2},c)&&Score(run with{Act=2},c)>Score(run with{Act=3},c),"Phase declines by actual act");
    Check(Score(run with{Floor=40},c)==Score(run,c),"Extra floors do not change stage");
    Check(Score(run with{CombatSolverAvailable=true},c)>Score(run,c),"Compatible solver adds explicit prior");
    foreach(var id in new[]{"DarkEmbrace","DrumOfBattle","HowlFromBeyond","AshenStrike"})
    {
        var payoff=CardRegistry.All[id].Basic;
        Check(Score(run with{Deck=[..startDeck,c]},payoff)>Score(run,payoff),"Stoke supports "+id);
    }
    var repeated=run with{Deck=[..startDeck,c,c,c]};
    Check(Score(repeated,c)<0,"Context bonus cannot justify unlimited Stoke duplicates");
    Check(CardCatalog.Find(c)!.Energy==0&&CardCatalog.Find(c)!.Draw==0,"No imaginary draw or free energy");
});
Test("recorded act-one event and boss reward replay with transparent solver evidence",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0610.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    var e=doc.RootElement.GetProperty("unrestState").Deserialize<RunSnapshot>(json)!;
    var choices=new[]{"REST","KILL"}.Select(k=>EventRules.Describe("UNREST_SITE",k,k,e,new Dictionary<string,double>{{"Heal",31},{"MaxHpLoss",8}})).ToArray();
    var ep=EventPolicy.Evaluate(e,choices);
    Check(ep.RecommendedKey=="KILL","Real 17-card event state changes recommendation");
    var d=doc.RootElement.GetProperty("stoke");var s=d.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var options=d.GetProperty("options").Deserialize<DecisionOption[]>(json)!;var result=policy.Evaluate(s,options);
    Check(result.RecommendedKey=="card:0","Real boss reward recommends Stoke over Brand and skip");
    Console.WriteLine("REPLAY0610 "+JsonSerializer.Serialize(new{eventScores=ep.Options.Select(o=>new{o.Name,o.Score}),cardScores=result.Options.Select(o=>new{o.Name,o.Score}),
        withoutSolver=Score(s with{CombatSolverAvailable=false},CardRegistry.All["Stoke"].Basic)}));
});
Test("actual AFBB late routes choose recovery and keep shop continuation visible",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0611.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var row in doc.RootElement.GetProperty("routes").EnumerateArray())
    {
        var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var result=RoutePolicy.Evaluate(s,row.GetProperty("nodes").Deserialize<RouteNode[]>(json)!,
            row.GetProperty("legal").Deserialize<string[]>(json)!,row.GetProperty("economy").Deserialize<RouteEconomy>(json)!);
        Console.WriteLine("REPLAY0611ROUTE "+JsonSerializer.Serialize(new{s.Floor,s.Hp,result.RecommendedNode,result.Options}));
        if(s.Floor==39)Check(result.RecommendedNode=="4,6","36 HP with two key upgrades should take right camp route");
        if(s.Floor==41)
        {
            Check(result.RecommendedNode=="1,8","7 HP should take available left camp");
            Check(result.Options.Single(o=>o.Kind=="Shop").Path.Contains("2,9"),"Must include shop's forced monster");
        }
        if(s.Floor==42)Check(result.Options.Length==1,"After wrong shop there was no legal route back to left camp");
        var shifted=RoutePolicy.Evaluate(s with{Floor=s.Floor+1000},row.GetProperty("nodes").Deserialize<RouteNode[]>(json)!,
            row.GetProperty("legal").Deserialize<string[]>(json)!,row.GetProperty("economy").Deserialize<RouteEconomy>(json)!);
        Check(result.RecommendedNode==shifted.RecommendedNode,"Actual graph, not total floor, governs decisions");
    }
});
Test("actual camps value Stoke upgrade and still heal in a critical state",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0611.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var row in doc.RootElement.GetProperty("camps").EnumerateArray())
    {
        var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var result=policy.Evaluate(s,row.GetProperty("options").Deserialize<DecisionOption[]>(json)!);
        var upgrades=s.Deck.Where(c=>c.CanUpgrade).OrderByDescending(c=>CardMechanics.Upgrade(s,c)).ToArray();
        Console.WriteLine("REPLAY0611CAMP "+JsonSerializer.Serialize(new{s.Floor,s.Hp,result.RecommendedKey,
            first=upgrades.FirstOrDefault()?.ClassName,scores=result.Options.Select(o=>new{o.Name,o.Score})}));
        if(s.Floor is 25 or 27 or 32)Check(upgrades[0].ClassName=="Stoke","Unupgraded generated hand should no longer lose to transition upgrades");
        if(s.Floor==44)Check(result.RecommendedKey=="HEAL","9 HP still needs actual recovery");
        if(s.Floor==47)Check(result.RecommendedKey=="SMITH","At 31 HP, evaluate the live exhaust engine upgrade");
    }
});
Test("courier cannot erase combat potions for unspendable final-act gold",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0611.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    var s=doc.RootElement.GetProperty("courier").GetProperty("decision").GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var vars=new Dictionary<string,double>{{"FoulPotions",3}};
    EventChoice[] Choices(RunSnapshot state)=>new[]{"GRAB_POTIONS","RANSACK"}
        .Select(k=>EventRules.Describe("POTION_COURIER",k,k,state,vars)).ToArray();
    Check(EventPolicy.Evaluate(s,Choices(s)).RecommendedKey=="RANSACK","No remaining shop: choose alternative");
    Check(!CourierPolicy.MayReplace(s),"Do not discard Weak and Precognition for unspendable gold");
    Check(!CourierPolicy.MayReplace(s with{Commerce=null}),"Missing evidence is not a shop");
    Check(!CourierPolicy.MayReplace(s with{Commerce=new(0,null,5,0)}),"Question nodes are not certain shops");
    var near=s with{Commerce=new(1,1,0,0)};
    Check(CourierPolicy.MayReplace(near)&&EventPolicy.Evaluate(near,Choices(near)).RecommendedKey=="GRAB_POTIONS","Known nearby shop retains economic option");
    Check(!CourierPolicy.MayReplace(near with{Relics=[..s.Relics,"RELIC.ECTOPLASM"]}),"No gold gain, no replacement");
    Check(!CourierPolicy.MayReplace(near with{Relics=[..s.Relics,"RELIC.SOZU"]}),"No potion procurement, no replacement");
});
Test("fatal estimates cannot be revived by downstream rewards and camps",()=>
{
    RouteNode[] nodes=[new("shop","Shop",["fight"]),new("fight","Monster",["camp"]),
        new("camp","RestSite",["loot"]),new("loot","Treasure",[]),new("safe","RestSite",[])];
    var r=RoutePolicy.Evaluate(run with{Hp=1,Gold=500},nodes,["shop","safe"],new());
    var shop=r.Options.Single(o=>o.Node=="shop");
    Check(r.RecommendedNode=="safe"&&shop.RiskPenalty>0,"Immediate danger cannot be diluted by loot");
    Check(!shop.Path.Contains("camp")&&shop.ProjectedHp==0,"No forecast resurrection");
    var noRest=RoutePolicy.Evaluate(run with{Hp=1},[new("camp","RestSite",[])],["camp"],new(){CanRest=false});
    Check(noRest.Options[0].ProjectedHp<=1,"Disabled rest cannot heal");
});
Test("Wellspring recorded healthy early run prefers permanent removal over a potion",()=>
{
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0612.json")));
    var s=doc.RootElement.GetProperty("WELLSPRING").GetProperty("decision").GetProperty("state").Deserialize<RunSnapshot>(json)!;
    Evaluation Eval(RunSnapshot x)=>EventPolicy.Evaluate(x,new[]{"BATHE","BOTTLE"}
        .Select(k=>EventRules.Describe("WELLSPRING",k,k,x,new Dictionary<string,double>())).ToArray());
    var actual=Eval(s);
    Check(actual.RecommendedKey=="BATHE","60/80, many basics, long future: delete a basic");
    Check(Math.Abs(actual.Options.Single(o=>o.Key=="BATHE").Score!.Value-7.2)<.001,"Recorded removal remains 9.2, temporary burden 2");
    Check(Eval(s with{Hp=8}).RecommendedKey=="BOTTLE","Very low health retains a short-term option");
    Check(Eval(s with{Deck=[new(){ClassName="AscendersBane",Type="Curse",Keywords=["Eternal"]}]}).RecommendedKey=="BOTTLE",
        "No useful removal: do not force a curse");
    Check(Eval(s with{PotionSlots=["WeakPotion","FirePotion"]}).RecommendedKey=="BATHE","Full belt should not value an uncollectable potion");
    var late=s with{Act=3,Map=new(3,16,14,16,"test")};
    Check(EventRules.Describe("WELLSPRING","BATHE","",late,new Dictionary<string,double>()).Burden>=5,
        "No assumption of five combat expiry with only two rooms remaining");
});
Test("native Guilty class is temporary and becomes less valuable to remove near expiry",()=>
{
    var guilty=new CardSnapshot{ClassName="Guilty",Type="Curse",Variables=new(){{"Combats",5}}};
    var v=new StrategicValue(run);
    Check(v.Remove(guilty)==4&&v.Remove(guilty)<v.Remove(startDeck[0]),"Native class must not fall through to permanent curse 18");
    Check(v.Remove(guilty with{Variables=new(){{"Combats",1}}})<v.Remove(guilty),"One combat left is less removal value");
    Check(v.Remove(new(){ClassName="Injury",Type="Curse"})==18,"Permanent curse cost preserved");
});
Test("Neow user tiers apply without changing generic relic scores",()=>
{
    var s=run with{Hp=64,Gold=99,Map=new(3,16,0,16,"test"),PotionSlots=[null,null]};
    double V(string id)=>NeowPolicy.Evaluate(id,s).Value;
    Check(V("PRECISE_SCISSORS")>V("SILVER_CRUCIBLE")&&V("PRECARIOUS_SHEARS")>V("SILVER_CRUCIBLE"),"Removal tier leads");
    Check(V("SILVER_CRUCIBLE")>V("NEW_LEAF")&&V("NEW_LEAF")>V("ARCANE_SCROLL"),"Second, third, fourth tiers");
    foreach(var id in new[]{"NUTRITIOUS_OYSTER","BOOMING_CONCH","FISHING_ROD","DOWSING_ROD","LAVA_ROCK","NEOWS_TALISMAN","POMANDER"})
        Check(V(id)<V("GOLDEN_PEARL"),"Low tier "+id);
    Check(NeowPolicy.Evaluate("PRECARIOUS_SHEARS",s with{Hp=17}).Value<V("PRECISE_SCISSORS"),"Preserve low HP caution");
    Check(NeowPolicy.Excluded("KALEIDOSCOPE")&&NeowPolicy.Excluded("MASSIVE_SCROLL"),"Unsupported rewards are excluded");
    var blue=CardRegistry.All["ShrugItOff"].Basic with{Rarity="Uncommon"};
    var gold=CardRegistry.All["Offering"].Basic with{Rarity="Rare"};
    Check(NeowPolicy.TransformScore(s,[blue],false)>=31&&NeowPolicy.TransformScore(s,[gold],false)>V("PRECARIOUS_SHEARS"),"Actual rarity changes transformation tier");
    Check(NeowPolicy.TransformScore(s,[gold,CardRegistry.All["Anger"].Basic],true)>V("PRECARIOUS_SHEARS"),"A pair containing gold is promoted, with both cards evaluated");
    var ordinary=RelicValue.Evaluate("NUTRITIOUS_OYSTER",s).Value;
    Check(ordinary>V("NUTRITIOUS_OYSTER"),"Opening ranking does not overwrite relic mechanism value");
});
Test("Neow removal plans strike first and one strike one defend, regardless of deck ordering",()=>
{
    var cards=new[]{CardRegistry.All["DefendIronclad"].Basic,CardRegistry.All["StrikeIronclad"].Basic,
        CardRegistry.All["StrikeIronclad"].Basic,CardRegistry.All["Bash"].Basic};
    Check(NeowPolicy.RemovalPlan(run,cards,1).SequenceEqual(new[]{1}),"Single removes strike");
    Check(NeowPolicy.RemovalPlan(run,cards,2).SequenceEqual(new[]{1,0}),"Double removes one of each");
});
Test("Neow boots respond to actual crossing opportunities",()=>
{
    RouteNode[] nodes=[new("start","Ancient",["a"],0),new("a","Monster",["b"],1),
        new("b","Monster",[],2),new("camp","RestSite",[],2)];
    Check(NeowPolicy.BootsOpportunity(nodes,"start")>0,"Jump can reach otherwise inaccessible camp");
    Check(NeowPolicy.BootsOpportunity(nodes[..3],"start")==0,"Linear path gains no imaginary camp");
});
Test("recorded Goopy defends do not block stolen Ashen Strike recovery",()=>
{
    using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0614.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    var s=fixture.RootElement.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var c=fixture.RootElement.GetProperty("card").Deserialize<CardSnapshot>(json)!;
    Check(s.Deck.Count(x=>x.Enchantment=="ENCHANTMENT.GOOPY")==4,"Actual four enchanted defends");
    Check(RecoveryPolicy.Evaluate(s,c).Action=="recover","Known Goopy state evaluates Ashen Strike normally");
    Check(RecoveryPolicy.Evaluate(s,c,true).Action=="recover","Continuous mode recovers without waiting");
});
Test("continuous recovery resolves uncertainty without forcing permanent discard",()=>
{
    var strong=run with{Deck=Enumerable.Repeat(Card("TwinStrike"),5).Concat(Enumerable.Repeat(Card("ShrugItOff"),5)).Append(Card("StrikeIronclad")).ToArray()};
    var unknown=Card("UnknownModCard");
    Check(RecoveryPolicy.Evaluate(strong,unknown).Action=="manual","Normal control still requests unknown review");
    var fallback=RecoveryPolicy.Evaluate(strong,unknown,true);
    Check(fallback.Action=="recover"&&fallback.MarginalScore==null,"Unknown fallback does not fabricate a score");
    Check(RecoveryPolicy.Evaluate(strong,Card("StrikeIronclad"),true).Action=="leave","Known obsolete cards may still be abandoned");
    Check(RecoveryPolicy.Evaluate(strong,Card("StrikeIronclad"),true,false).Action=="recover","Mandatory recovery settles loop");
    Check(RecoveryPolicy.Evaluate(strong,Card("StrikeIronclad"),false,false).Action=="leave","Normal policy does not silently force a choice");
    Check(RecoveryPolicy.Evaluate(strong,Card("Bash") with{Modified=true,Enchantment="ENCHANTMENT.CLONE"},true).Action=="recover","Unknown enchantment restored in loop");
    Check(CardCatalog.HasUnmodelledModification(Card("Bash") with{Modified=true,Enchantment="ENCHANTMENT.SHARP",Affliction="UNKNOWN"}),"Known enchantment does not hide unknown affliction");
});
Test("Bash upgrade competes as modest vulnerability extension, not a default engine",()=>
{
    var bash=CardRegistry.All["Bash"].Basic;
    foreach(var key in new[]{"TrueGrit","Stoke","DarkEmbrace","Offering","Uppercut"})
    {
        var s=run with{Deck=[..startDeck,CardRegistry.All[key].Basic]};
        Check(CardMechanics.Upgrade(s,bash)<CardMechanics.Upgrade(s,CardRegistry.All[key].Basic),"Core upgrade must beat Bash: "+key);
    }
    var alternate=run with{Deck=[..startDeck,CardRegistry.All["Shockwave"].Basic]};
    Check(CardMechanics.Upgrade(alternate,bash)<CardMechanics.Upgrade(run,bash),"Other Vulnerable sources reduce Bash's marginal turn");
    var synergy=run with{Deck=[..startDeck,CardRegistry.All["MoltenFist"].Basic]};
    Check(CardMechanics.Upgrade(synergy,bash)>CardMechanics.Upgrade(run,bash),"Real vulnerability payoff still matters");
    Check(policy.Evaluate(run with{Hp=80},rest).RecommendedKey=="smith","Basic-only healthy camp still upgrades");
});
Test("duplicate BattleTrance does not masquerade as new defensive readiness",()=>
{
    var c=CardRegistry.All["BattleTrance"].Basic;
    var first=run with{Deck=[..startDeck,c]};var second=first with{Deck=[..first.Deck,c]};
    Check(DeckReadiness.Read(second).Quality-DeckReadiness.Read(first).Quality<.03,"Duplicate draw cannot inflate readiness by ten points");
    Check(CardMechanics.Upgrade(second,c)<CardMechanics.Upgrade(first,c),"Duplicate upgrade discounts shared no-draw lock");
    Check(Score(second,c)<Score(first,c),"Further copies lose marginal value");
});
Test("recorded batch decisions repair missing defense and duplicate draw",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0617.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var row in doc.RootElement.GetProperty("decisions").EnumerateArray())
    {
        var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var options=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
        var result=policy.Evaluate(s,options);var picked=options.Single(o=>o.Key==result.RecommendedKey);
        var seed=row.GetProperty("seed").GetString();
        if(seed=="C6D4069F9B25")Check(picked.Kind=="skip","Do not add second conflicting Trance in recorded weak deck");
        if(seed=="9561CA709A80")Check(picked.Card?.ClassName=="Shockwave","Sustained debuff must not vanish under generic taxes");
        if(seed=="088C7C869AF4"&&options.Any(o=>o.Card?.ClassName=="FeelNoPain"))
            Check(picked.Card?.ClassName=="FeelNoPain","Complete exhaust/block engine before strength payoff");
        if(seed=="B143435788F8")Check(s.Deck.Where(c=>c.CanUpgrade).MaxBy(c=>CardMechanics.Upgrade(s,c))!.ClassName=="FeelNoPain","Upgrade sustained exhaust defense before another paid cycler");
    }
});
Test("recorded 37 HP route keeps the camp healing branch and remains floor-independent",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0617.json")));
    var row=doc.RootElement.GetProperty("routes")[0];var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
    var graph=row.GetProperty("nodes").Deserialize<RouteNode[]>(json)!;var legal=row.GetProperty("legal").Deserialize<string[]>(json)!;
    var economy=row.GetProperty("economy").Deserialize<RouteEconomy>(json)!;
    var before=JsonSerializer.Serialize(s);
    var r=RoutePolicy.Evaluate(s,graph,legal,economy);
    Check(r.RecommendedNode=="2,11","Immediate camp now beats dangerous elite");
    Check(r.Options[0].CampPlans[0].Contains("休息"),"Do not bind camp to greedy smith");
    Check(r.Options.Single(o=>o.Kind=="Elite").RiskPenalty>0,"Previous large elite damage cannot be zero risk");
    var shifted=RoutePolicy.Evaluate(s with{Floor=5011},graph,legal,economy);
    Check(shifted.RecommendedNode==r.RecommendedNode&&shifted.Options[0].Score==r.Options[0].Score,"Actual graph supports extra floors");
    Check(JsonSerializer.Serialize(s)==before,"Route projection does not mutate player data");
    Check(JsonSerializer.Serialize(r)==JsonSerializer.Serialize(RoutePolicy.Evaluate(s,graph,legal,economy)),"Stable bounded projection");
});
Test("arrival camp uses the same forward survival check without breaking special relic actions",()=>
{
    var s=run with{Hp=37,Deck=[..startDeck,CardRegistry.All["Uppercut"].Basic],
        RecentBattles=[new(1,"Elite","previous",49,40,1,80)]};
    RouteNode[] graph=[new("camp","RestSite",["elite"]),new("elite","Elite",[])];
    var risks=CampRoutePolicy.Risks(s,rest,graph,"camp",new());
    Check(risks["smith"]>risks["heal"],"Healing and potion use must not disguise earlier gross damage");
    var captured=s with{CampRouteRisk=risks};
    Check(policy.Evaluate(captured,rest).RecommendedKey=="heal","Upon arrival do not discard planned safety to smith");
    var healthy=s with{Hp=80};
    Check(policy.Evaluate(healthy with{CampRouteRisk=CampRoutePolicy.Risks(healthy,rest,graph,"camp",new())},rest).RecommendedKey=="smith","Healthy camps still smith");
    Check(CampRoutePolicy.Risks(s with{Relics=["RELIC.MINIATURE_TENT"]},rest,graph,"camp",new()).Count==0,"Tent multi-actions not treated as exclusive");
    Check(CampRoutePolicy.Risks(s,[..rest,new("dig","dig","effect",EffectValue:8)],graph,"camp",new()).Count==0,"Do not invent transitions for special actions");
    var blocked=new[]{rest[0] with{Enabled=false},rest[1]};
    Check(!CampRoutePolicy.Risks(s,blocked,graph,"camp",new()).ContainsKey("heal"),"Unavailable healing never becomes a legal recommendation");
});
Test("recorded vulnerability and long-fight decisions address the actual missing components",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0618.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var row in doc.RootElement.GetProperty("decisions").EnumerateArray())
    {
        var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var opts=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
        var result=policy.Evaluate(s,opts);var picked=opts.Single(o=>o.Key==result.RecommendedKey);
        if(s.Floor is 9 or 14 or 31)Check(picked.Card?.ClassName=="Tremble","Cheap vulnerability no longer erased by Bash");
        if(s.Floor==18&&opts.Any(o=>o.Card?.ClassName=="Dominate"))Check(picked.Card?.ClassName=="Dominate","Supported second Dominate beats nominal block-card count");
        if(s.Floor==29)Check(picked.Card?.ClassName=="CrimsonMantle","Recognize recurring defense and existing Juggernaut");
        if(s.Floor==38)Check(picked.Kind=="skip","Do not keep stacking late Inflame");
        var best=s.Deck.Where(c=>c.CanUpgrade).MaxBy(c=>CardMechanics.Upgrade(s,c));
        if(s.Floor==25)Check(best?.ClassName=="Dominate","Upgrade existing engine before fixed strength");
        if(s.Floor==32)Check(best?.ClassName=="PommelStrike","Extra draw may still beat gold rarity");
        if(s.Floor==47)Check(best?.ClassName!="Juggernaut","Poor trigger support must affect smith ranking too");
    }
});
Test("Tremble supports cheap opening vulnerability without forcing endless copies",()=>
{
    var initial=run with{Deck=startDeck.Select(c=>CardRegistry.All[c.ClassName].Basic).ToArray()};
    var tremble=CardRegistry.All["Tremble"].Basic;
    Check(Score(initial,tremble)>0,"Bash alone cannot veto first Tremble");
    var covered=initial with{Deck=[..initial.Deck,CardRegistry.All["Break"].Basic,CardRegistry.All["Shockwave"].Upgraded,tremble]};
    Check(Score(covered,tremble)<0,"Sufficient efficient coverage makes another copy optional");
    Check(IroncladSynergies.VulnerableCoverage(initial with{Deck=[CardRegistry.All["Break"].Basic]})>
        IroncladSynergies.VulnerableCoverage(initial with{Deck=[CardRegistry.All["Bash"].Upgraded]}),"Ancient starter is a different quality source");
    Check(Score(initial with{Floor=5000},tremble)==Score(initial,tremble),"Extra floors do not age out a first-act starter");
});
Test("Juggernaut uses real trigger support instead of a pile of Defends",()=>
{
    var jug=CardRegistry.All["Juggernaut"].Basic;
    var weak=run with{Deck=Enumerable.Repeat(CardRegistry.All["DefendIronclad"].Basic,8).Append(CardRegistry.All["Bash"].Basic).ToArray()};
    var supported=run with{Deck=[..startDeck,CardRegistry.All["FeelNoPain"].Basic,CardRegistry.All["Stoke"].Basic,
        CardRegistry.All["SecondWind"].Basic,CardRegistry.All["DarkEmbrace"].Basic]};
    Check(Score(weak,jug)<0,"Basics alone are not a block-trigger engine");
    Check(Score(supported,jug)>0,"Actual multi-exhaust block engine retains Juggernaut");
    Check(CardMechanics.Upgrade(supported,jug)>CardMechanics.Upgrade(weak,jug),"Upgrade follows attainable triggers");
});
Test("Crimson Mantle values recurring defense without pretending to heal critical HP",()=>
{
    var c=CardRegistry.All["CrimsonMantle"].Basic;
    var s=run with{Act=2,Deck=[..startDeck,CardRegistry.All["Juggernaut"].Basic]};
    Check(Score(s,c)>Score(s with{Hp=2},c),"Continuing self-damage retains low HP penalty");
    Check(Score(s with{Hp=2},c)<0,"It cannot rescue two HP on the current turn");
    Check(Score(s,c)>Score(s with{Deck=startDeck},c),"Recognize automatic damage trigger partner");
});
Test("fixed strength remains useful early but cannot masquerade as unlimited boss growth",()=>
{
    var inflame=CardRegistry.All["Inflame"].Basic;
    var early=run with{Deck=[..startDeck,CardRegistry.All["SwordBoomerang"].Basic]};
    Check(Score(early,inflame)>0,"First early strength still helps existing multihit");
    var late=early with{Act=3,Deck=[..early.Deck,inflame,CardRegistry.All["Dominate"].Basic,CardRegistry.All["Tremble"].Basic]};
    Check(Score(late,inflame)<0,"Late redundant fixed strength can lose to skipping");
    Check(DeckReadiness.Read(early with{Deck=[..early.Deck,inflame,inflame]}).Growth<.6,"Two fixed buffs do not fill boss growth axis");
    Check(CardMechanics.Upgrade(late,inflame)<CardMechanics.Upgrade(early,inflame),"Upgrade follows marginal strength demand");
});
Test("recorded late shop buys a supported multihit before removing a basic",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","feedback-0618.json")));
    var s=doc.RootElement.GetProperty("shopState").Deserialize<RunSnapshot>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    var c=CardRegistry.All["Conflagration"].Basic;
    var stock=new[]{new ShopCard("conflagration",c,144),new ShopCard("drum",CardRegistry.All["DrumOfBattle"].Basic,74)};
    var shop=ShopPolicy.Evaluate(s,stock,200,s.Gold);
    Console.WriteLine($"CARD0618 Conflagration={Score(s,c):F3}; shop={shop.Recommendation?.Name}/{shop.Recommendation?.Score:F3}; readiness={DeckReadiness.Read(s)}");
    Check(shop.Recommendation?.Key=="conflagration","Supported damage upgrade competes against removal and actual price");
    Check(ShopPolicy.Evaluate(s with{Gold=100},stock,null,100).Choices.All(x=>x.Key!="conflagration"),"No imaginary purchase without gold");
    var noStrength=s with{Deck=s.Deck.Where(x=>x.ClassName is not ("Inflame" or "Dominate")).ToArray(),Relics=[]};
    Check(Score(noStrength,c)<Score(s,c)-3,"Not an unconditional rare-card bonus");
    var before=JsonSerializer.Serialize(s);_ = IroncladSynergies.SupportedStrength(s);
    Check(JsonSerializer.Serialize(s)==before,"Analysis never modifies snapshot or live RNG");
});
Test("ancient tooth values actual Bash replacement and inherits upgrade benefits",()=>
{
    var bash=CardRegistry.All["Bash"].Basic;
    var initial=run with{Deck=[bash]};
    var plain=RelicValue.Evaluate("ARCHAIC_TOOTH",initial,true).Value;
    var linked=initial with{Deck=[bash,CardRegistry.All["Dominate"].Basic,CardRegistry.All["MoltenFist"].Basic]};
    Check(plain>20,"Not the old fixed twelve-point placeholder");
    Check(RelicValue.Evaluate("ARCHAIC_TOOTH",linked,true).Value>plain,"Actual vulnerability payoff increases replacement value");
    Check(RelicValue.Evaluate("ARCHAIC_TOOTH",initial with{Deck=[CardRegistry.All["Bash"].Upgraded]},true).Value>plain,"Native inheritance yields upgraded Break");
    Check(RelicValue.Evaluate("ARCHAIC_TOOTH",initial with{Deck=[]},true).Value<=0,"No starter means no invented transformation");
    Check(CardMechanics.Upgrade(linked,CardRegistry.All["Break"].Basic)>CardMechanics.Upgrade(linked,bash),"Ancient upgrade is not ordinary Bash upgrade");
});
Test("boss defense discounts one-shot supply and respects draw and energy budget",()=>
{
    var baseState=run with{Deck=[..startDeck,CardRegistry.All["Impervious"].Basic,CardRegistry.All["Impervious"].Basic]};
    var r=DeckReadiness.Read(baseState);
    Check(r.SustainedDefense<r.Defense,"Large one-shot blocks are not permanent per-turn defense");
    var costly=baseState with{Energy=2};var funded=baseState with{Energy=5};
    Check(DeckReadiness.Read(costly).SustainedDefense<DeckReadiness.Read(funded).SustainedDefense,"Actual energy affects payable long-fight defense");
    var recurring=baseState with{Deck=[..baseState.Deck,CardRegistry.All["CrimsonMantle"].Basic]};
    Check(DeckReadiness.Read(recurring).SustainedDefense>r.SustainedDefense,"Automatic block supplies later turns");
    var many=run with{Deck=Enumerable.Repeat(CardRegistry.All["DefendIronclad"].Basic,30).ToArray()};
    Check(DeckReadiness.Read(many).SustainedDefense<1,"Thirty Defends do not mean thirty payable blocks each turn");
});
Test("climb preferences persist selected character and difficulty, fresh install defaults to zero",()=>
{
    var path=Path.Combine(Path.GetTempPath(),"strategist-settings-"+Guid.NewGuid().ToString("N"),"climb.json");
    var p=ClimbPreferences.Load(path);
    Check(p.Ascension==0&&p.Character=="Ironclad"&&p.Supported,"fresh default N0 Ironclad");
    p.Ascension=10;p.Character="Necrobinder";p.Save(path);
    var loaded=ClimbPreferences.Load(path);
    Check(loaded.Ascension==10&&loaded.Character=="Necrobinder"&&loaded.Supported,"persist enabled Necrobinder selection");
    Check(ClimbPreferences.Characters.Length==5,"all five selectable labels");
});
Test("highest streak excludes joins, interruptions and duplicate outcomes; buckets stay separate",()=>
{
    var p=new ClimbPreferences();var mode=ClimbPreferences.Mode("Ironclad",10);
    ClimbResult R(string id,bool full=true,string outcome="win")=>new(id,id,full,outcome,"");
    void Finish(ClimbResult r,int n=10){p.Begin(r,"Ironclad",n);p.Complete(r);p.Complete(r);}
    Finish(R("a"));Finish(R("b"));Check(p.Get(mode).Current==2&&p.Get(mode).Best==2,"two wins counted once each");
    Finish(R("c",outcome:"loss"));Check(p.Get(mode).Current==0&&p.Get(mode).Best==2,"loss resets current only");
    Finish(R("d"));Finish(R("e",false));Check(p.Get(mode).Current==0&&p.Get(mode).Best==2,"joined win cannot extend streak");
    Finish(R("f"));Finish(R("g",outcome:"interrupted"));Check(p.Get(mode).Current==0,"immediate stop breaks streak");
    Finish(R("h"),0);Check(p.Get(ClimbPreferences.Mode("Ironclad",0)).Best==1&&p.Get(mode).Best==2,"difficulty buckets isolated");
});
Test("climb streak survives clean restart and detects unfinished run on disk",()=>
{
    var path=Path.Combine(Path.GetTempPath(),"strategist-streak-"+Guid.NewGuid().ToString("N"),"climb.json");
    var p=new ClimbPreferences();var mode=ClimbPreferences.Mode("Ironclad",10);
    var win=new ClimbResult("a","seed",true,"win","");p.Begin(win,"Ironclad",10);p.Complete(win);p.Save(path);
    p=ClimbPreferences.Load(path);Check(p.Get(mode).Current==1&&p.Get(mode).Best==1,"pre-stop preserves completed streak across restart");
    p.Begin(win with{Id="pending"},"Ironclad",10);p.Save(path);
    p=ClimbPreferences.Load(path);Check(p.Get(mode).Current==0&&p.Get(mode).Best==1&&p.PendingRunId==null,"crash cannot bridge a lost run");
    p.Complete(win with{Id="pending"});Check(p.Get(mode).Current==0,"late settlement cannot count recovered interruption");
});
Test("fast loop keeps readiness, cancellation and exactly-once gate semantics",()=>
{
    Check(AutomationTiming.Delay(false,8000)==8000&&AutomationTiming.Delay(false,2000)==2000,"ordinary delays unchanged");
    Check(AutomationTiming.Delay(true,8000)==250&&AutomationTiming.NextRunDelay==1000,"loop short delays");
    var gate=new ActionGate();gate.Enable();Check(!gate.TryClaim("buy",0,true,250),"first poll never executes");
    Check(!gate.TryClaim("buy",300,false,250),"not ready resets");
    Check(!gate.TryClaim("buy",600,true,250),"fresh wait after not ready");gate.Pause();
    Check(!gate.TryClaim("buy",900,true,250),"stop cancels pending");gate.Enable();gate.TryClaim("buy",1000,true,250);
    Check(gate.TryClaim("buy",1250,true,250)&&!gate.TryClaim("buy",1500,true,250),"one fast purchase only");
});
Test("Adroit adds block to attacks and prefers reusable useful cards",()=>
{
    var v=new StrategicValue(run);
    var useful=Card("PommelStrike") with {Type="Attack"};
    var basic=Card("StrikeIronclad") with {Type="Attack"};
    Check(v.Enchant(useful,"ADROIT",3)>0,"non-block attacks receive positive Adroit benefit");
    Check(v.Enchant(useful,"ADROIT",3)>v.Enchant(basic,"ADROIT",3),"prefer repeatable useful attack over removable basic");
    Check(v.Enchant(useful,"ADROIT",3)>v.Enchant(useful with {Keywords=["Exhaust"]},"ADROIT",3),"once-only card receives fewer expected activations");
});
Test("English catalog preserves placeholders and translates every sampled template",()=>
{
    var slots=new System.Text.RegularExpressions.Regex(@"\{(\d+)\}");
    foreach(var (source,target) in Localization.Catalog)
    {
        Check(slots.Matches(source).Select(m=>m.Value).Order().SequenceEqual(slots.Matches(target).Select(m=>m.Value).Order()),"placeholder mismatch: "+source);
        var input=slots.Replace(source,m=>"VALUE"+m.Groups[1].Value);
        var expected=slots.Replace(target,m=>"VALUE"+m.Groups[1].Value);
        Check(Localization.English(input)==expected,"translation mismatch: "+source+" => "+Localization.English(input));
    }
});
Test("presentation translation keeps policy numbers IDs and original records unchanged",()=>
{
    var evaluation=policy.Evaluate(run,[Pick(Card("Offering")),Pick(Card("Bash",2)),Skip()]);
    var before=JsonSerializer.Serialize(evaluation);
    foreach(var option in evaluation.Options)
    {
        foreach(var part in option.Parts)Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(part.Reason),"[\\u3400-\\u9fff]"),part.Reason);
        foreach(var note in option.Notes)Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(note),"[\\u3400-\\u9fff]"),note);
    }
    Check(before==JsonSerializer.Serialize(evaluation),"display must not mutate a decision");
    Check(Localization.English("实际可回复 12.5 点生命")=="Can actually restore 12.5 HP","actual numeric value preserved");
    Check(Localization.English("接管中 · 升级 Offering")=="Takeover · Upgrade: Offering","concatenated status");
    Check(Localization.English("unknown.mod.card") == "unknown.mod.card","unknown identifiers preserved");
});
Test("English display translates multiline indented actual advice without dropping values",()=>
{
    var samples=new[]{
        "建议：Offering\n记录：abcd1234\n分数用于相对比较，不是胜率。\n\nOffering：12.5\n  实际可回复 12.5 点生命\n",
        "商店评估（待校准）\n推荐 删 Strike：100 金币 / 2.5 分\n  价格机会成本 -2.5\n  幽灵种子=False，强过牌=False，强消耗=False；删牌收益倍率 1.00\n",
        "第 1 幕 · 2 层 · 进阶 10\n生命 70/80  金币 600  牌组 11 张\n",
        "自动爬塔已关闭\n所有已结算：2 胜 / 3 负\n完整自动样本：2 胜 / 3 负；人工参与、中途加入或中断 1 局"
    };
    foreach(var input in samples)
    {
        var output=Localization.English(input);
        Check(!System.Text.RegularExpressions.Regex.IsMatch(output,"[\\u3400-\\u9fff]"),output);
        foreach(System.Text.RegularExpressions.Match number in System.Text.RegularExpressions.Regex.Matches(input,@"[0-9]+(?:\.[0-9]+)?"))
            Check(output.Contains(number.Value),"lost numeric value: "+output);
        Check(output.Split('\n').Length==input.Split('\n').Length,"lost paragraph: "+output);
    }
});
Test("English handles composed route notes and indented numeric explanations",()=>
{
    var input="· 问号普通战概率字段 10 %；新手局强制规则另计。按原生允许类型与概率递增估计，后续概率为期望近似，非确定预言。\n地图 21 行 · 共 3 幕 · 每幕额外 5 层\n  +2.5  实际可回复 12.5 点生命";
    var output=Localization.English(input);
    Check(!System.Text.RegularExpressions.Regex.IsMatch(output,"[\\u3400-\\u9fff]"),output);
    Check(output.Contains("12.5")&&output.Contains("21")&&output.Contains("10 %"),"keep numbers");
});
Test("Defect native policy matches reviewed offline prior across character and neutral fixtures",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-prior-v14.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var item in doc.RootElement.EnumerateArray())
    {
        var s=item.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        foreach(var value in item.GetProperty("scores").EnumerateArray())
        {
            var name=value.GetProperty("name").GetString()!;
            var up=value.GetProperty("upgraded").GetBoolean();
            var c=DefectPolicy.Card(name,up)??(up?CardRegistry.All[name].Upgraded:CardRegistry.All[name].Basic);
            var result=policy.Evaluate(s,[Pick(c),Skip()]);var actual=result.Options[0].Score;
            Check(result.PolicyVersion==DefectPolicy.Version,"Defect dispatcher");
            foreach(var part in result.Options[0].Parts)
                Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(part.Reason),"[\\u3400-\\u9fff]"),"Untranslated Defect reason: "+part.Reason);
            var expected=value.GetProperty("score");
            if(expected.ValueKind==JsonValueKind.Null)Check(actual==null,"Multiplayer exclusion: "+name);
            else Check(actual.HasValue&&Math.Abs(actual.Value-expected.GetDouble())<.0011,$"{name} actual={actual} expected={expected} act={s.Act}");
        }
    }
});
Test("Defect refuses unknown effects and supports rest decisions",()=>
{
    var s=run with{Character="Defect",Deck=[DefectPolicy.Card("Zap")!,DefectPolicy.Card("Dualcast")!]};
    Check(policy.Evaluate(s,[Pick(Card("UnknownModCard")),Skip()]).RecommendedKey==null,"unknown not skipped by default");
    Check(policy.Evaluate(s,[Pick(DefectPolicy.Card("Glacier")! with{Enchantment="CUSTOM"}),Skip()]).RecommendedKey==null,"unknown enchantment");
    Check(policy.Evaluate(s,rest).RecommendedKey=="smith","Defect has its own rest rules");
    Check(policy.Evaluate(s with{PlayerCount=2},[Pick(DefectPolicy.Card("Glacier")!),Skip()]).RecommendedKey!=null,"multiplayer personal advice");
    Check(policy.Evaluate(s,[Pick(DefectPolicy.Card("MeteorStrike")!),Skip()]).RecommendedKey=="skip","no self-funded meteor");
});
Test("Defect copy engine protects draw copies and distinguishes ordinary copies",()=>
{
    var a=DefectPolicy.Card("AdaptiveStrike")!;
    var one=DefectPolicy.Card("AllForOne")!;
    var s=run with{Character="Defect",Act=3,Deck=[a,DefectPolicy.Card("GoForTheEyes")!,DefectPolicy.Card("Hologram",true)!]};
    Check(Score(s,one)>Score(s with{Deck=s.Deck.Where(c=>c!=a).ToArray()},one)+3,"generated zero cards must count for retrieval");
    var swift=a with{Modified=true,Enchantment="ENCHANTMENT.SWIFT",EnchantmentAmount=2};
    var combo=s with{Deck=[swift,one,DefectPolicy.Card("Coolheaded",true)!]};
    Check(Score(combo,DefectPolicy.Card("Claw",true)!)<0,"copy engine does not need extra claws");
    Check(Score(combo,DefectPolicy.Card("Iteration",true)!)<Score(s,DefectPolicy.Card("Iteration",true)!),"Swift copies reduce extra draw demand");
    Check(DefectStrategy.Remove(combo,swift)<-20,"protect enchanted core from removal");
    var thin=combo with{Deck=[..combo.Deck,DefectPolicy.Card("Scavenge")!]};
    Check(CopyEngine.Active(thin),"exhaust support does not erase engine");
    Check(new ClimbPreferences{Character="Defect"}.Supported,"Defect climbing enabled");
    Check(new ClimbPreferences{Character="Regent"}.Supported,"Regent is now supported");
});
Test("Defect camping subtracts route risk and preserves emergency healing",()=>
{
    var s=run with{Character="Defect",Hp=60,MaxHp=75,Deck=[DefectPolicy.Card("Hologram")!]};
    var calm=DefectStrategy.NonCard(s,new("smith","Smith","smith"))!.Score!.Value;
    var risky=s with{CampRouteRisk=new(){{"smith",20}}};
    Check(DefectStrategy.NonCard(risky,new("smith","Smith","smith"))!.Score<calm,"higher projected damage never increases smith score");
    Check(DefectStrategy.NonCard(s with{Hp=5},new("heal","Rest","heal",HealAmount:22))!.Score>calm,"emergency survival can beat an engine upgrade");
});
Test("Defect neutral support and Anger preserve self-copy mechanics",()=>
{
    var a=DefectPolicy.Card("AdaptiveStrike")! with{Enchantment="ENCHANTMENT.SWIFT",EnchantmentAmount=2,Modified=true};
    var s=run with{Character="Defect",Act=2,Deck=[a,DefectPolicy.Card("AllForOne")!,..Enumerable.Repeat(DefectPolicy.Card("DefendDefect")!,4)]};
    var purity=CardRegistry.All["Purity"].Basic;
    Check(Score(s,purity)>3,"native neutral exhaust supports thinning without Ironclad payoffs");
    Check(Score(s,CardRegistry.All["MasterOfStrategy"].Basic)>0,"one-shot neutral draw is not an unsupported auto-skip");
    var anger=Card("Anger");
    var enchanted=anger with{Enchantment="ENCHANTMENT.SWIFT",EnchantmentAmount=2,Modified=true};
    Check(Score(run,enchanted)>Score(run,anger)+3,"Anger inherited unused Swift receives a bounded mechanism bonus");
});
Test("Defect partial free rewards keep known support without inventing unknown values",()=>
{
    var s=run with{Character="Defect",Deck=[DefectPolicy.Card("Zap")!,DefectPolicy.Card("Dualcast")!]};
    DecisionOption[] options=[new("unknown","Unknown","card",Card:Card("UnknownModCard")),new("draw","Draw","card",Card:CardRegistry.All["MasterOfStrategy"].Basic),Skip()];
    var evaluation=policy.Evaluate(s,options);
    Check(evaluation.RecommendedKey==null,"manual advice does not pretend complete comparison");
    Check(DefectPolicy.KnownFreeReward(options,evaluation)?.Key=="draw","unknown option does not discard known positive support");
    options=[options[0],Skip()];
    Check(DefectPolicy.KnownFreeReward(options,policy.Evaluate(s,options))?.Kind=="skip","all unknown free rewards skip explicitly");
    options=[options[0]];
    Check(DefectPolicy.KnownFreeReward(options,policy.Evaluate(s,options))==null,"no forced or paid selection fallback");
});
Test("Defect evaluates X terminals against affordable existing retrieval",()=>
{
    var s=run with{Character="Defect",Act=3,Energy=3,Deck=[DefectPolicy.Card("Dualcast",true)!,DefectPolicy.Card("Hologram")!,DefectPolicy.Card("Darkness")!,DefectPolicy.Card("Coolheaded")!,DefectPolicy.Card("Coolheaded")!,DefectPolicy.Card("Defragment")!]};
    var terminal=DefectPolicy.Card("MultiCast")!;
    bool Limited(RunSnapshot state,CardSnapshot card)=>policy.Evaluate(state,[Pick(card)]).Options[0].Parts.Any(p=>p.Reason.Contains("缺爆发费用"));
    Check(Limited(s,terminal),"three energy with free Dualcast and Hologram discounts extra X terminal");
    Check(!Limited(s with{Deck=[..s.Deck,DefectPolicy.Card("Turbo")!]},terminal),"burst energy removes this specific constraint");
    Check(!Limited(s with{Relics=["RELIC.CHEMICAL_X"]},terminal),"Chemical X changes the incremental evoke count");
    Check(!Limited(s,DefectPolicy.Card("MultiCast",true)!),"upgraded extra evoke is retained");
    Check(policy.Evaluate(s,[Pick(DefectPolicy.Card("Defragment")!)]).Options[0].Parts.Any(p=>p.Reason.Contains("冰霜防线")),"second focus is not purely redundant with frost support");
});
Test("Defect native optional reroll cannot block known rewards",()=>
{
    var s=run with{Character="Defect",Deck=[DefectPolicy.Card("Zap")!,DefectPolicy.Card("Dualcast")!]};
    var reroll=new DecisionOption("alternative:1","Reroll","reroll");
    DecisionOption[] options=[Pick(DefectPolicy.Card("Glacier")!),Skip(),reroll];
    Check(policy.Evaluate(s,options).RecommendedKey==options[0].Key,"positive Glacier beats an unforecast reroll");
    options=[Pick(DefectPolicy.Card("StrikeDefect")!),Skip(),reroll];
    Check(policy.Evaluate(s,options).RecommendedKey==reroll.Key,"free native reroll beats skipping all negative cards");
    options=[new("unknown","Unknown","card",Card:Card("UnknownModCard")),new("draw","Draw","card",Card:CardRegistry.All["MasterOfStrategy"].Basic),Skip(),reroll];
    var e=policy.Evaluate(s,options);
    Check(e.RecommendedKey==null&&DefectPolicy.KnownFreeReward(options,e)?.Key=="draw","unknown card plus reroll still preserves known support");
    options=[options[0],Skip(),reroll with{Enabled=false}];
    Check(DefectPolicy.KnownFreeReward(options,policy.Evaluate(s,options))?.Kind=="skip","unavailable reroll is never selected");
});
Test("ThisOrThat follows money preference with forecast bag exception and lethal guard",()=>
{
    var vars=new Dictionary<string,double>{{"HpLoss",6},{"Gold",50}};
    var s=run with{Hp=7,MaxHp=80,Gold=100,Relics=["RELIC.BAG_OF_PREPARATION"]};
    var money=EventRules.Describe("THIS_OR_THAT","PLAIN","Money",s,vars);
    var relic=EventRules.Describe("THIS_OR_THAT","ORNATE","Relic",s,vars);
    Check(EventPolicy.Evaluate(s,[money,relic]).RecommendedKey=="PLAIN","low nonlethal HP and already-owned bag do not change the explicit preference");
    Check(EventPolicy.Evaluate(s,[ThisOrThatPolicy.Apply(money,"RELIC.BAG_OF_PREPARATION"),ThisOrThatPolicy.Apply(relic,"RELIC.BAG_OF_PREPARATION")]).RecommendedKey=="ORNATE","predicted reward bag is the exception");
    Check(EventPolicy.Evaluate(s,[ThisOrThatPolicy.Apply(money,"RELIC.ICE_CREAM",true),ThisOrThatPolicy.Apply(relic,"RELIC.ICE_CREAM",true)]).RecommendedKey=="ORNATE","confirmed rare relic is also an explicit exception");
    Check(EventPolicy.Evaluate(s,[ThisOrThatPolicy.Apply(money,"RELIC.SOMETHING_ELSE"),ThisOrThatPolicy.Apply(relic,"RELIC.SOMETHING_ELSE")]).RecommendedKey=="PLAIN","other common or uncommon relic stays below money");
    Check(EventPolicy.Evaluate(s with{Hp=6},[money,relic]).RecommendedKey=="ORNATE","direct death protection remains above preference");
});
Test("Defect draft v6 separates engines and retains shared status support",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    RunSnapshot S(params string[] n)=>run with{Character="Defect",Act=2,Hp=60,MaxHp=75,Deck=n.Select(C).ToArray()};
    var orb=S("Storm","Thunder","Defragment","Glacier","Coolheaded");
    foreach(var n in new[]{"Claw","AdaptiveStrike","BulkUp","Scrape","BeamCell"})Check(Score(orb,C(n))<0,"orb rejects physical "+n);
    var clawScore=policy.Evaluate(orb,[Pick(C("Claw"))]).Options[0];
    Check(clawScore.Parts.Count(p=>p.Reason=="球系主线不再投资物理组件")==1
        &&!clawScore.Parts.Any(p=>p.Reason=="当前球系方向与物理组件的机会成本"),"cross-engine incompatibility charged once");
    var claws=S("Claw","Claw","Claw","Hologram","AllForOne","Scrape");
    foreach(var n in new[]{"Defragment","Darkness","Storm","Capacitor"})Check(Score(claws,C(n))<0,"physical rejects orb "+n);
    Check(Score(claws,C("Claw"))>0,"fourth claw grows the shared damage engine");
    Check(Score(S("Claw","Claw","Claw","Claw","Claw","Hologram","AllForOne"),C("Claw"))<0,"sixth claw prefers support");
    Check(Score(S("AdaptiveStrike","AllForOne","Hologram"),C("Claw"))<0,"adaptive does not restart claws");
    Check(Score(claws,C("AdaptiveStrike"))>0,"claw engine can accept one adaptive");
    foreach(var st in new[]{orb,claws})
    {Check(Score(st,C("Compact"))>0,"shared status handling");Check(Score(st,C("Overclock"))>0,"shared draw");}
});
Test("Defect common filler gate and repeatable energy are contextual",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=2,Hp=60,MaxHp=75,Deck=[C("Hologram"),C("Turbo"),C("Skim"),C("MultiCast")],Relics=["RELIC.ICE_CREAM"]};
    foreach(var n in new[]{"BallLightning","SweepingBeam"})
    {
        Check(Score(s with{Act=1},C(n))>0,"early utility does not satisfy damage need "+n);
        Check(Score(s,C(n))<Score(s with{Act=1},C(n)),"transition value decays with phase without banning a needed first attack "+n);
    }
    Check(Score(s,C("GunkUp"))<0,"missing damage alone does not justify weak self-status attack");
    var engine=s with{Deck=[..s.Deck,C("Darkness"),C("ConsumingShadow"),C("Loop")]};
    foreach(var n in new[]{"BallLightning","SweepingBeam","Leap"})Check(Score(engine,C(n))<Score(s,C(n)),"existing engine reduces filler value without pretending all defense is solved "+n);
    Check(Score(s with{Deck=[..s.Deck,C("DoubleEnergy"),C("DoubleEnergy")]},C("DoubleEnergy"))>0,"third energy has actual draw and spend support");
    Check(Score(s with{Deck=[..s.Deck,..Enumerable.Repeat(C("DoubleEnergy"),4)]},C("DoubleEnergy"))<0,"energy is not unlimited");
    var gem=CardRegistry.All["HiddenGem"].Basic;
    Check(policy.Evaluate(s,[Pick(gem)]).Options[0] is {Supported:true,Score:>0},"neutral replay gem is modeled");
});
Test("Defect cheese growth and camping prioritize upgrades without sacrificing lethal protection",()=>
{
    var s=run with{Character="Defect",Act=1,Hp=51,MaxHp=75,Map=new(3,16,3,16,"Standard"),Deck=[DefectPolicy.Card("Hologram")!,DefectPolicy.Card("Coolheaded")!]};
    var vars=new Dictionary<string,double>{{"Damage",14}};
    var gorge=EventRules.Describe("ROOM_FULL_OF_CHEESE","GORGE","Cards",s,vars);
    var cheese=EventRules.Describe("ROOM_FULL_OF_CHEESE","SEARCH","Cheese",s,vars);
    Check(EventPolicy.Evaluate(s,[gorge,cheese]).RecommendedKey=="SEARCH","51 HP early cheese beats two forced commons");
    Check(EventPolicy.Evaluate(s with{Hp=14},[gorge,cheese]).RecommendedKey=="GORGE","cheese cannot kill player");
    var camp=s with{Hp=36,CampRouteRisk=new(){{"smith",100}}};
    Check(policy.Evaluate(camp,rest).RecommendedKey=="smith","coarse distant hazard cannot force rest at viable HP with engine upgrades");
    Check(policy.Evaluate(camp with{Hp=5},rest).RecommendedKey=="heal","critical HP still rests");
    Check(policy.Evaluate(camp with{Deck=camp.Deck.Select(c=>c with{CanUpgrade=false}).ToArray()},rest).RecommendedKey=="heal","no usable upgrade still heals");
});
Test("Defect early Ice Cream run invests beyond common filler",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Floor=4,Hp=37,MaxHp=77,Relics=["RELIC.ICE_CREAM"],
        Deck=[..Enumerable.Repeat(C("StrikeDefect"),5),..Enumerable.Repeat(C("DefendDefect"),5),C("Zap"),C("Dualcast"),C("LightningRod")],
        Map=new(3,16,4,16,"Standard")};
    var energyState=s with{Deck=[..s.Deck,C("SweepingBeam")],Hp=36,Floor=6};
    Check(Score(energyState,C("DoubleEnergy"))>Score(energyState,C("GoForTheEyes")),"first doubler plus Ice Cream outranks weak-attack filler");
    Check(!policy.Evaluate(energyState,[Pick(C("DoubleEnergy"))]).Options[0].Parts.Any(p=>p.Reason=="能量需要花费出口和过牌"),"generic energy penalty does not duplicate doubler-specific reasoning");
    Check(Score(s,C("HelixDrill"))>Score(s with{Relics=[]},C("HelixDrill")),"stored energy improves multi-hit payoff without universally overruling immediate group damage");
    Check(Score(s,CardRegistry.All["HiddenGem"].Basic)>0,"known colorless investment remains available without always outranking urgently needed group damage");
    var starter=s with{Deck=s.Deck.Where(c=>c.ClassName!="LightningRod").ToArray()};
    Check(Score(starter,C("Thunder"))>0,"first investment remains available alongside immediate transition options");
    var vars=new Dictionary<string,double>{{"RipHpLoss",5}};
    var a=EventRules.Describe("BRAIN_LEECH","RIP","Colorless",s,vars);
    var b=EventRules.Describe("BRAIN_LEECH","SHARE_KNOWLEDGE","Class cards",s,vars);
    Check(EventPolicy.Evaluate(s,[a,b]).RecommendedKey=="RIP","colorless opportunity can justify nonlethal health cost");
    Check(EventPolicy.Evaluate(s with{Hp=5},[a,b]).RecommendedKey=="SHARE_KNOWLEDGE","no promised random card overrides direct death");
});
Test("Defect colorless catalog explicitly covers solo mechanisms and excludes multiplayer",()=>
{
    var excluded="BeaconOfHope BelieveInYou Coordinate GangUp HuddleUp Intercept Knockdown Lift Mimic Rally TagTeam TheBall".Split(' ').ToHashSet();
    var pool=CardRegistry.All.Values.Where(d=>d.Pool=="ColorlessCardPool").ToArray();
    Check(pool.Length==65,"native colorless pool changed; review new cards");
    var s=run with{Character="Defect",Deck=[DefectPolicy.Card("Zap")!,DefectPolicy.Card("Dualcast")!]};
    foreach(var d in pool)
        foreach(var c in new[]{d.Basic,d.Upgraded})
            Check(policy.Evaluate(s,[Pick(c)]).Options[0].Supported==!excluded.Contains(c.ClassName),"explicit coverage: "+c.ClassName);
    Check(Score(s,CardRegistry.All["UltimateDefend"].Basic)<0&&Score(s,CardRegistry.All["UltimateStrike"].Basic)<0,"flat stats must not outrank functional cards");
    Check(Score(s,CardRegistry.All["MasterOfStrategy"].Basic)>0,"reliable draw should remain useful");
    Check(Score(s,CardRegistry.All["TheGambit"].Upgraded)<-20,"fatal downside cannot be valued as ordinary block");
    Check(Score(s with{Relics=["RELIC.SOZU"]},CardRegistry.All["Alchemize"].Basic)<0,"Sozu suppresses potion generation");
});
Test("Defect energy startup and power duplication have explicit support and saturation",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Energy=3,Act=2,Deck=[C("Storm"),C("Thunder"),C("Glacier"),C("DoubleEnergy"),C("SignalBoost")],Relics=["RELIC.ICE_CREAM"]};
    Check(Score(s,C("MeteorStrike"))>Score(s with{Relics=[]},C("MeteorStrike"))+8,"stored doubled energy is a possible startup channel");
    Check(Score(s with{Relics=[],Deck=[C("MeteorStrike")]},C("MeteorStrike"))<0,"Meteor's own plasma cannot pay its first cast");
    Check(Score(s,C("SignalBoost"))>0,"second copy has two real power targets");
    Check(Score(s with{Deck=[..s.Deck,C("SignalBoost"),C("SignalBoost")]},C("SignalBoost"))<0,"too many copies saturate real targets");
    Check(Score(s,C("Darkness"))<0,"existing Lightning engine should not start a Dark branch");
});
Test("Defect relics use character synergies and do not redirect orbs toward attack filler",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Deck=[C("Storm"),C("Thunder"),C("Glacier"),C("Defragment")],Relics=["RELIC.CRACKED_CORE"]};
    var physical=s with{Deck=[C("Claw"),C("Claw"),C("AllForOne"),C("Scrape")]};
    Check(RelicValue.Evaluate("TOUCH_OF_OROBAS",s,true).Value>10,"starter refinement counts Defect's core");
    Check(RelicValue.Evaluate("DATA_DISK",s).Value>RelicValue.Evaluate("DATA_DISK",physical).Value,"Focus relic recognizes orbs");
    Check(RelicValue.Evaluate("SHURIKEN",physical).Value>RelicValue.Evaluate("SHURIKEN",s).Value,"Strength relic recognizes attack throughput");
    Check(!DefectRelics.CardAdjustments(s with{Relics=["RELIC.KUNAI","RELIC.SHURIKEN"]},C("GoForTheEyes")).Any(),"attack relics alone must not justify physical filler");
    Check(Score(s with{Relics=["RELIC.MUMMIFIED_HAND"]},C("Storm"))>Score(s,C("Storm")),"owned relic changes actual card score");
});
Test("Defect Neow uses opening priorities and character-correct removal targets",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",PlayerCount=1,Hp=60,MaxHp=75,Act=1,
        Deck=[..Enumerable.Repeat(C("StrikeDefect"),4),..Enumerable.Repeat(C("DefendDefect"),4),C("Zap"),C("Dualcast")]};
    Check(NeowPolicy.Supports(s)&&!NeowPolicy.Supports(s with{PlayerCount=2}),"runtime gate must include singleplayer Defect");
    double N(string id)=>NeowPolicy.Evaluate(id,s).Value;
    Check(N("NEW_LEAF")>N("SCROLL_BOXES")&&N("SCROLL_BOXES")>N("LARGE_CAPSULE"),"observed opening must prefer transform, not extra basics");
    Check(N("PRECISE_SCISSORS")>N("WINGED_BOOTS")&&N("SILVER_CRUCIBLE")>N("NEW_LEAF"),"previous user opening tiers carry over");
    var cards=s.Deck.Select(c=>c with{Variables=new(c.Variables)}).ToArray();
    var plan=NeowPolicy.RemovalPlan(s,cards,2);
    Check(plan.Length==2&&cards[plan[0]].ClassName=="StrikeDefect"&&cards[plan[1]].ClassName=="DefendDefect","pair removes one Defect Strike and one Defend");
});
Test("Defect weak starting decks can trim redundant basics without deleting their last defenses",()=>
{
    CardSnapshot C(string n,bool up=false)=>DefectPolicy.Card(n,up)!;
    var s=run with{Character="Defect",Hp=60,MaxHp=75,Act=1,Deck=[..Enumerable.Repeat(C("StrikeDefect"),4),
        ..Enumerable.Repeat(C("DefendDefect"),3),C("DefendDefect",true),C("DefendDefect",true),C("Zap"),C("Dualcast"),C("Ftl")]};
    var v=new StrategicValue(s);
    var lights=new EventChoice("light","Light",Benefit:v.Upgrade(C("StrikeDefect"))+v.Upgrade(C("DefendDefect")));
    var dark=new EventChoice("dark","Dark",Remove:1,Selection:"remove");
    Check(EventPolicy.Evaluate(s,[lights,dark]).RecommendedKey=="dark","actual basic/basic upgrade forecast must lose to removal");
    var coreLight=lights with{Benefit=v.Upgrade(C("Hologram"))+v.Upgrade(C("Defragment"))};
    Check(EventPolicy.Evaluate(s,[coreLight,dark]).RecommendedKey=="light","do not replace this with unconditional removal");
    var strike=C("StrikeDefect");
    Check(DefectStrategy.Remove(s,strike)>0,"fresh UI snapshot should remove one matching slot");
    var shared=s with{Deck=[strike,strike,strike,strike,..s.Deck.Where(c=>c.ClassName!="StrikeDefect")]};
    var copied=shared with{Deck=shared.Deck.Select(c=>c with{Variables=new(c.Variables)}).ToArray()};
    Check(DefectStrategy.Remove(shared,strike)==DefectStrategy.Remove(copied,strike),"aliasing and deserialization must not change removal score");
    var sparse=s with{Deck=[C("StrikeDefect"),C("DefendDefect"),C("DefendDefect"),C("Zap"),C("Dualcast"),C("Turbo"),C("DoubleEnergy"),C("SignalBoost"),C("Overclock")]};
    Check(DefectStrategy.Remove(sparse,C("StrikeDefect"))<0&&DefectStrategy.Remove(sparse,C("DefendDefect"))<0,"preserve last reliable attack/block sources");
    Check(RelicValue.Evaluate("WHETSTONE",s).Value<8,"two random basic upgrades cannot get flat generic relic value");
    Check(RelicValue.Evaluate("WAR_PAINT",s with{Deck=s.Deck.Select(c=>c with{CanUpgrade=false}).ToArray()}).Value==0,"no eligible cards means no upgrade relic benefit");
});
Test("Defect physical direction permits frost defense and values targeted recovery",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Hp=60,MaxHp=75,Relics=["RELIC.CRACKED_CORE"],
        Deck=[..Enumerable.Repeat(C("StrikeDefect"),3),..Enumerable.Repeat(C("DefendDefect"),3),C("Zap"),C("Dualcast"),C("Claw")]};
    Check(Score(s,C("Thunder"))<0,"first Claw must suppress unsupported lightning terminal investment");
    var frost=s with{Deck=[..s.Deck,C("Glacier"),C("Coolheaded")]};
    Check(Score(frost,C("Claw"))>0,"frost defense must not become an orb damage commitment");
    Check(Score(s,C("Glacier"))>0,"physical decks can acquire frost defense");
    Check(RelicValue.Evaluate("DATA_DISK",frost).Value>RelicValue.Evaluate("DATA_DISK",s).Value,"Frost still benefits from Focus relics despite not selecting an orb damage direction");
    var committed=frost with{Deck=[..frost.Deck,C("Claw"),C("Feral")]};
    Check(Score(committed,C("Glasswork"))<0&&Score(committed,C("Hotfix"))<0,"established physical deck does not invest in glass damage or temporary focus");
    var mature=frost with{Act=3,Gold=486,Deck=[..frost.Deck,C("Claw"),C("Hologram"),C("BulkUp")]};
    var shop=DefectStrategy.Shop(mature,[new("feral",C("Feral"),38),new("hotfix",C("Hotfix"),51)],150,486,0,100);
    Check(shop.Recommendation?.Key=="feral","cheap Feral with repeatable zero attacks outranks temporary focus and basic removal");
    Check(Score(mature,C("Hyperbeam"))<0,"established claw deck avoids expensive competing damage");
    var orbs=s with{Deck=[C("Storm"),C("Thunder"),C("Defragment"),C("Glacier"),C("Coolheaded"),C("Zap"),C("Dualcast")]};
    Check(Score(orbs,C("Feral"))<0,"zero skills and Frost do not justify Feral");
});
Test("Defect Smokestack requires actual status engine density",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=3,Deck=[C("Claw"),C("Claw"),C("Glacier"),C("Coolheaded"),C("Turbo"),C("BoostAway"),C("Hologram")]};
    Check(Score(s,DefectPolicy.Card("Smokestack",true)!)<0,"two utility status sources do not justify upgraded Smokestack");
    var status=s with{Deck=[C("Turbo"),C("BoostAway"),C("Overclock"),C("GunkUp"),C("Compact"),C("FlakCannon")]};
    var parts=policy.Evaluate(status,[Pick(C("Smokestack"))]).Options[0].Parts;
    Check(!parts.Any(p=>p.Reason.StartsWith("烟囱需要高密度")),"true status-heavy engine is not blanket banned");
});
Test("Toasty Mittens and hole event value cleanup without treating Normality as free",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Deck=[..Enumerable.Repeat(C("StrikeDefect"),3),..Enumerable.Repeat(C("DefendDefect"),3),C("Zap"),C("Dualcast"),C("Claw"),C("Glacier"),C("Hologram")]};
    Check(RelicValue.Evaluate("TOASTY_MITTENS",s,true).Value>RelicValue.Evaluate("TOY_BOX",s,true).Value,"physical cleanup plus recurring Strength outranks generic toy value");
    var remove=EventRules.Describe("FIELD_OF_MAN_SIZED_HOLES","RESIST","Remove",s,new Dictionary<string,double>());
    var enchant=EventRules.Describe("FIELD_OF_MAN_SIZED_HOLES","ENTER_YOUR_HOLE","Enchant",s,new Dictionary<string,double>());
    Check(EventPolicy.Evaluate(s,[remove,enchant]).RecommendedKey=="RESIST","many redundant basics justify the curse handling plan");
    Check(remove.Burden>0,"curse remains a real cost");
    var mittens=EventRules.Describe("FIELD_OF_MAN_SIZED_HOLES","RESIST","Remove",s with{Relics=["RELIC.TOASTY_MITTENS"]},new Dictionary<string,double>());
    Check(mittens.Burden<remove.Burden,"reliable hand exhaust lowers the handling burden");
});
RunSnapshot EventRegression(string floor)
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-event-regression.json")));
    return doc.RootElement.GetProperty(floor).Deserialize<RunSnapshot>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
}
Test("Self Help Book actual Nimble candidates do not tie on base Defend",()=>
{
    var s=EventRegression("44");var v=new StrategicValue(s);
    var cards=s.Deck.Where(c=>c.ClassName is "DefendDefect" or "BoostAway" or "BootSequence").ToArray();
    foreach(var enchant in new[]{"NIMBLE","ADROIT"})
    {
        var best=cards.OrderByDescending(c=>v.Enchant(c,enchant,2)).First();
        Check(best.ClassName=="BoostAway","repeatable upgraded zero-cost defense must win actual target ranking: "+enchant);
        Check(v.Enchant(best,enchant,2)>v.Enchant(cards.Single(c=>c.ClassName=="DefendDefect"),enchant,2),"strictly better, not an order-dependent tie");
        Check(cards.Reverse().OrderByDescending(c=>v.Enchant(c,enchant,2)).First().ClassName=="BoostAway","candidate order must not alter this choice");
    }
    var choice=EventRules.Describe("SELF_HELP_BOOK","READ_PASSAGE","Nimble",s,new Dictionary<string,double>());
    Check(Math.Abs(choice.Benefit-cards.Max(c=>v.Enchant(c,"NIMBLE",2)))<1e-9,"event option must price the same target as execution");
    var lone=s with{Deck=[DefectPolicy.Card("DefendDefect")!]};
    Check(new StrategicValue(lone).BestEnchant("NIMBLE",2)>0,"a basic-only legal pool remains usable");
    Check(v.Enchant(DefectPolicy.Card("Zap")!,"NIMBLE",2)<0,"no block is ineligible for Nimble");
});
Test("Zap upgrade cannot crowd out the observed functional upgrade",()=>
{
    var s=EventRegression("8");
    var best=s.Deck.Where(c=>c.CanUpgrade).OrderByDescending(c=>DefectStrategy.Upgrade(s,c)).First();
    Check(best.ClassName=="Modded","actual floor eight should upgrade Modded instead of Zap or Dualcast");
    var dark=s with{Deck=[..s.Deck,DefectPolicy.Card("Darkness")!]};
    Check(DefectStrategy.Upgrade(dark,DefectPolicy.Card("Dualcast")!)>DefectStrategy.Upgrade(s,DefectPolicy.Card("Dualcast")!),"Dark decks retain Dualcast upgrade value");
});
Test("Enthralled is not priced as an ordinary removable curse",()=>
{
    var s=EventRegression("34");
    double Rose(RunSnapshot state)=>RelicValue.Evaluate("BLOOD_SOAKED_ROSE",state,true).Value;
    Check(Rose(s)<RelicValue.Evaluate("LORDS_PARASOL",s,true).Value,"observed small deck must not choose Rose over Parasol");
    var hand=s with{Deck=[..s.Deck,DefectPolicy.Card("Scavenge")!,DefectPolicy.Card("Compact")!]};
    Check(Rose(hand)<0,"hand exhaust and status cleanup do not solve a locked hand");
    Check(Rose(s with{Relics=[..s.Relics,"RELIC.TOASTY_MITTENS"]})>Rose(s)+10,"automatic start-of-turn exhaust is a verified exception");
    Check(new StrategicValue(s).EnthralledCost()>new StrategicValue(s with{Deck=[..s.Deck,..s.Deck]}).EnthralledCost(),"small decks re-encounter the curse more often");
});
Test("First Echo Form can be invested in before an existing Power engine",()=>
{
    var s=EventRegression("17");var echo=DefectPolicy.Card("EchoForm")!;
    Check(Score(s,echo)>Score(s,DefectPolicy.Card("IceLance")!),"actual boss reward should invest in its first long-fight engine");
    var duplicate=s with{Deck=[..s.Deck,echo]};
    Check(!policy.Evaluate(duplicate,[Pick(echo)]).Options[0].Parts.Any(p=>p.Reason.StartsWith("缺少长战引擎")),"not a duplicate-power bonus");
    var low=s with{Hp=8};
    Check(Score(s,echo)>Score(low,echo),"immediate survival still limits speculative investment");
});
RunSnapshot LongFightRegression(string floor)
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-long-fight-regression.json")));
    return doc.RootElement.GetProperty(floor).Deserialize<RunSnapshot>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
}
Test("Early Storm investment beats weak filler without crossing a physical commitment",()=>
{
    var s=LongFightRegression("3");var storm=DefectPolicy.Card("Storm")!;
    Check(Score(s,storm)>Score(s,DefectPolicy.Card("Synthesis")!),"actual early Storm should outrank weak Synthesis");
    var physical=s with{Deck=[..s.Deck,DefectPolicy.Card("Claw")!]};
    Check(Score(physical,storm)>Score(physical with{Deck=[..physical.Deck,DefectPolicy.Card("Claw")!,DefectPolicy.Card("Feral")!]},storm),"one Claw is tentative; supported Claws impose a stronger switching cost");
    Check(!policy.Evaluate(physical,[Pick(storm)]).Options[0].Parts.Any(p=>p.Reason.StartsWith("前期首张雷暴")),"physical commitment receives no early Storm bonus");
});
Test("Frost quantity is not Hailstorm damage scaling",()=>
{
    var s=LongFightRegression("36");
    Check(Score(s,DefectPolicy.Card("Hailstorm")!)<0,"observed late Hailstorm should not be preferred over skip");
    var p=policy.Evaluate(s,[Pick(DefectPolicy.Card("Hailstorm")!)]).Options[0];
    Check(!p.Parts.Any(x=>x.Reason=="已有组件形成方向，保留有限未来投资"&&x.Value>0),"generic orb affinity must not inflate flat Hailstorm damage");
});
Test("Boss growth requires a damage chain rather than accumulated defensive components",()=>
{
    var s=LongFightRegression("47");
    CardSnapshot C(string n,bool up=false)=>DefectPolicy.Card(n,up)!;
    Check(DeckReadiness.Read(s).Growth<.65,"actual unfocused deck must not be near maximum long-fight growth");
    var frost=s with{Deck=[C("Glacier"),C("Glacier"),C("Coolheaded",true),C("Defragment",true),C("Hotfix"),C("Capacitor"),C("Loop")],Relics=[]};
    Check(DeckReadiness.Read(frost).Growth==0,"frost/focus/block without a damage route is not damage growth");
    var dark=frost with{Deck=[..frost.Deck,C("Darkness",true),C("Darkness",true),C("Dualcast",true),C("Hologram",true)]};
    Check(DeckReadiness.Read(dark).Growth>DeckReadiness.Read(s).Growth,"actual Dark generation/passive/evoke chain beats unrelated component count");
    var claw=frost with{Deck=[C("Claw"),C("Claw"),C("Claw"),C("Feral"),C("AllForOne"),C("Hologram",true),C("Overclock")]};
    var status=frost with{Deck=[C("FlakCannon"),C("Compact"),C("Iteration"),C("Turbo"),C("Overclock"),C("FightThrough")]};
    Check(DeckReadiness.Read(claw).Growth>.5&&DeckReadiness.Read(status).Growth>.5,"physical and status engines retain their separate route to long-fight output");
});
RunSnapshot FeedbackRegression(string floor)
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-feedback-regression.json")));
    return doc.RootElement.GetProperty(floor).GetProperty("state").Deserialize<RunSnapshot>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
}
Test("Iteration compensates real self-created statuses without pretending to remove them",()=>
{
    var s=FeedbackRegression("13");var c=DefectPolicy.Card("Iteration")!;
    Check(Score(s,c)>0,"first Iteration with Turbo and Overclock should be worth taking");
    var noSources=s with{Deck=s.Deck.Where(x=>x.ClassName is not ("Turbo" or "Overclock")).ToArray()};
    Check(Score(s,c)>Score(noSources,c),"own status sources must matter");
    Check(!DefectPolicy.Tag(c,"status_clear"),"Iteration draws, does not remove status cards");
});
Test("weak duplicate resources do not win against skip by a fraction of a point",()=>
{
    Check(Score(FeedbackRegression("24"),DefectPolicy.Card("Turbo")!)<0,"actual second Turbo was only 0.05");
    Check(Score(FeedbackRegression("45"),DefectPolicy.Card("Hotfix")!)<0,"actual second Hotfix was only 0.45");
});
Test("unupgraded Coolheaded is utility rather than an extra orb engine",()=>
{
    var s=FeedbackRegression("31");var c=DefectPolicy.Card("Coolheaded")!;
    Check(Score(s,c)<Score(s,DefectPolicy.Card("Rainbow")!),"actual Rainbow has more marginal value than unupgraded Coolheaded");
    Check(Score(s,DefectPolicy.Card("Coolheaded",true)!)>Score(s,c)+2,"upgraded real net draw retains its value");
    var shop=FeedbackRegression("6");
    Check(ShopPolicy.Evaluate(shop,[new("cool",c,48)],100,shop.Gold).Recommendation?.Kind=="remove","133 gold must not spend on utility instead of removal");
});
Test("first Supercritical supports the recorded expensive power startup",()=>
{
    var s=FeedbackRegression("39");
    Check(Score(s,DefectPolicy.Card("Supercritical")!)>Score(s,DefectPolicy.Card("SignalBoost")!),"actual startup need should precede an additional copy effect");
    var noPowers=s with{Deck=s.Deck.Where(c=>c.Type!="Power").ToArray()};
    var eval=policy.Evaluate(noPowers,[Pick(DefectPolicy.Card("Supercritical")!)]);
    Check(!eval.Options[0].Parts.Any(p=>p.Reason.StartsWith("首张超临界态帮助高费能力")),"bonus requires actual expensive powers");
});
Test("verified post-boss recovery affects acquisition only and never rewrites current HP",()=>
{
    var s=FeedbackRegression("33");var projected=s with{HpAfterPendingAncient=60};
    Check(s.Hp==2&&projected.Hp==2,"actual HP remains untouched");
    Check(Score(projected,DefectPolicy.Card("EchoForm")!)>Score(projected,DefectPolicy.Card("Spinner")!),"invest in Echo at verified recovery boundary");
    Check(Score(projected,DefectPolicy.Card("EchoForm")!)>Score(s,DefectPolicy.Card("EchoForm")!),"ordinary low HP without verified recovery retains caution");
    Check(Score(s with{HpAfterPendingAncient=999},DefectPolicy.Card("EchoForm")!)==Score(s,DefectPolicy.Card("EchoForm")!),"invalid recovery data cannot inflate valuation");
});
Test("Compact repeat requires high actual status burden rather than generic insurance",()=>
{
    CardSnapshot C(string n,bool u=false)=>DefectPolicy.Card(n,u)!;
    var s=run with{Character="Defect",Act=1,Hp=39,MaxHp=77,Deck=[C("Turbo"),C("Overclock")]};
    Check(Score(s,C("Compact"))>0,"first Compact remains useful");
    foreach(var upgraded in new[]{false,true})foreach(var hp in new[]{5,39,77})
    {
        var covered=s with{Hp=hp,Deck=[..s.Deck,C("Compact",upgraded)]};
        foreach(var offeredUpgrade in new[]{false,true})
            Check(Score(covered,C("Compact",offeredUpgrade))<0,"second Compact loses to skip regardless of upgrade and urgency");
    }
    var heavy=s with{Deck=[..s.Deck,C("Compact"),C("FightThrough"),C("GunkUp")]};
    Check(Score(heavy,C("Compact"))>0,"heavy generated statuses may justify second Compact");
    Check(Score(heavy with{Deck=[..heavy.Deck,C("Compact")]},C("Compact"))<0,"do not extend to third copy");
    Check(Score(heavy with{Deck=[..heavy.Deck,C("FlakCannon")]},C("Compact"))<0,"existing alternative clearance raises second-copy threshold");
});
Test("Output investment respects established physical versus orb direction",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Hp=60,MaxHp=75,Deck=[C("Hologram"),C("Turbo"),C("Leap")]};
    var parts=policy.Evaluate(s,[Pick(C("Storm"))]).Options[0].Parts;
    Check(parts.Any(p=>p.Reason.StartsWith("尚缺长战输出主干")),"utility alone cannot mark damage engine complete");
    Check(Score(s with{Deck=[..s.Deck,C("Claw"),C("Claw")]},C("Storm"))<0,"physical deck does not start orb engine");
    Check(Score(s with{Deck=[..s.Deck,C("Storm"),C("Thunder"),C("Defragment")]},C("Claw"))<0,"orb deck does not start Claw");
});
Test("Actual Compact and first-boss Voltaic choices retain contextual exceptions",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-live-0638.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var row in doc.RootElement.EnumerateArray())
    {
        var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var choices=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
        var result=policy.Evaluate(s,choices);
        var best=choices.Single(c=>c.Key==result.RecommendedKey);
        if(s.Floor==13)Check(best.Kind=="skip","recorded second Compact should skip");
        else
        {
            Check(best.Card?.ClassName=="Voltaic","recorded first-boss choice invests in missing damage");
            var v=DefectPolicy.Card("Voltaic")!;
            Check(DefectStrategy.Upgrade(s,v)>DefectStrategy.Upgrade(s,DefectPolicy.Card("Zap")!),"follow acquisition with upgrade priority");
            var unsupported=s with{Deck=s.Deck.Where(c=>c.ClassName!="Zap").ToArray(),Relics=[]};
            Check(Score(unsupported,v)<0,"no Lightning seed is not a working terminal");
            var physical=s with{Deck=[..s.Deck,DefectPolicy.Card("Claw")!,DefectPolicy.Card("AllForOne")!]};
            Check(Score(physical,v)<0,"do not switch established physical deck to Voltaic");
        }
    }
});
Test("Actual merchant Thunder completes upgraded Voltaic rather than buying generic Loop",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-shop-0638.json")));
    var s=doc.RootElement.GetProperty("state").Deserialize<RunSnapshot>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    Check(s.Gold==103,"captured pre-purchase budget");
    var stock=new[]{new ShopCard("thunder",DefectPolicy.Card("Thunder")!,72),new ShopCard("barrage",DefectPolicy.Card("Barrage")!,50),new ShopCard("charge",DefectPolicy.Card("ChargeBattery")!,25)};
    Check(ShopPolicy.Evaluate(s,stock,null,103).Recommendation?.Key=="thunder","affordable synergistic Thunder may use reserve");
    Check(Score(s,DefectPolicy.Card("Loop")!)<0,"orb deck alone does not justify Loop");
    var bare=s with{Deck=s.Deck.Where(c=>c.ClassName!="Voltaic").ToArray()};
    Check(Score(s,DefectPolicy.Card("Thunder")!)>Score(bare,DefectPolicy.Card("Thunder")!),"specific owned amplifier accounts for extra value");
    Check(policy.Evaluate(s,[Pick(DefectPolicy.Card("Thunder")!)]).Options[0].Parts.Any(p=>p.Reason.StartsWith("已有电流相生批量")&&p.Value>0),"owned amplifier has an explicit contribution");
    Check(ShopPolicy.Evaluate(s with{Gold=71},stock,null,71).Recommendation?.Key!="thunder","cannot buy unaffordable combo");
});
Test("Concrete engine synergy survives generic cap without rewarding filler or duplicate cores",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=2,Hp=60,MaxHp=75,Deck=[DefectPolicy.Card("Voltaic",true)!,C("Storm"),C("Subroutine"),C("Defragment"),C("BallLightning"),C("Glacier"),C("Turbo")]};
    double Restored(RunSnapshot state,string n)=>policy.Evaluate(state,[Pick(C(n))]).Options[0].Parts.Where(p=>p.Reason.StartsWith("明确核心搭档")).Sum(p=>p.Value);
    Check(Restored(s,"Thunder")>0,"actual power/electric synergy must survive cap");
    Check(Score(s,C("Thunder"))>Score(s,C("ChargeBattery")),"output terminal beats another ordinary block");
    Check(Restored(s,"ChargeBattery")==0,"generic tags cannot claim core budget");
    Check(Restored(s with{Deck=[..s.Deck,C("Thunder")]},"Thunder")==0,"duplicate not given first-core budget");
    Check(Restored(s with{Deck=[C("Glacier"),C("Turbo")]},"Thunder")==0,"removing partners removes budget");
    Check(Score(s,C("SweepingBeam"))<Score(s with{Act=2,Deck=[C("Hologram"),C("Turbo"),C("Leap")]},C("SweepingBeam")),"existing damage engine lowers marginal need for bridge damage");
});
Test("Defect engine relic shop priority checks partners, alternatives and affordability",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=2,Hp=60,MaxHp=75,Gold=200,Deck=[C("Storm"),C("Thunder"),C("EchoForm"),C("Defragment"),C("Glacier"),C("Turbo")]};
    var hand=new ShopRelic("hand","MUMMIFIED_HAND","hand",180);
    Check(PriorityShopPolicy.Evaluate(s,[hand],[new("filler",C("ChargeBattery"),50)])?.Relic.Key=="hand","save budget for actual engine relic");
    Check(PriorityShopPolicy.Evaluate(s with{Gold=179},[hand],[])==null,"cannot spend missing gold");
    var competing=s with{Deck=[C("Storm"),C("Thunder"),C("EchoForm"),C("Glacier"),C("BallLightning"),C("LightningRod")]};
    Check(PriorityShopPolicy.Evaluate(competing,[hand with{Price=200}],[new("focus",C("Defragment"),0)])==null,"higher-value card prevents engine relic priority");
    Check(PriorityShopPolicy.Evaluate(s with{Deck=[C("Claw"),C("AllForOne"),C("Hologram")]},[hand],[])==null,"physical deck without powers cannot claim hand synergy");
    Check(PriorityShopPolicy.Evaluate(s,[hand],[new("echo",C("EchoForm") with{Enchantment="ENCHANTMENT.UNKNOWN"},50)])?.Relic.Key=="hand","unsupported card does not invent alternative value");
});
Test("Actual boss offers distinguish tentative components from damage engines",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","defect-damage-plan-0639.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var row in doc.RootElement.EnumerateArray())
    {
        var s=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var options=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
        var result=policy.Evaluate(s,options);
        Check(options.Single(o=>o.Key==result.RecommendedKey).Card?.ClassName==row.GetProperty("expected").GetString(),row.GetProperty("label").GetString()!);
    }
});
Test("Adaptive Strike investment uses normal copies and respects established orbs",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Deck=[C("Glacier"),C("Coolheaded"),C("Hologram"),C("Turbo"),C("Scavenge")]};
    Check(Score(s,C("AdaptiveStrike"))>Score(s,C("Reboot")),"Frost defense and utility should welcome damage core");
    foreach(var n in new[]{"Ftl","Sunder","Null"})Check(Score(s,C("AdaptiveStrike"))>Score(s,C(n)),"prefer core to bridge "+n);
    var established=s with{Deck=[..s.Deck,C("Storm"),C("Thunder"),C("Defragment")]};
    Check(Score(established,C("AdaptiveStrike"))<0,"do not abandon established electric damage");
    var dark=s with{Deck=[..s.Deck,C("ConsumingShadow"),C("Null")]};
    Check(Score(dark,C("AdaptiveStrike"))<0,"dark source plus actual seed remains a damage direction");
    var tentative=s with{Deck=[C("Spinner"),C("ChargeBattery")]};
    Check(Score(tentative,C("GoForTheEyes"))<Score(tentative,C("AdaptiveStrike")),"tentative orbs permit options but output core outranks weak support");
    Check(Score(tentative,C("Scrape"))<0,"draw without zero-cost targets is not a missing damage core");
    Check(Score(s with{Deck=[..s.Deck,C("AdaptiveStrike")]},C("AdaptiveStrike"))<Score(s,C("AdaptiveStrike"))-5,"first core investment cannot reward duplicates");
});
Test("Bridge attack budget accounts for role overlap without banning usable partners",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=3,Deck=[C("SweepingBeam"),C("FocusedStrike"),C("Ftl"),C("Reboot"),C("Hologram"),C("Turbo")]};
    Check(Score(s,C("Sunder"))<0,"late attack pile cannot keep buying bridge damage");
    Check(Score(s,C("Null"))<0,"late new dark branch must justify itself");
    var physical=s with{Deck=[C("AdaptiveStrike"),C("AllForOne"),C("Turbo")]};
    var dark=s with{Deck=[C("Darkness"),C("Defragment"),C("MultiCast")]};
    bool Budget(RunSnapshot x,string n)=>policy.Evaluate(x,[Pick(C(n))]).Options[0].Parts.Any(p=>p.Reason.StartsWith("过渡攻击共用"));
    Check(!Budget(physical,"Ftl"),"retrievable FTL is a concrete physical partner");
    Check(!Budget(dark,"Null"),"Null can seed an existing dark engine");
    Check(Score(s with{Act=1,Deck=[C("StrikeDefect"),C("DefendDefect")]},C("Sunder"))>0,"first early bridge still allowed");
});
Test("All for One completion requires reusable retrieval, energy and damage targets",()=>
{
    CardSnapshot C(string n,bool up=false)=>DefectPolicy.Card(n,up)!;
    var s=run with{Character="Defect",Act=1,Deck=[C("Hologram",true),C("Turbo"),C("Ftl")]};
    double Completion(RunSnapshot x)=>policy.Evaluate(x,[Pick(C("AllForOne"))]).Options[0].Parts.Where(p=>p.Reason.StartsWith("这张牌补齐")).Sum(p=>p.Value);
    Check(Completion(s)>0,"owned three pieces make a concrete loop investment");
    foreach(var missing in new[]{"Hologram","Turbo","Ftl"})Check(Completion(s with{Deck=s.Deck.Where(c=>c.ClassName!=missing).ToArray()})==0,"missing "+missing+" cannot invent completion");
    Check(Completion(s with{Deck=[C("Hologram"),C("Turbo"),C("Ftl")]})==0,"exhausting Hologram is not repeatable");
});
Test("normal fight losses reach route risk without inventing another act's danger",()=>
{
    var graph=new[]{new RouteNode("fight","Monster",[]),new RouteNode("camp","RestSite",[])};
    var s=run with{Hp=24,Act=2,RecentBattles=[]};
    var baseline=RoutePolicy.Evaluate(s,graph,["fight","camp"],new());
    var observed=s with{RecentBattles=[new(2,"Monster",null,35,25,1,80)]};
    var adjusted=RoutePolicy.Evaluate(observed,graph,["fight","camp"],new());
    Check(adjusted.Options.Single(o=>o.Node=="fight").RiskPenalty>baseline.Options.Single(o=>o.Node=="fight").RiskPenalty,
        "heavy ordinary damage must increase danger, even if potion/healing hid net loss");
    Check(adjusted.RecommendedNode=="camp","recovery preferred to observed ordinary combat danger");
    var oldAct=RoutePolicy.Evaluate(s with{RecentBattles=[new(1,"Monster",null,70,0,0,80)]},graph,["fight","camp"],new());
    Check(oldAct.Options.Single(o=>o.Node=="fight").Score==baseline.Options.Single(o=>o.Node=="fight").Score,
        "act one damage is not attributed to act two encounters");
});
Test("early Defect elite risk respects missing engine and observed damage",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Hp=60,MaxHp=80,Deck=[C("StrikeDefect"),C("DefendDefect"),C("Zap"),C("Dualcast"),C("Hologram"),C("DoubleEnergy")]};
    var graph=new[]{new RouteNode("elite","Elite",[]),new RouteNode("camp","RestSite",[])};
    var plan=RoutePolicy.Evaluate(s,graph,["elite","camp"],new());
    Check(plan.RecommendedNode=="camp","tools without an engine prefer preparation to elite");
    Check(plan.Options.Single(o=>o.Node=="elite").ProjectedHp<=30,"unformed deck budgets at least 30 elite damage before upgrades");
    var hurt=RoutePolicy.Evaluate(s with{RecentBattles=[new(1,"Elite",null,50,30,1,80)]},graph,["elite","camp"],new());
    Check(hurt.Options.Single(o=>o.Node=="elite").ProjectedHp<=15,"recent heavy damage keeps 90 percent risk despite healing");
    var forced=RoutePolicy.Evaluate(s,graph,["elite"],new());
    Check(forced.RecommendedNode=="elite","legal mandatory elite remains traversable");
    var longMap=s with{Floor=100};
    Check(RoutePolicy.Evaluate(longMap,graph,["elite","camp"],new()).Options[0].Score==plan.Options[0].Score,"extra floors do not turn first act into later act");
});
Test("foresight invests in cores without merging exclusive rewards or mixing archetypes",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Hp=60,MaxHp=75,Deck=[C("StrikeDefect"),C("DefendDefect"),C("Zap"),C("Hologram")]};
    RewardBranch B(params string[] names)=>new("test",[new("2,3","Monster",1,.85,names.Select(C).ToArray())]);
    RewardForesight F(params RewardBranch[] branches)=>new("test",true,branches,"conditional test");
    double Bonus(RunSnapshot x,string n)=>DefectForesight.Adjustments(x,C(n)).Sum(p=>p.Value);
    Check(Bonus(s,"AdaptiveStrike")==0,"absent forecast cannot invent a future core");
    var future=s with{Foresight=F(B("AllForOne","Scrape","Hologram"))};
    Check(Bonus(future,"AdaptiveStrike")>=8,"actual complementary preview makes first damage core a material investment");
    Check(Bonus(future,"AdaptiveStrike")==Bonus(s with{Foresight=F(B("AllForOne"))},"AdaptiveStrike"),"three options in one reward are not three owned cards");
    Check(Bonus(s with{Foresight=F(B("AllForOne"),B("Scrape"))},"AdaptiveStrike")==Bonus(future,"AdaptiveStrike"),"mutually exclusive route branches do not add together");
    Check(Bonus(future with{Foresight=future.Foresight! with{SourceUnchanged=false}},"AdaptiveStrike")==0,"invalidated state cannot affect scoring");
    Check(Bonus(future with{Deck=[C("Storm"),C("Thunder"),C("Defragment")]},"AdaptiveStrike")==0,"foresight cannot force physical into committed orb deck");
    Check(Bonus(future with{Deck=[..s.Deck,C("AdaptiveStrike")]},"AdaptiveStrike")==0,"future supports do not reward redundant core copies");
    Check(Bonus(future,"DefendDefect")==0,"generic filler receives no foresight bonus");
    Check(DeckReadiness.Read(s)==DeckReadiness.Read(future),"future rewards never inflate present combat readiness");
});
Test("free card selection completes an available damage engine before more utility",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Hp=60,MaxHp=75,Deck=[C("StrikeDefect"),C("DefendDefect"),C("Zap"),C("Dualcast"),C("Hologram"),C("Turbo"),C("DoubleEnergy")]};
    var result=policy.Evaluate(s,[new("core","AdaptiveStrike","card",Card:C("AdaptiveStrike")),new("tool","Coolheaded","card",Card:C("Coolheaded")),new("skip","skip","skip")]);
    Check(result.RecommendedKey=="core","existing support can make Adaptive Strike the first real engine");
    var alone=policy.Evaluate(s,[new("core","AdaptiveStrike","card",Card:C("AdaptiveStrike"))]);
    Check(!alone.Options[0].Parts.Any(p=>p.Reason.StartsWith("现有组件已齐")),"completion priority does not alter standalone shop/card quotes");
    var noSupport=policy.Evaluate(s with{Deck=[C("StrikeDefect"),C("DefendDefect")]},[new("core","AdaptiveStrike","card",Card:C("AdaptiveStrike")),new("skip","skip","skip")]);
    Check(!noSupport.Options[0].Parts.Any(p=>p.Reason.StartsWith("现有组件已齐")),"completion requires owned support, not an imagined deck");
});
Test("shared potion replacement respects tiers defense and merchant opportunities",()=>
{
    foreach(var character in new[]{"Ironclad","Defect","Silent"})
    {
        var s=run with{Character=character};
        var fire=new PotionCandidate("FirePotion","Common");var block=new PotionCandidate("BlockPotion","Common");
        var rare=new PotionCandidate("FairyInABottle","Rare");var rainbow=new PotionCandidate("EntropicBrew","Rare");var foul=new PotionCandidate("FoulPotion","Event");
        Check(PotionPolicy.Replacement(s,[fire],block)==0,"same tier defense wins");
        Check(PotionPolicy.Replacement(s,[block],fire)==-1,"no reverse churn");
        Check(PotionPolicy.Replacement(s,[block],rare)==0,"higher tier precedes defense preference");
        Check(PotionPolicy.Replacement(s,[rare],rainbow)==-1,"revival and refill tie at highest priority");
        Check(PotionPolicy.Replacement(s,[rainbow],rare)==-1,"retain rainbow");
        var shop=s with{Commerce=new(1,1,0,0)};
        Check(PotionPolicy.Replacement(shop,[rare],foul)==-1,"foul cannot displace revival protection");
        Check(PotionPolicy.Replacement(shop,[rainbow],foul)==-1,"top priority ties preserve existing potion");
        Check(PotionPolicy.Replacement(shop with{Relics=["RELIC.ECTOPLASM"]},[rare],foul)==-1,"ectoplasm prevents merchant money priority");
        Check(PotionPolicy.Replacement(s,[fire],foul)==-1,"no shop evidence no foul replacement");
        Check(PotionPolicy.Replacement(s,[foul],block)==0,"stranded foul frees a useful combat slot");
        Check(PotionPolicy.Replacement(s,[new("Custom","Common",false)],rare)==-1,"unknown inventory protected");
        Check(PotionPolicy.Replacement(s with{Relics=["RELIC.SOZU"]},[fire],rare)==-1,"Sozu cannot discard stock for unobtainable reward");
    }
});
Test("Ironclad shop retains modeled enchanted offers",()=>
{
    var s=run with{Character="Ironclad",Gold=300};
    var card=CardRegistry.All["Offering"].Basic with{Modified=true,Enchantment="ENCHANTMENT.SWIFT",EnchantmentAmount=2};
    Check(ShopPolicy.Evaluate(s,[new("offering",card,25)],null,300).Recommendation?.Key=="offering","modeled modified card receives a recommendation");
    Check(ShopPolicy.Evaluate(s,[new("unknown",card with{Enchantment="ENCHANTMENT.CUSTOM"},25)],null,300).Recommendation==null,"unknown enchantment still does not authorize a purchase");
});
Test("shared potion purchase values purpose and final boss preparation",()=>
{
    foreach(var ch in new[]{"Ironclad","Defect"})
    {
        var s=run with{Character=ch,Act=3,Gold=200,PotionSlots=[null,null],Map=new(3,15,12,15,"test"),NextNodeTypes=["Boss"]};
        Check(PotionPolicy.PurchaseScore(s,new("ExplosiveAmpoule","Common"),50)<0,"do not pay for tiny final-boss damage");
        Check(PotionPolicy.PurchaseScore(s,new("FairyInABottle","Rare"),100)>PotionPolicy.PurchaseScore(s,new("BlockPotion","Common"),25),"revival precedes cheap refill");
        Check(PotionPolicy.PurchaseScore(s,new("EntropicBrew","Rare"),100)>PotionPolicy.PurchaseScore(s,new("BlockPotion","Common"),25),"refill precedes cheap refill");
        Check(PotionPolicy.PurchaseScore(s with{PotionSlots=["POTION.FIRE_POTION"]},new("BlockPotion","Common"),20)<0,"no purchased potion without a free slot");
        Check(PotionPolicy.Replacement(s,[new("GhostInAJar","Rare")],new("FairyInABottle","Rare"))==0,"revival exceeds ordinary rare defense");
        Check(PotionPolicy.Replacement(s with{Commerce=new(1,1,0,0)},[new("FirePotion","Common")],new("FoulPotion","Event"))==0,"foul still useful with reachable merchant");
        Check(RelicValue.Shop("BAG_OF_PREPARATION",s)!=null,"shared relic recommendation supports both characters");
        Check(RelicValue.Shop("CUSTOM_UNKNOWN",s)==null,"unknown relic is not mislabeled as modeled");
    }
    var physical=run with{Character="Defect",Deck=[DefectPolicy.Card("Claw")!,DefectPolicy.Card("AllForOne")!]};
    var orb=physical with{Deck=[DefectPolicy.Card("Darkness")!,DefectPolicy.Card("MultiCast")!]};
    Check(PotionPolicy.Evaluate(orb,new("FocusPotion","Common"))!.Value> PotionPolicy.Evaluate(physical,new("FocusPotion","Common"))!.Value,"focus depends on orb sources");
});
Test("established dark engine does not invest in an unrelated lightning branch",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=3,Deck=[C("Darkness"),C("MultiCast"),C("Hologram"),C("Coolheaded")]};
    foreach(var n in new[]{"Storm","Thunder"})
    {
        var result=policy.Evaluate(s,[new("card",n,"card",Card:C(n)),new("skip","skip","skip")]);
        Check(result.Options[0].Parts.Any(p=>p.Reason.StartsWith("黑球蓄积")),"dark branch penalty applied");
        var f=s with{Foresight=new("x",true,[new("p",[new("n","Monster",1,1,[C("Voltaic")])])],"test")};
        Check(!DefectForesight.Adjustments(f,C(n)).Any(),"future lightning cannot cancel dark commitment");
        var lightning=policy.Evaluate(s with{Deck=[C("Storm"),C("Voltaic")]},[new("card",n,"card",Card:C(n))]);
        Check(!lightning.Options[0].Parts.Any(p=>p.Reason.StartsWith("黑球蓄积")),"lightning synergy retained");
    }
});
Test("foresight changes acquisition for reverse dark assembly across choice sources",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Hp=60,MaxHp=75,Gold=200,Deck=[C("StrikeDefect"),C("DefendDefect"),C("Zap"),C("Dualcast")]};
    var f=new RewardForesight("test",true,[new("p",[new("1","Monster",1,.85,[C("Darkness")])])],"test");
    var options=new[]{new DecisionOption("core","MultiCast","card",Card:C("MultiCast")),new("tool","Leap","card",Card:C("Leap")),Skip()};
    var before=policy.Evaluate(s,options);var after=policy.Evaluate(s with{Foresight=f},options);
    Check(before.RecommendedKey!="core"&&after.RecommendedKey=="core","future Darkness must actually change the current choice to Multi-Cast");
    var shop=ShopPolicy.Evaluate(s with{Foresight=f},[new("core",C("MultiCast"),30)],null,200);
    Check(shop.Recommendation?.Key=="core"&&shop.Choices[0].Reasons.Any(r=>r.Contains("辉眼")),"shop applies and explains the same investment after price cost");
    Check(new StrategicValue(s with{Foresight=f}).Add(C("MultiCast"))>new StrategicValue(s).Add(C("MultiCast"))+8,"event and ancient card valuation use the same future evidence");
    Check(DeckReadiness.Read(s)==DeckReadiness.Read(s with{Foresight=f}),"future plans never improve current combat readiness");
});
Test("foresight shop plans respect total cash and invalidated evidence",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Gold=100,Deck=[C("StrikeDefect"),C("DefendDefect")]};
    var f=new RewardForesight("test",true,[new("p",[new("a","Shop",1,1,[C("Overclock")],[80]),new("b","Shop",1,1,[C("Turbo")],[80])])],"test");
    var low=DefectBuildForecast.Evaluate(s with{Foresight=f},C("FlakCannon"));
    var high=DefectBuildForecast.Evaluate(s with{Gold=200,Foresight=f},C("FlakCannon"));
    Check(low.Value>0&&high.Value>low.Value,"two shops cannot each spend the same gold");
    Check(DefectBuildForecast.Evaluate(s with{Gold=0,Foresight=f},C("FlakCannon")).Value==0,"unaffordable stock supplies no investment bonus");
    var unknownPrice=f with{Branches=[new("p",[new("shop","Shop",1,1,[C("Overclock")],[])])]};
    Check(DefectBuildForecast.Evaluate(s with{Foresight=unknownPrice},C("FlakCannon")).Value==0,"missing shop prices never imply free goods");
    Check(DefectBuildForecast.Evaluate(s with{Foresight=f with{SourceUnchanged=false}},C("FlakCannon")).Value==0,"stale evidence cannot guide a build");
});
Test("future pages and routes cannot be combined into imaginary packages",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Deck=[C("StrikeDefect")]};
    RewardBranch B(params CardSnapshot[] cards)=>new("p",[new("a","Monster",1,1,cards)]);
    double V(params RewardBranch[] branches)=>DefectBuildForecast.Evaluate(s with{Foresight=new("f",true,branches,"test")},C("FlakCannon")).Value;
    Check(V(B(C("Overclock"),C("Turbo")))==V(B(C("Overclock"))),"one page supplies only one card");
    Check(V(B(C("Overclock")),B(C("Turbo")))==V(B(C("Overclock"))),"exclusive paths cannot supply two cards");
});
Test("multiplayer-only cards abstain without blocking known rewards for either character",()=>
{
    foreach(var character in new[]{"Ironclad","Defect"})
    foreach(var count in new[]{1,2,4})
    {
        var s=run with{Character=character,PlayerCount=count,MultiplayerSession=true,Gold=100};
        var known=character=="Defect"?DefectPolicy.Card("Glacier")!:CardRegistry.All["Offering"].Basic;
        var coop=new DecisionOption("coop","Cooperative card","card",Card:known with{MultiplayerOnly=true});
        var choices=new[]{coop,new DecisionOption("known",known.Name,"card",Card:known),Skip()};
        var result=policy.Evaluate(s,choices);
        Check(result.Options[0].Score==null&&!result.Options[0].Supported,"cooperative option is not assigned a fabricated low score");
        Check(result.RecommendedKey is "known" or "skip","known options remain comparable");
        Check(s.IsMultiplayer&&result.Warnings.Contains(MultiplayerAdvice.Notice),"network multiplayer mode remains explicit even with one player");
        Check(policy.Evaluate(s,[coop]).RecommendedKey==null,"mandatory all-cooperative choices remain manual");
        Check(policy.Evaluate(s,[coop,new("unknown","unknown","card",Card:Card("UnknownModCard")),Skip()]).RecommendedKey==null,"ignoring cooperative cards does not silently ignore all unknown effects");
        var stock=ShopPolicy.Evaluate(s,[new("coop",known with{MultiplayerOnly=true},1),new("known",known,1)],null,100);
        Check(stock.Choices.All(c=>c.Key!="coop")&&stock.Choices.Any(c=>c.Key=="known"),"shop ignores cooperative stock but retains modeled quotes");
    }
});
Test("review 0646 two exhausting Holograms are useful without mandatory smithing",()=>
{
    CardSnapshot C(string n,bool up=false)=>DefectPolicy.Card(n,up)!;
    var s=run with{Character="Defect",Deck=[C("AdaptiveStrike"),C("Sunder"),C("Glacier"),C("Hologram"),C("Turbo")]};
    Check(Score(s,C("Hologram"))>0,"second one-shot recall remains a valid acquisition");
    var two=s with{Deck=[..s.Deck,C("Hologram")]};
    Check(DefectStrategy.Upgrade(two,C("Hologram"))<DefectStrategy.Upgrade(s,C("Hologram")),"two one-shot copies reduce urgency to smith");
    Check(Score(two,C("Hologram"))<Score(s,C("Hologram")),"third copy still has ordinary diminishing returns");
    Check(DefectStrategy.Upgrade(two with{Deck=[..two.Deck,C("AllForOne")]},C("Hologram"))>DefectStrategy.Upgrade(two,C("Hologram")),"repeatable retrieval engine still values exhaust removal");
});
Test("review 0646 early coverage is not inferred from two support cards",()=>
{
    CardSnapshot C(string n,bool up=false)=>DefectPolicy.Card(n,up)!;
    var s=run with{Character="Defect",Act=1,Floor=3,Hp=60,MaxHp=75,
        Deck=[..Enumerable.Repeat(C("StrikeDefect"),4),..Enumerable.Repeat(C("DefendDefect"),4),C("Zap"),C("Dualcast"),C("Hologram"),C("Turbo")]};
    foreach(var n in new[]{"BallLightning","ColdSnap","ChargeBattery"})
        Check(Score(s,C(n))>0,"support-only additions cannot ban useful transition "+n);
    Check(Score(s,C("BallLightning"))>Score(s,C("Coolheaded")),"immediate attack outranks unupgraded draw investment before early damage is solved");
    Check(Score(s,C("Coolheaded",true))>Score(s,C("Coolheaded")),"net draw upgrade retains value");
    var options=policy.Evaluate(s,rest);
    Check(options.Options.Single(o=>o.Key=="smith").Parts.Any(p=>p.Reason.Contains("推荐升级：")),"smith recommendation identifies targets");
});
Test("review 0646 commitment requires partners and retains mature engine conflicts",()=>
{
    CardSnapshot C(string n)=>DefectPolicy.Card(n)!;
    var s=run with{Character="Defect",Act=1,Deck=[C("StrikeDefect"),C("DefendDefect"),C("AdaptiveStrike")]};
    Check(!policy.Evaluate(s,[Pick(C("Darkness"))]).Options[0].Parts.Any(p=>p.Reason=="物理主线不再投资集中与产球终端"),"single adaptive attack is not a hard archetype lock");
    var supported=s with{Deck=[..s.Deck,C("AllForOne"),C("Hologram")]};
    Check(Score(supported,C("Darkness"))<Score(s,C("Darkness")),"real partners increase switching cost");
    Check(Score(supported,C("Glacier"))>0,"shared frost defense remains legal");
});
Test("0647 Apotheosis demand and priority are shared across characters",()=>
{
    CardSnapshot A(bool up=false)=>Card("Apotheosis",up?1:2,!up) with{Rarity="Ancient",Type="Skill",UpgradeLevel=up?1:0};
    var cores=new[]{"Darkness","Storm","Defragment","Coolheaded"}.Select(n=>DefectPolicy.Card(n)!).ToArray();
    var d=run with{Character="Defect",Deck=cores};
    Check(ApotheosisPolicy.PendingCores(d)==4&&Score(d,A())>0,"four key upgrades justify Apotheosis");
    Check(Score(d with{Deck=cores.Take(3).ToArray()},A())<0,"three key upgrades do not justify it by default");
    Check(Score(d with{Deck=[..cores.Take(2),..Enumerable.Repeat(DefectPolicy.Card("DefendDefect")!,8)]},A())<0,"many basics do not manufacture core demand");
    Check(RelicValue.Evaluate("JEWELRY_BOX",d).Value==ApotheosisPolicy.Value(d),"ancient offering uses actual upgrade demand");
    foreach(var character in new[]{"Defect","Ironclad"})
    {
        var s=d with{Character=character,Deck=[..cores,A()]};
        Check(CardMechanics.Upgrade(s,A())>cores.Max(c=>CardMechanics.Upgrade(s,c)),"Apotheosis first upgrade");
        Check(Score(s,A())<0,"duplicate is not valuable");
        var done=s with{Hp=40,Deck=[..cores,A(true)]};
        Check(policy.Evaluate(done,rest).RecommendedKey=="heal","upgraded Apotheosis prefers recovery");
        Check(cores.All(c=>CardMechanics.Upgrade(done,c)==0),"covered upgrades have no camp value");
        var effects=new[]{rest[0],rest[1],new DecisionOption("dig","Dig","effect",EffectValue:8)};
        Check(policy.Evaluate(done with{Hp=80},effects).RecommendedKey=="dig","full health keeps dig value");
        Check(policy.Evaluate(s with{Hp=5},rest).RecommendedKey=="heal","critical recovery still beats Apotheosis smithing");
    }
});
Test("0647 camp routes preserve safety and anticipate damage after Apotheosis",()=>
{
    var apo=Card("Apotheosis",1,false) with{UpgradeLevel=1,Type="Skill",Rarity="Ancient"};
    var s=run with{Hp=80,Deck=[..run.Deck,apo]};
    var nodes=new[]{new RouteNode("camp","RestSite",["boss"]),new("fight","Monster",["boss"]),new("boss","Boss",[])};
    var r=RoutePolicy.Evaluate(s,nodes,["camp","fight"],new());
    Check(r.RecommendedNode=="camp","full HP and Apotheosis do not ban safe camp route");
    var path=new[]{new RouteNode("fight","Monster",["camp"]),new("camp","RestSite",["boss"]),new("boss","Boss",[])};
    var plan=RoutePolicy.Evaluate(s,path,["fight"],new()).Options[0];
    Check(plan.CampPlans.Any(p=>p.Contains("预计休息"))&&!plan.CampPlans.Any(p=>p.Contains("预计升级")),"future battle damage schedules healing before it happens");
    var before=RoutePolicy.Evaluate(s with{Deck=[..run.Deck,apo with{CanUpgrade=true,UpgradeLevel=0}]},
        [new("a","RestSite",["b"]),new("b","RestSite",[])],["a"],new()).Options[0];
    Check(before.CampPlans.Count(p=>p.Contains("预计升级"))==1,"two camps only project the Apotheosis upgrade once");
});
Test("0647 tent actions and finite relic charges are not counted repeatedly",()=>
{
    var s=run with{Hp=40,Deck=[]};
    var nodes=new[]{new RouteNode("a","RestSite",["b"]),new("b","RestSite",["c"]),new("c","RestSite",[])};
    var economy=new RouteEconomy{CanSmith=false,CampAlternatives=[new("LIFT",10,1),new("DIG",8,12)],CampAllActions=true};
    var plan=RoutePolicy.Evaluate(s,nodes,["a"],economy).Options[0];
    Check(plan.CampPlans.Count(p=>p.Contains("LIFT"))==1,"only one remaining Girya charge");
    Check(plan.CampPlans.Count(p=>p.Contains("DIG"))==3,"Shovel once per distinct camp");
    Check(plan.CampPlans.Any(p=>p.Contains("预计休息")),"tent includes healing without giving up relic actions");
    var single=RoutePolicy.Evaluate(s,[new("a","RestSite",[])],["a"],economy with{CampAllActions=false}).Options[0];
    Check(single.CampPlans.Length==1,"ordinary camp gets one action");
});
Test("0647 passive camp healing is capped and never applied twice to current camp",()=>
{
    var s=run with{Hp=79,Deck=[]};
    RouteScore R(RouteEconomy e)=>RoutePolicy.Evaluate(s,[new("a","RestSite",[])],["a"],e).Options[0];
    var e=new RouteEconomy{CanRest=false,CanSmith=false,CampEntryHealing=9};
    Check(R(e).ProjectedHp==80,"feather capped at max HP");
    Check(R(e with{CurrentCampEntered=true}).ProjectedHp==79,"current camp already received feather heal");
    var terrors=R(new(){CanSmith=false,CampHealToFull=true,CampHealMaxHpDelta=-5,CampHealExtra=-5});
    Check(terrors.ProjectedHp==75,"full heal then max HP loss clamps projected HP");
    var full=RoutePolicy.Evaluate(s with{Hp=80},[new("a","RestSite",[])],["a"],new(){CanSmith=false,CampHealToFull=true,CampHealMaxHpDelta=-5,CampHealExtra=-5}).Options[0];
    Check(full.ProjectedHp==75,"mandatory full-HP rest cannot evade Night Terrors cost in forecast");
    var armed=R(e with{CampEntryAlreadyArmed=true,CampEntryValue=5});
    Check(armed.Score==R(e).Score,"already armed tea set does not stack more entry energy");
});
Test("0647 mimicked camp rest uses extra rewards without changing event combat burden",()=>
{
    var vars=new Dictionary<string,double>{{"Heal",24}};
    var before=EventRules.Describe("DENSE_VEGETATION","REST","Rest",run,vars);
    vars["RestExtra"]=6;
    var after=EventRules.Describe("DENSE_VEGETATION","REST","Rest",run,vars);
    Check(after.Benefit==before.Benefit+6&&after.Burden==before.Burden&&after.Heal==before.Heal,"extra reward does not replace combat risk or double-count healing");
});
Test("0648 real reward snapshots value burst energy and refresh over redundant transition cards",()=>
{
    using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","reward-support-0648.json")));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
    foreach(var item in doc.RootElement.EnumerateArray())
    {
        var s=item.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        var options=item.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
        var result=policy.Evaluate(s,options);
        var expected=item.GetProperty("expected").GetString();
        Check(options.Single(o=>o.Key==result.RecommendedKey).Card?.ClassName==expected,"actual reward should select "+expected);
    }
});
Test("0648 strong support still respects duplicate and energy saturation",()=>
{
    var super=DefectPolicy.Card("Supercritical")!;var reboot=DefectPolicy.Card("Reboot")!;
    var s=run with{Character="Defect",Deck=[DefectPolicy.Card("Glacier")!,DefectPolicy.Card("Hologram")!,DefectPolicy.Card("Skim")!,DefectPolicy.Card("AdaptiveStrike")!]};
    Check(Score(s,super)>Score(s with{Deck=[..s.Deck,super,DefectPolicy.Card("Turbo")!,DefectPolicy.Card("DoubleEnergy")!]},super),"duplicate burst cannot grow without bound");
    Check(Score(s,reboot)>Score(s with{Deck=[..s.Deck,reboot]},reboot),"second Reboot is marginal");
    var zero=s with{Energy=5,Deck=Enumerable.Repeat(DefectPolicy.Card("Claw")!,8).ToArray()};
    Check(Score(zero,super)<Score(s,super),"zero-cost deck cannot spend burst energy like an expensive deck");
});
Test("0648 bridge sees beyond another valuable card to a basic removal",()=>
{
    var core=DefectPolicy.Card("Supercritical")!;var bulk=DefectPolicy.Card("BulkUp")!;var basic=DefectPolicy.Card("DefendDefect")!;
    var s=run with{Character="Defect",Hp=50,MaxHp=75,Deck=[..Enumerable.Repeat(DefectPolicy.Card("StrikeDefect")!,3),..Enumerable.Repeat(basic,4),core,bulk,DefectPolicy.Card("AdaptiveStrike")!,DefectPolicy.Card("Hologram")!]};
    var plan=BridgePolicy.BestContinuation(s,[new(bulk,1,3),new(basic,2,7)])!;
    Check(plan.Stop.Steps==2&&plan.Score>BridgePolicy.Removal(s,core),"spend 3+4 HP to remove a basic instead of core");
    Check(BridgePolicy.BestContinuation(s with{Hp=3},[new(basic,1,3)])==null,"never predict surviving lethal damage");
    var low=BridgePolicy.BestContinuation(s with{Hp=8},[new(bulk,1,3),new(basic,2,7)])!;
    Check(low.Score<BridgePolicy.Removal(s with{Hp=8},core),"do not blindly pay down to one HP");
    Check(BridgePolicy.Removal(s,basic)>BridgePolicy.BestContinuation(s,[new(basic,1,3)])!.Score,"stop immediately when a basic target already suffices");
});
HeroTests.Register(tests);
Regent0670Tests.Register(tests);
Regent0671Tests.Register(tests);
Review0672Tests.Register(tests);
RegentDirectionTests.Register(tests);
ItemModelTests.Register(tests);
SilentTests.Register(tests);
SilentIntegrationTests.Register(tests);
var failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
