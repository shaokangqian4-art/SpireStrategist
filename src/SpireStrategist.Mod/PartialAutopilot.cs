using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rewards;
using SpireStrategist.Core;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private readonly AdvisorService service;
    private readonly ActionGate gate = new();
    private readonly SolverBridge solver = new();
    private CombatState? ownedCombat;
    private Task? operation;
    private object? operationOrigin;
    private readonly Stack<(Task Task,object? Origin)> parentOperations = new();
    private Task? chestOperation;
    private ulong operationStarted;
    private CardModel? smithTarget;
    private NRestSiteRoom? smithRoom;
    private CardModel? removalTarget;
    private MerchantRoom? shoppingRoom;
    private int shopArrivalGold,shopBoughtCards;
    internal int ShopArrivalGold(int fallback)=>shoppingRoom==service.State.CurrentRoom?shopArrivalGold:fallback;
    internal int ShopBoughtCards=>shoppingRoom==service.State.CurrentRoom?shopBoughtCards:0;
    private string? unsupported;
    private ulong unsupportedSince;
    private int enableGeneration;
    private CombatState? blockedCombat;
    internal bool FullAutomatic {get;private set;}
    internal bool WaitingForManual {get;private set;}
    internal bool Enabled => gate.Enabled;
    internal bool OwnsDecision(string id)=>gate.WasSubmitted("decision:"+id);
    internal string Status { get; private set; } = "建议模式 · 点击开始接管";
    internal PartialAutopilot(AdvisorService owner) => service=owner;
    internal void Toggle() { if(Enabled) Pause("你已暂停；已提交的单次动作可能仍在完成。"); else Enable(true); }
    internal void SetFullAutomatic(bool value) {if(value)Enable(true);else Pause("全自动待命已关闭。");}
    internal void Enable(bool fullAutomatic=false)
    {
        if(service.State.Players.Count!=1 || service.Player.Character.GetType().Name!="Ironclad") { Pause("仅支持单人战士。");return; }
        gate.Enable(); FullAutomatic=fullAutomatic; WaitingForManual=false; blockedCombat=null;
        if(operation!=null)operationStarted=Time.GetTicksMsec();
        if(solver.Bind()==null)solver.ArmAutoStart();
        enableGeneration++; unsupported=null; Status=ClimbController.Enabled?"循环接管中 · 快速执行":"接管中 · 常规操作等待 2 秒，商店购买等待 8 秒";
        service.RecordAuto("enabled",new{});
        UpdatePanel();
    }
    internal void Pause(string reason)
    {
        var wasEnabled=Enabled;
        gate.Pause(); FullAutomatic=false; WaitingForManual=false; smithTarget=null; smithRoom=null; removalTarget=null; unsupported=null;
        ownedEvent=null;eventSelectionTargets=null;ownedRestSelection=null;ownedPickup=null;ownedPickupRoom=null;ownsDig=false;
        Status="已暂停 · "+reason;
        solver.ReleaseAutoStart();
        var owned=ownedCombat; ownedCombat=null;
        if(owned!=null) { try {solver.Stop(owned);} catch(Exception ex) {Status+=" 求解器停止失败："+ex.GetBaseException().Message;} }
        if(wasEnabled) service.RecordAuto("paused",new{reason=Status});
        UpdatePanel();
    }
    private void UpdatePanel()=>service.Panel.Automation(Enabled,FullAutomatic,Status);
    private void Wait(string text) {WaitingForManual=false;Status=text;unsupported=null;UpdatePanel();}
    private void Manual(string text)
    {
        if(!FullAutomatic){Pause(text);return;}
        gate.ResetPending();
        if(!WaitingForManual)
        {
            enableGeneration++;
            if(ownedCombat!=null){solver.Stop(ownedCombat);ownedCombat=null;}
            service.RecordAuto("waiting_for_manual",new{reason=text});
        }
        WaitingForManual=true;Status="全自动待命 · "+text+" 进入支持的界面后自动继续。";UpdatePanel();
    }
    private bool Claim(string key,bool ready=true,ulong delay=2000)
    {
        var result=gate.TryClaim(key,Time.GetTicksMsec(),ready,AutomationTiming.Delay(ClimbController.Enabled,delay));
        if(result) service.RecordAuto("submit",new{key});
        return result;
    }
    private void Track(Func<Task> start)
    {
        // Capture BEFORE invoking: a rest/relic action can synchronously open its child UI.
        var origin=ActiveScreenContext.Instance.GetCurrentScreen();
        var task=start();
        if(operation!=null)parentOperations.Push((operation,operationOrigin));
        operation=task;operationOrigin=origin;operationStarted=Time.GetTicksMsec();
    }
    internal void Tick()
    {
        try {TickCore();} catch(Exception ex) {Pause("操作失败："+ex.GetBaseException().Message);service.RecordAuto("error",new{error=ex.ToString()});}
    }
    private void TickCore()
    {
        if(!Enabled) return;
        if(ClimbController.Enabled&&ClimbController.LaunchInProgress)
        {gate.ResetPending();Wait("循环接管中 · 等待原生开局加载完成");return;}
        if(service.Panel.ReviewPaused){gate.ResetPending();if(operation!=null)operationStarted=Time.GetTicksMsec();Wait("接管待命 · 正在查看建议或填写异议，暂停后续动作");return;}
        if(FullAutomatic&&CombatManager.Instance.IsInProgress&&
            solver.TryDeathStop(CombatManager.Instance.DebugOnlyGetState(),out var deathTurn))
        {
            ClimbController.Concede(service,deathTurn);
            return;
        }
        if(chestOperation is {IsCompleted:true}){var task=chestOperation;chestOperation=null;task.GetAwaiter().GetResult();}
        if(!CombatManager.Instance.IsInProgress && ownedCombat!=null) {solver.Stop(ownedCombat);ownedCombat=null;}
        if(service.Player.Creature.CurrentHp<=0) {Pause("本局已结束。");return;}
        while(operation is {IsCompleted:true})
        {
            var finished=operation;
            if(parentOperations.TryPop(out var parent))
            {operation=parent.Task;operationOrigin=parent.Origin;operationStarted=Time.GetTicksMsec();}
            else {operation=null;operationOrigin=null;}
            finished.GetAwaiter().GetResult();
            // A child reward ending does not end the rest/upgrade that is waiting for it.
            if(operation==null){smithTarget=null;smithRoom=null;removalTarget=null;ownedRestSelection=null;ownsDig=false;}
            service.RecordAuto("completed",new{pendingParents=parentOperations.Count,parentPending=operation!=null});
        }
        if(operation!=null && Time.GetTicksMsec()-operationStarted>30000) {Pause("操作等待超过 30 秒，请手动检查。");return;}
        var screen=ActiveScreenContext.Instance.GetCurrentScreen();
        if(ClimbController.Enabled&&screen is MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NEarlyAccessDisclaimer disclaimer)
        {
            Wait("自动爬塔 · 关闭抢先体验说明");
            if(Claim("early-access:"+disclaimer.GetInstanceId()))Track(()=>disclaimer.CloseScreen());
            return;
        }
        if(NModalContainer.Instance?.OpenModal!=null) {Manual("出现弹窗，请先手动处理。");return;}
        // Native result viewer (e.g. Pandora's Box): cards are already in the deck.
        // Confirm only this exact read-only screen, never a selection or inspection screen.
        if(!CombatManager.Instance.IsInProgress && screen is NSimpleCardsViewScreen results &&
            results.GetType()==typeof(NSimpleCardsViewScreen))
        {
            Wait("接管中 · 确认已完成的卡牌变化结果");
            var confirm=GameActions.Field(results,"_confirmButton") as NClickableControl;
            if(Claim("card-results-confirm:"+results.GetInstanceId(),
                confirm is {IsEnabled:true} && confirm.IsVisibleInTree() && results.IsVisibleInTree()))
            {
                service.RecordAuto("card_results_confirm",new{screen=results.GetType().Name,
                    cards=(GameActions.Field(results,"_cards") as IEnumerable<CardModel>)?.Select(c=>SnapshotReader.ReadCard(c)).ToArray()});
                GameActions.Invoke(results,"OnReturnButtonPressed",confirm);
            }
            return;
        }
        if(screen is NMerchantInventory busyInventory && GameActions.Field(busyInventory,"_isInputBlocked") is true)
        {Wait("接管中 · 等待商店遗物或动画处理完成");return;}
        PrepareParasolRemoval(screen);
        if(TickEvent(screen))return;
        if(ownedCombat!=null && CombatManager.Instance.IsInProgress && screen?.GetType().Namespace==typeof(NDeckUpgradeSelectScreen).Namespace)
        {
            if(!solver.FullAuto){blockedCombat=ownedCombat;Manual("求解器已在战斗选牌中停止。");return;}
            Wait("接管中 · 战斗内选牌交给求解器");return;
        }
        var excludedRewards=service.State.CurrentRoom is MerchantRoom&&!OwnsPickup || service.State.CurrentRoom is EventRoom {CanonicalEvent:AncientEventModel} && !OwnsCurrentEvent;
        if(screen is NDeckCardSelectScreen remove && removalTarget!=null && service.State.CurrentRoom==shoppingRoom)
        {
            var eligible=(IEnumerable<CardModel>)GameActions.Field(remove,"_cards")!;
            var selected=(HashSet<CardModel>)GameActions.Field(remove,"_selectedCards")!;
            if(!eligible.Contains(removalTarget) || !service.Player.Deck.Cards.Contains(removalTarget)) {Pause("删牌目标已变化。");return;}
            if(selected.Count>0 && !selected.SetEquals([removalTarget])) {Pause("检测到你更改了删牌目标。");return;}
            Wait("接管中 · 商店删除 "+removalTarget.Title);
            if(selected.Count==0)
            {if(Claim("remove-select:"+remove.GetInstanceId()))GameActions.Invoke(remove,"OnCardClicked",removalTarget);}
            else
            {
                var confirm=(NClickableControl)GameActions.Field(remove,"_previewConfirmButton")!;
                if(Claim("remove-confirm:"+remove.GetInstanceId(),confirm.IsEnabled && confirm.IsVisibleInTree()))GameActions.Invoke(remove,"ConfirmSelection",confirm);
            }
            return;
        }
        // During an owned smith operation only, finish the game's actual selection and confirmation screen.
        if(screen is NDeckUpgradeSelectScreen upgrade && smithTarget!=null && smithRoom==service.RestRoom)
        {
            var eligible=(IEnumerable<CardModel>)GameActions.Field(upgrade,"_cards")!;
            if(!smithTarget.IsUpgradable || !service.Player.Deck.Cards.Contains(smithTarget) || !eligible.Contains(smithTarget)) {Pause("升级目标已变化。");return;}
            var selected=(HashSet<CardModel>)GameActions.Field(upgrade,"_selectedCards")!;
            if(selected.Count>0 && !selected.SetEquals([smithTarget])) {Pause("检测到你更改了升级目标。");return;}
            if(selected.Count==0)
            {
                if(Claim("upgrade-select:"+upgrade.GetInstanceId())) GameActions.SelectUpgrade(upgrade,smithTarget);
            }
            else if(Claim("upgrade-confirm:"+upgrade.GetInstanceId())) GameActions.ConfirmUpgrade(upgrade);
            Wait("接管中 · 升级 "+smithTarget.Title); return;
        }
        if(screen is NCardRewardSelectionScreen cards && ReferenceEquals(cards,service.CardScreen))
        {
            if(excludedRewards){Manual("商店或先古奖励请手动选择。");return;}
            var d=service.Decision;
            if(d?.Kind!="cards") {gate.ResetPending();return;}
            var chosen=d.Options.FirstOrDefault(o=>o.Key==d.Evaluation.RecommendedKey);
            if(OwnsCurrentEvent&&eventSelection=="skip-foreign")chosen=d.Options.FirstOrDefault(o=>o.Kind=="skip"&&o.Enabled);
            if(chosen==null) {Manual("这组选牌存在未适配效果，请手动选择。");return;}
            Wait("接管中 · 即将选择 "+chosen.Name);
            if(!Claim("decision:"+d.Id,GameActions.RewardReady(cards))) return;
            if(chosen.Key.StartsWith("card:")) GameActions.SelectCard(cards,service.CardOptions[int.Parse(chosen.Key[5..])].Card);
            else if(chosen.Kind=="skip"||chosen.Kind=="effect") GameActions.SelectSkip(cards,int.Parse(chosen.Key[12..]));
            else Manual("当前奖励选项尚未支持自动执行。");
            return;
        }
        // A relic pickup can open an unimplemented card selector. Retain its task while the
        // user completes it, and resume afterwards without repeatedly claiming the reward.
        if(operation!=null && screen is not (NRewardsScreen or NRestSiteRoom or NTreasureRoom or NCombatRoom or NMerchantInventory or NMerchantRoom))
        {Manual("当前动作需要手动完成选择。");operationStarted=Time.GetTicksMsec();return;}
        if(screen is NTreasureRoom treasure)
        {
            var collection=GameActions.Descendants<NTreasureRoomRelicCollection>(treasure).FirstOrDefault(c=>c.IsVisibleInTree());
            var holders=collection==null?[]:GameActions.Descendants<NTreasureRoomRelicHolder>(collection).Where(h=>h.IsVisibleInTree() && h.IsEnabled && h.MouseFilter!=Control.MouseFilterEnum.Ignore).ToArray();
            var holder=holders.FirstOrDefault(h=>h.Relic.Model.Rarity!=RelicRarity.Ancient);
            if(holder!=null)
            {
                Wait("接管中 · 领取宝箱遗物 "+holder.Relic.Model.Title.GetFormattedText());
                if(Claim("chest-relic:"+holder.GetInstanceId()))
                {PrepareRelicSelection(holder.Relic.Model);GameActions.Invoke(collection!,"PickRelic",holder);}
                return;
            }
            if(holders.Length>0){Manual("先古遗物请手动选择。");return;}
            if(chestOperation!=null){Wait("接管中 · 等待宝箱处理完成");return;}
            if(GameActions.Field(treasure,"_hasChestBeenOpened") is not true)
            {
                var chest=(NClickableControl)GameActions.Field(treasure,"_chestButton")!;
                Wait("接管中 · 打开宝箱");
                if(Claim("chest:"+treasure.GetInstanceId(),chest.IsEnabled))
                {chest.Disable();chestOperation=(Task)GameActions.Invoke(treasure,"OpenChest")!;}return;
            }
            Proceed(treasure,treasure.ProceedButton,"OnProceedButtonReleased");return;
        }
        // Sleep/dig/relic tasks await their reward overlay. Drive that child overlay while
        // retaining the parent task, but do not click another button on a still-busy source.
        if(operation!=null && (screen is not NRewardsScreen || ReferenceEquals(screen,operationOrigin)))
        {gate.ResetPending();Wait("接管中 · 等待当前动作完成");return;}
        if(screen is NRestSiteRoom rest && ReferenceEquals(rest,service.RestRoom))
        {
            if(service.RestBusy) {Proceed(rest,rest.ProceedButton,"OnProceedButtonReleased");return;}
            var d=service.Decision;
            if(d?.Kind!="rest") {Proceed(rest,rest.ProceedButton,"OnProceedButtonReleased");return;}
            var chosen=d.Options.FirstOrDefault(o=>o.Key==d.Evaluation.RecommendedKey && o.Enabled);
            if(rest.ProceedButton.IsEnabled && (chosen==null&&d.Options.All(o=>!o.Enabled)||
                d.Evaluation.Options.FirstOrDefault(o=>o.Key==chosen?.Key)?.Score<=0))
            {Proceed(rest,rest.ProceedButton,"OnProceedButtonReleased");return;}
            var option=rest.Options.FirstOrDefault(o=>o.OptionId==chosen?.Key);
            if(option is not (HealRestSiteOption or SmithRestSiteOption or CookRestSiteOption or CloneRestSiteOption or HatchRestSiteOption or KindleRestSiteOption or LiftRestSiteOption or DigRestSiteOption)) {Manual("营火含未适配选项，请手动操作。");return;}
            var button=rest.GetButtonForOption(option);
            Wait("接管中 · 即将"+chosen!.Name);
            if(!Claim("decision:"+d.Id,button?.IsEnabled==true && button.IsVisibleInTree()))return;
            if(option is SmithRestSiteOption smith)
            {
                if(smith.SmithCount!=1){ownedRestSelection=rest;eventSelection="upgrade";eventSelectionTargets=null;Track(()=>GameActions.SelectRest(button!,option));return;}
                smithTarget=service.Player.Deck.Cards.Where(c=>c.IsUpgradable && CardCatalog.Find(SnapshotReader.ReadCard(c))!=null)
                    .OrderByDescending(c=>CardMechanics.Upgrade(service.Decision!.State,SnapshotReader.ReadCard(c))).FirstOrDefault();
                if(smithTarget==null){Pause("没有可执行的升级目标。");return;}
                smithRoom=rest;
            }
            if(option is CookRestSiteOption){ownedRestSelection=rest;eventSelection="remove";eventSelectionTargets=null;}
            if(option is DigRestSiteOption){ownedRestSelection=rest;ownsDig=true;}
            Track(()=>GameActions.SelectRest(button!,option));return;
        }
        if(screen is NRewardsScreen rewards)
        {
            if(excludedRewards){Manual("商店或先古奖励请手动处理。");return;}
            var skipped=(IEnumerable<Control>)GameActions.Field(rewards,"_skippedRewardButtons")!;
            var buttons=GameActions.Descendants<NRewardButton>(rewards).Where(b=>b.IsVisibleInTree() && b.IsEnabled && !skipped.Contains(b)).ToArray();
            if(service.State.CurrentRoom is EventRoom courier && courier.CanonicalEvent is MegaCrit.Sts2.Core.Models.Events.PotionCourier &&
                CourierPolicy.MayReplace(SnapshotReader.Read(service.State,service.Player)) &&
                !service.Player.Relics.Any(r=>r.Id.Entry=="SOZU") && !service.Player.PotionSlots.Any(p=>p==null) &&
                buttons.Any(b=>b.Reward is PotionReward {Potion: MegaCrit.Sts2.Core.Models.Potions.FoulPotion}))
            {
                var replace=service.Player.Potions.Where(p=>p is not MegaCrit.Sts2.Core.Models.Potions.FoulPotion && p.GetType().Assembly==typeof(PotionModel).Assembly)
                    .OrderBy(p=>p.Rarity.ToString() switch{"Common"=>0,"Uncommon"=>1,"Rare"=>2,_=>3}).FirstOrDefault();
                if(replace!=null)
                {
                    Wait("接管中 · 药水快递员：替换 "+replace.Title.GetFormattedText()+"，为污浊药水腾空位");
                    if(Claim("courier-discard:"+rewards.GetInstanceId()+":"+service.Player.PotionSlots.ToList().IndexOf(replace)+":"+replace.Id+":"+string.Join(',',buttons.Select(b=>b.GetInstanceId()))))
                    {service.RecordAuto("courier_discard",new{potion=replace.Id.ToString(),rarity=replace.Rarity.ToString(),state=SnapshotReader.Read(service.State,service.Player)});Track(()=>MegaCrit.Sts2.Core.Commands.PotionCmd.Discard(replace));}
                    return;
                }
            }
            var safe=buttons.FirstOrDefault(b=>b.Reward is GoldReward or CardReward ||
                b.Reward is RelicReward {Relic:not null} relic && relic.Relic.Rarity!=RelicRarity.Ancient && !LeaveHarmfulFakeRelic(b.Reward) ||
                b.Reward is PotionReward && service.Player.PotionSlots.Any(p=>p==null));
            if(safe==null)
            {
                // Evaluate AFTER ordinary rewards: a new card/relic can change whether recovery is useful.
                var state=SnapshotReader.Read(service.State,service.Player);
                var recoveries=buttons.Where(b=>IsStolenCardRecovery(b.Reward)).Select(b=>
                    (Button:b,Card:GameActions.Field(b.Reward!,"_card") as CardModel)).ToArray();
                if(recoveries.Any(r=>r.Card==null)){Manual("无法读取被偷的牌，请手动决定是否取回。");return;}
                var plans=recoveries.Select(r=>(r.Button,Plan:RecoveryPolicy.Evaluate(state,SnapshotReader.ReadCard(r.Card!),
                    continuous:FullAutomatic,skipAllowed:GameActions.Field(rewards,"_skipDisallowed") is not true))).ToArray();
                var take=plans.FirstOrDefault(r=>r.Plan.Action=="recover");
                var fingerprint=System.Text.Json.JsonSerializer.Serialize(new{state,plans=plans.Select(p=>p.Plan).ToArray()});
                if(take.Button!=null)
                {
                    Wait("接管中 · 取回 "+take.Plan.CardName+"："+take.Plan.Reasons[0]);
                    if(Claim("recover:"+take.Button.GetInstanceId()+":"+fingerprint))
                    {service.RecordAuto("recovery",new{state,plan=take.Plan});Track(()=>GameActions.ClaimReward(take.Button));}
                    return;
                }
                var uncertain=plans.FirstOrDefault(r=>r.Plan.Action=="manual");
                if(uncertain.Button!=null){Manual("取回牌需判断："+uncertain.Plan.CardName+"。"+uncertain.Plan.Reasons[0]);return;}
                var unsupportedReward=buttons.FirstOrDefault(b=>b.Reward is not PotionReward && !IsStolenCardRecovery(b.Reward) && !LeaveHarmfulFakeRelic(b.Reward));
                if(unsupportedReward!=null)
                {Manual("此奖励尚未适配："+unsupportedReward.Reward?.Description.GetFormattedText()+"（"+unsupportedReward.Reward?.GetType().Name+"），请手动处理。");return;}
                // Full potion belt: leave the excess potion behind, never discard or replace one.
                var proceed=(NClickableControl)GameActions.Field(rewards,"_proceedButton")!;
                if(plans.Length>0)
                {
                    if(GameActions.Field(rewards,"_skipDisallowed") is true){Manual("建议不取回，但此奖励界面禁止跳过，请手动处理。");return;}
                    Wait("接管中 · 不取回 "+string.Join("、",plans.Select(p=>p.Plan.CardName))+"，保留免费删牌收益");
                    if(Claim("leave-recovery:"+rewards.GetInstanceId()+":"+fingerprint,proceed.IsEnabled && proceed.IsVisibleInTree()))
                    {service.RecordAuto("recovery_leave",new{state,plans=plans.Select(p=>p.Plan).ToArray()});GameActions.Invoke(rewards,"OnProceedButtonPressed",proceed);}
                    return;
                }
                Proceed(rewards,proceed,"OnProceedButtonPressed");return;
            }
            Wait("接管中 · 领取奖励（遗物全拿，药水有空位才拿）");
            if(Claim("reward:"+safe.GetInstanceId()))
            {if(safe.Reward is RelicReward {Relic:not null} pickup)PrepareRelicSelection(pickup.Relic);Track(()=>GameActions.ClaimReward(safe));}return;
        }
        if(service.State.CurrentRoom is MerchantRoom merchant && screen is NMerchantRoom or NMerchantInventory)
        {
            var room=NMerchantRoom.Instance;
            if(room==null){gate.ResetPending();return;}
            if(shoppingRoom!=merchant){shoppingRoom=merchant;shopArrivalGold=service.Player.Gold;shopBoughtCards=0;}
            if(screen is NMerchantRoom)
            {
                if(FullAutomatic&&leavingShop==room.GetInstanceId())
                {Proceed(room,room.ProceedButton,"HideScreen");return;}
                // Open once. When the user closes the inventory, leave control with them.
                if(Claim("shop-open:"+room.GetInstanceId(),room.MerchantButton.IsEnabled))room.OpenInventory();
                else if(WaitingForManual) return;
                return;
            }
            var inventory=(NMerchantInventory)screen;
            if(!inventory.IsOpen || GameActions.Field(inventory,"_isInputBlocked") is true){gate.ResetPending();return;}
            var slots=inventory.GetAllSlots().Where(s=>s.IsVisibleInTree() && s.Hitbox.IsEnabled && s.Entry is {IsStocked:true}).ToArray();
            var removal=slots.FirstOrDefault(s=>s.Entry is MerchantCardRemovalEntry);
            var cardsInShop=slots.Where(s=>s.Entry is MerchantCardEntry {CreationResult:not null}).ToArray();
            var state=SnapshotReader.Read(service.State,service.Player);
            if(TickPriorityShop(room,slots,state))return;
            var finalShop=FullAutomatic&&service.State.CurrentMapPoint is {} shopPoint&&
                ShopBudget.Reserve(state,RouteAdapter.Nodes(service.State),RouteAdapter.Key(shopPoint),100)==0;
            var plan=ShopPolicy.Evaluate(state,cardsInShop.Select(s=>new ShopCard(s.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MerchantCardEntry)s.Entry).CreationResult!.Card),s.Entry.Cost)).ToArray(),
                removal?.Entry.Cost,shopArrivalGold,shopBoughtCards,finalShop?0:null);
            if(FullAutomatic)plan=plan with{Notes=[plan.Notes[0],
                $"全局接管：删牌/买牌后随机买遗物，最后补空药水栏；遗物/药水购买后预留 {(finalShop?0:ClimbController.RelicReserve)} 金币。"]};
            service.ShowShop(state,plan);
            var choice=plan.Recommendation;
            if(choice==null)
            {
                if(FullAutomatic){TickClimbShop(room,inventory,slots,state);return;}
                Manual("商店删牌/购牌评估完成；遗物和药水购买请手动处理，离店后自动继续。");return;
            }
            var slot=choice.Kind=="remove"?removal:cardsInShop.FirstOrDefault(s=>s.GetInstanceId().ToString()==choice.Key);
            if(slot==null){gate.ResetPending();return;}
            Wait($"接管中 · { (choice.Kind=="remove"?"删牌":"购买") } {choice.Name}（{choice.Price} 金币）");
            var key=$"shop:{room.GetInstanceId()}:{slot.GetInstanceId()}:{service.Player.Gold}:{shopBoughtCards}:{string.Join(',',service.Player.Deck.Cards.Select(c=>c.Id.ToString()))}:{choice.Price}:{choice.Key}";
            if(Claim(key,slot.Entry.EnoughGold,8000))
            {
                if(choice.Kind=="remove")removalTarget=service.Player.Deck.Cards[choice.DeckIndex];
                service.RecordAuto("shop",new{state,plan});
                Track(()=>BuyShopSlot(slot,choice.Kind=="card"));
            }
            return;
        }
        if(screen is NMapScreen map)
        {
            if(CombatManager.Instance.IsInProgress && !map.IsTravelEnabled){Manual("正在查看战斗中的地图，请手动返回战斗。");return;}
            var legal=RouteAdapter.Legal(map);
            if(legal.Length==0){gate.ResetPending();Wait("接管中 · 等待地图允许前进");return;}
            var evaluation=RouteAdapter.Evaluate(service,legal);
            var target=legal.FirstOrDefault(p=>RouteAdapter.Key(p.Point)==evaluation.RecommendedNode);
            if(target==null){Manual("地图未找到可执行路线。"+string.Join("；",evaluation.Notes));return;}
            service.ShowRoute(evaluation);
            Wait("接管中 · 即将前往 "+RouteName(evaluation.Options.Single(o=>o.Node==evaluation.RecommendedNode).Kind));
            var key=$"route:{service.State.CurrentActIndex}:{service.State.CurrentMapCoord}:{target.Point.coord}";
            if(Claim(key+":"+System.Text.Json.JsonSerializer.Serialize(evaluation)))
            {
                service.RecordAuto("route",evaluation);
                service.RecordAuto("route_context",new{state=SnapshotReader.Read(service.State,service.Player),nodes=RouteAdapter.Nodes(service.State),legal=legal.Select(p=>RouteAdapter.Key(p.Point)).ToArray(),economy=RouteAdapter.Economy(service)});
                map.OnMapPointSelectedLocally(target);
            }return;
        }
        if(screen is NMapRoom && NMapScreen.Instance is {} mapScreen)
        {Wait("接管中 · 打开地图");if(Claim("map-open:"+((NMapRoom)screen).GetInstanceId()))mapScreen.Open();return;}
        var combat=CombatManager.Instance.DebugOnlyGetState();
        if(CombatManager.Instance.IsInProgress && combat!=null && (screen is NCombatRoom || screen is NEventRoom {EmbeddedCombatRoom:not null}))
        {
            if(blockedCombat==combat){Manual("求解器已主动停止；请处理战斗或手动重新开始接管。");return;}
            unsupported=null;
            if(ownedCombat==combat)
            {
                if(!solver.FullAuto) {blockedCombat=combat;Manual("战斗求解器已停止，请检查其提示后再接管。");return;}
                if(solver.TryAdoptAfterBudget(service.Player.Creature.CurrentHp,service.Player.Creature.MaxHp,Time.GetTicksMsec(),out var budget))
                    service.RecordAuto("solver_adopt",new{budgetSeconds=budget,state=SnapshotReader.Read(service.State,service.Player)});
                Wait("接管中 · 战斗交给求解器");return;
            }
            var error=solver.Bind();if(error!=null){Manual(error);return;}
            if(solver.FullAuto)
            {
                ownedCombat=combat;service.RecordAuto("combat_handoff",new{source="solver_auto_start"});
                Wait("接管中 · 战斗交给求解器");return;
            }
            if(Claim("combat:"+enableGeneration+":"+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(combat),solver.CanStart(combat)))
            {
                solver.Start(combat);
                if(!solver.FullAuto){Pause("求解器拒绝接管当前战斗。");return;}
                ownedCombat=combat;service.RecordAuto("combat_handoff",new{});
            }
            Wait("接管中 · 等待战斗求解器就绪");return;
        }
        if(!CombatManager.Instance.IsInProgress) ownedCombat=null;
        gate.ResetPending();
        var name=screen?.GetType().Name??"Transition";
        if(unsupported!=name){unsupported=name;unsupportedSince=Time.GetTicksMsec();}
        if(Time.GetTicksMsec()-unsupportedSince>3000) Manual("事件、商店或当前界面需手动处理。");
    }
    private void Proceed(Node owner,NClickableControl button,string method)
    {
        Wait("接管中 · 当前房间已处理，继续前进");
        if(Claim("proceed:"+owner.GetInstanceId(),button.IsEnabled && button.IsVisibleInTree()))GameActions.Invoke(owner,method,button);
    }
    private async Task BuyShopSlot(NMerchantSlot slot,bool isCard)
    {
        var before=service.Player.Deck.Cards.ToHashSet();
        if(slot.Entry is MerchantRelicEntry {Model:not null} relic)PrepareRelicSelection(relic.Model);
        await (Task)GameActions.Invoke(slot,"OnSelected")!;
        ownedPickup=null;ownedPickupRoom=null;
        if(isCard && service.Player.Deck.Cards.Any(c=>!before.Contains(c)))shopBoughtCards++;
    }
    internal static bool IsStolenCardRecovery(Reward? reward)=>reward?.GetType()==typeof(SpecialCardReward) &&
        GameActions.Field(reward,"_customDescriptionEncounterSourceId") is ModelId id &&
        id==ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.ThievingHopperWeak>().Id;
    internal static string RouteName(string kind)=>kind switch{"RestSite"=>"火堆","Elite"=>"精英","Monster"=>"普通战","Unknown"=>"问号","Shop"=>"商店","Treasure"=>"宝箱","Boss"=>"首领","MiniBoss"=>"骷髅小首领","Ancient"=>"先古",_=>kind};
}
