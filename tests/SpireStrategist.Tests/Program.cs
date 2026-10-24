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
            Check(CardCatalog.Find(c) != null && double.IsFinite(Score(run, c)), "Missing: " + c.ClassName);
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
Test("non-warrior and multiplayer states abstain", () =>
{
    Check(policy.Evaluate(run with { Character = "Silent" }, rest).RecommendedKey == null, "Unsupported character");
    Check(policy.Evaluate(run with { PlayerCount = 2 }, rest).RecommendedKey == null, "Unsupported multiplayer");
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
Test("regression late routes choose recovery and keep shop continuation visible",()=>
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
        if(seed=="regression-case-5")Check(picked.Kind=="skip","Do not add second conflicting Trance in recorded weak deck");
        if(seed=="regression-case-3")Check(picked.Card?.ClassName=="Shockwave","Sustained debuff must not vanish under generic taxes");
        if(seed=="regression-case-6"&&options.Any(o=>o.Card?.ClassName=="FeelNoPain"))
            Check(picked.Card?.ClassName=="FeelNoPain","Complete exhaust/block engine before strength payoff");
        if(seed=="regression-case-2")Check(s.Deck.Where(c=>c.CanUpgrade).MaxBy(c=>CardMechanics.Upgrade(s,c))!.ClassName=="FeelNoPain","Upgrade sustained exhaust defense before another paid cycler");
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
    Check(loaded.Ascension==10&&loaded.Character=="Necrobinder"&&!loaded.Supported,"persist placeholder without enabling it");
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
var failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
