using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal sealed record DecisionRecord(string Id, string RunId, string Kind, RunSnapshot State,
    DecisionOption[] Options, Evaluation Evaluation);
internal sealed record ChoiceRecord(string DecisionId, string RunId, string Key, string Name);

internal sealed class AdvisorService
{
    internal static AdvisorService? Current { get; private set; }
    private static Func<string>? lastFeedbackExport;
    internal static string? ExportLastFeedback()=>lastFeedbackExport?.Invoke();
    private readonly RunState run;
    private readonly Player player;
    private readonly Journal journal;
    private readonly Policy policy;
    private readonly AdvisorPanel panel;
    internal RecommendationOverlay Visuals {get;}
    private readonly string runId = Guid.NewGuid().ToString("N");
    private string lastState = "";
    private string lastDecision = "";
    private DecisionRecord? decision;
    private NCardRewardSelectionScreen? cardScreen;
    private IReadOnlyList<CardCreationResult> cardOptions = [];
    private IReadOnlyList<CardRewardAlternative> extraOptions = [];
    private NRestSiteRoom? restRoom;
    private bool restBusy;
    private bool cardsResolved;
    private bool active = true;
    private bool faulted;
    private readonly PartialAutopilot autopilot;
    private object? feedbackContext, pendingFeedbackContext;
    private string? lastShopPreview;
    internal RunState State => run;
    internal Player Player => player;
    internal AdvisorPanel Panel => panel;
    internal DecisionRecord? Decision => decision;
    internal NCardRewardSelectionScreen? CardScreen => cardScreen;
    internal NRestSiteRoom? RestRoom => restRoom;
    internal bool RestBusy => restBusy;
    internal IReadOnlyList<CardCreationResult> CardOptions => cardOptions;
    internal PartialAutopilot Autopilot => autopilot;
    internal void ShowRoute(RouteEvaluation evaluation)
    {feedbackContext=new{kind="route",state=SnapshotReader.Read(run,player),evaluation};panel.ShowRoute(evaluation);Visuals.Route(evaluation);}
    internal void ShowShop(RunSnapshot state,ShopEvaluation plan)
    {
        feedbackContext=new{kind="shop",state,plan};
        var fingerprint=JsonSerializer.Serialize(feedbackContext,Journal.Json);
        if(fingerprint!=lastShopPreview){lastShopPreview=fingerprint;journal.Write("shop_preview",new{runId,state,plan});}
        panel.ShowShop(plan);Visuals.Shop(plan);
    }
    internal void RecordAuto(string type, object payload) => journal.Write("auto_"+type, new {runId, payload});
    internal void PresentEvent(EventPlan plan)
    {
        var fingerprint=JsonSerializer.Serialize(new{plan.State,plan.Choices},Journal.Json);
        if(lastDecision==fingerprint)return;
        lastDecision=fingerprint;
        var options=plan.Choices.Select(c=>new DecisionOption(c.Key,c.Name,"event",c.Enabled)).ToArray();
        decision=new(Guid.NewGuid().ToString("N"),runId,"event",plan.State,options,plan.Evaluation);
        feedbackContext=decision;
        journal.Write("event_decision",new{decision,eventId=plan.Model.Id.ToString(),choices=plan.Choices,textKeys=plan.Buttons.Select(b=>b.Option.TextKey).ToArray()});
        journal.Latest("latest-decision",decision);panel.Show(plan.State,plan.Evaluation,decision.Id);
    }

    internal static void Attach(NRun node, RunState state)
    {
        if (state.Players.Count != 1) return;
        Current = new AdvisorService(node, state);
    }

    private AdvisorService(NRun node, RunState state)
    {
        run = state;
        player = state.Players[0];
        journal = new Journal(Path.Combine(OS.GetUserDataDir(), "SpireStrategist"));
        PolicyWeights weights;
        string? configError = null;
        try { weights = journal.LoadWeights(); }
        catch (Exception ex) { weights = new(); configError = ex.Message; }
        policy = new Policy(weights);
        journal.Write("session_start", new { runId, gameVersion = NGame.GetGameVersion(),
            policyVersion = Policy.Version, weights, configError,
            mods = ModManager.GetLoadedMods().Where(m => m.manifest != null)
                .Select(m => new { id = m.manifest!.id, version = m.manifest.version }).ToArray() });
        panel = new AdvisorPanel(node, (vote,reason) => Guard(() => Feedback(vote,reason)),()=>pendingFeedbackContext=feedbackContext,
            () => Guard(Export), () => Guard(ToggleAuto),value=>Guard(()=>autopilot!.SetFullAutomatic(value)));
        autopilot = new PartialAutopilot(this);
        Visuals = new RecommendationOverlay(node,this);
        var autoTimer = new Godot.Timer { WaitTime = .25, OneShot = false };
        node.AddChild(autoTimer);
        autoTimer.Timeout += () => {if(active && !faulted) autopilot.Tick();};
        autoTimer.Start();
        var timer = new Godot.Timer { WaitTime = 1, OneShot = false };
        node.AddChild(timer);
        timer.Timeout += () => Guard(Poll);
        timer.Start();
        node.TreeExiting += () =>
        {
            ClimbController.LeftRun(state);
            autopilot.Pause("已离开本局。");
            active = false;
            if (Current == this) Current = null;
            try
            {
                journal.Write("session_end", new { runId, reason = "run_node_exited" });
                var summary=new{version=Policy.Version,runId,context=feedbackContext,decision};
                lastFeedbackExport=()=>journal.ExportFeedback(summary);
            }
            catch (Exception ex) { GD.PrintErr("[SpireStrategist] " + ex); }
        };
        Poll();
        ClimbController.Attach(this);
        if (configError != null) panel.Status("参数文件错误，已用默认值；详情见日志。");
    }

    internal void Guard(Action action)
    {
        if (!active || faulted) return;
        try { action(); }
        catch (Exception ex)
        {
            // A broken adapter must never keep displaying an old recommendation.
            faulted = true;
            autopilot.Pause("助手出现异常。");
            decision = null;
            panel.Error(ex.Message);Visuals.Clear();
            GD.PrintErr("[SpireStrategist] " + ex);
            try { journal.Write("error", new { runId, error = ex.ToString() }); } catch { }
        }
    }

    private void ToggleAuto()
    {
        if(ClimbController.Enabled)ClimbController.Stop("玩家点击停止接管");
        else autopilot.Toggle();
    }

    internal void Cards(NCardRewardSelectionScreen screen, IReadOnlyList<CardCreationResult> cards,
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        cardScreen = screen;
        cardOptions = cards.ToArray();
        extraOptions = alternatives.ToArray();
        cardsResolved = false;
        lastDecision = "";
        Poll();
    }

    internal void Rest(NRestSiteRoom room)
    {
        restRoom = room;
        restBusy = false;
        lastDecision = "";
        Poll();
    }

    internal void RestStarted() => restBusy = true;
    internal void RestFinished(RestSiteOption option, bool success)
    {
        if (decision?.Kind == "rest")
        {
            journal.Write("rest_result", new { decisionId = decision.Id, runId, key = option.OptionId, success });
            if (success) CommitChoice(PrepareChoice(option.OptionId));
        }
        restBusy = success;
        if (!success) lastDecision = "";
        // Extra-action relics re-enable options through the separate EnableOptions hook.
        Poll();
    }

    internal void RestEnabled() { restBusy = false; lastDecision = ""; Poll(); }

    private static double? RestEffect(RestSiteOption option,RunSnapshot s)
    {
        var v=new StrategicValue(s);
        return option switch
        {
            CookRestSiteOption=>v.RemoveBest(2)+v.MaxHp(5),
            CloneRestSiteOption=>s.Deck.Where(c=>c.Enchantment=="ENCHANTMENT.CLONE").Sum(v.Add),
            HatchRestSiteOption=>10*v.Horizon,
            KindleRestSiteOption=>5+Math.Min(4,v.Attack),
            LiftRestSiteOption=>4+v.Multi*2,
            DigRestSiteOption=>8,
            _=>null
        };
    }
    private void Poll()
    {
        var snapshot = SnapshotReader.Read(run, player);
        var stateJson = JsonSerializer.Serialize(snapshot, Journal.Json);
        if (stateJson != lastState)
        {
            journal.Write("state", new { runId, state = snapshot });
            journal.Latest("latest-state", snapshot);
            lastState = stateJson;
        }

        panel.UpdateState(snapshot);
        if(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance is {IsOpen:true} mapScreen)
        {
            if(!autopilot.Enabled || panel.ReviewPaused || !mapScreen.IsTravelEnabled)
                ShowRoute(RouteAdapter.Preview(this,mapScreen));
            return;
        }
        if(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom.Instance?.Inventory is {IsOpen:true} inventory)
        {
            if(!autopilot.Enabled || panel.ReviewPaused)
            {
                var slots=inventory.GetAllSlots().Where(x=>x.IsVisibleInTree()&&x.Entry is {IsStocked:true}).ToArray();
                var stock=slots.Where(x=>x.Entry is MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry{CreationResult:not null})
                    .Select(x=>new ShopCard(x.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry)x.Entry).CreationResult!.Card),x.Entry.Cost)).ToArray();
                var removal=slots.FirstOrDefault(x=>x.Entry is MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry);
                var relics=slots.Where(x=>x.Entry is MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry {Model:not null})
                    .Select(x=>{var r=(MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry)x.Entry;return new ShopRelic(x.GetInstanceId().ToString(),r.Model!.Id.Entry,r.Model.Title.GetFormattedText(),r.Cost);}).ToArray();
                var priority=PriorityShopPolicy.Evaluate(snapshot,relics,stock,autopilot.ShopBoughtCards);
                if(priority!=null)
                {
                    var c=new ShopChoice(priority.Card?.Key??priority.Relic.Key,priority.Card?.Card.Name??priority.Relic.Name,
                        priority.Card!=null?"card":"relic",priority.Card?.Price??priority.Relic.Price,100,
                        [priority.Reason,$"计划总价 {priority.TotalPrice}，现款 {snapshot.Gold}"]);
                    ShowShop(snapshot,new(c,[c],0,[priority.Reason]));
                }
                else ShowShop(snapshot,ShopPolicy.Evaluate(snapshot,stock,removal?.Entry.Cost,autopilot.ShopArrivalGold(snapshot.Gold),autopilot.ShopBoughtCards));
            }
            return;
        }
        if(!MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress &&
            MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance?.GetCurrentScreen() is NEventRoom eventRoom &&
            EventAdapter.Read(this,eventRoom) is {} eventPlan)
        {PresentEvent(eventPlan);return;}
        DecisionOption[]? options = null;
        string kind = "";
        if (cardScreen != null && GodotObject.IsInstanceValid(cardScreen) && cardScreen.IsInsideTree() && !cardsResolved)
        {
            kind = "cards";
            options = cardOptions.Select((c, i) => new DecisionOption("card:" + i, c.Card.Title, "card", Card: SnapshotReader.ReadCard(c.Card)))
                .Concat(extraOptions.Select((o, i) => new DecisionOption("alternative:" + i, o.Title.GetFormattedText(),
                    o.OptionId.Equals("Skip", StringComparison.OrdinalIgnoreCase) ? "skip" : o.OptionId=="SACRIFICE"&&player.GetRelic<MegaCrit.Sts2.Core.Models.Relics.PaelsWing>()!=null?"effect":"unknown",
                    EffectValue:o.OptionId=="SACRIFICE"?4:null))).ToArray();
        }
        else if (restRoom != null && GodotObject.IsInstanceValid(restRoom) && restRoom.IsInsideTree() && !restBusy)
        {
            kind = "rest";
            options = restRoom.Options.Select(o => new DecisionOption(o.OptionId, o.Title.GetFormattedText(),
                o is HealRestSiteOption ? "heal" : o is SmithRestSiteOption ? "smith" : RestEffect(o,snapshot)!=null?"effect":"unknown", o.IsEnabled,
                HealAmount: o is HealRestSiteOption ? (double)HealRestSiteOption.GetHealAmount(player) : 0,
                SmithCount: o is SmithRestSiteOption smith ? smith.SmithCount : 1,EffectValue:RestEffect(o,snapshot))).ToArray();
        }

        if (options is { Length: > 0 })
        {
            var fingerprint = stateJson + JsonSerializer.Serialize(options, Journal.Json);
            if (fingerprint == lastDecision) return;
            if(kind=="rest"&&run.CurrentMapPoint is {} campPoint&&snapshot.Map?.Error==null)
            {
                try
                {
                    var nodes=RouteAdapter.Nodes(run);var economy=RouteAdapter.Economy(this);
                    snapshot=snapshot with{CampRouteRisk=CampRoutePolicy.Risks(snapshot,options,nodes,RouteAdapter.Key(campPoint),economy)};
                    journal.Write("camp_route_context",new{state=snapshot,nodes,economy,current=RouteAdapter.Key(campPoint)});
                }
                catch(Exception ex){journal.Write("camp_route_unavailable",new{error=ex.GetBaseException().Message});}
            }
            var evaluation = policy.Evaluate(snapshot, options);
            decision = new(Guid.NewGuid().ToString("N"), runId, kind, snapshot, options, evaluation);
            feedbackContext=decision;
            journal.Write("decision", decision);
            journal.Latest("latest-decision", decision);
            lastDecision = fingerprint;
            panel.Show(snapshot, evaluation, decision.Id);
        }
        else
        {
            // Retain rest decision while the upgrade selector is open so feedback still refers to it.
            if (restBusy && decision?.Kind == "rest") return;
            decision = null;
            feedbackContext=null;
            lastDecision = "";
            panel.Show(snapshot, null, null);
        }
    }

    internal ChoiceRecord? PrepareCardChoice(CardModel? model)
    {
        var index = cardOptions.ToList().FindIndex(c => ReferenceEquals(c.Card, model));
        return index < 0 ? null : PrepareChoice("card:" + index);
    }

    internal void CardUpgraded(CardModel card)
    {
        if (decision?.Kind != "rest" || !player.Deck.Cards.Contains(card)) return;
        journal.Write("upgrade", new { decisionId = decision.Id, runId, card = SnapshotReader.ReadCard(card) });
    }

    internal ChoiceRecord? PrepareChoice(string key)
    {
        var option = decision?.Options.FirstOrDefault(o => o.Key == key);
        return option == null || decision == null ? null : new(decision.Id, runId, key, option.Name);
    }

    internal void CommitChoice(ChoiceRecord? choice)
    {
        if (choice == null) return;
        if(ClimbController.Enabled&&!autopilot.OwnsDecision(choice.DecisionId))
            ClimbController.Assisted("检测到非助手提交的选牌/营火操作");
        journal.Write("choice", choice);
        // A reroll can open a new decision synchronously while the old callback is completing.
        if (decision?.Id != choice.DecisionId) return;
        if (decision.Kind == "cards") cardsResolved = true;
        decision = null;
        lastDecision = "";
        Poll();
    }

    private void Feedback(string vote,string reason)
    {
        var context=vote=="disagree"?pendingFeedbackContext:feedbackContext;
        if(context==null)return;
        journal.Write("feedback", new {decisionId=(context as DecisionRecord)?.Id,runId,vote,reason,context});
        panel.Status($"已在本机记录{(vote == "agree" ? "赞同" : "异议")}；点击“导出反馈包”后将 ZIP 发给作者。");
        pendingFeedbackContext=null;
    }

    private void Export()
    {
        var path=journal.ExportFeedback(new{version=Policy.Version,runId,state=SnapshotReader.Read(run,player),context=feedbackContext,decision});
        panel.Status("已导出反馈包，请把 ZIP 发给作者：\n"+path);
        OS.ShellOpen(Path.GetDirectoryName(path)!);
    }
}
