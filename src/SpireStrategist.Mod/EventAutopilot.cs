using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private EventModel? ownedEvent;
    private string eventSelection="";
    private ulong eventSelectorId;
    private CardModel[]? eventSelectionTargets;
    private NRestSiteRoom? ownedRestSelection;
    private bool ownsDig;
    internal void RestRelicGenerated(MegaCrit.Sts2.Core.Entities.Players.Player player,RelicModel relic)
    {
        if(Enabled&&ownsDig&&player==service.Player&&ownedRestSelection==service.RestRoom)
            PrepareRelicSelection(relic);
    }
    private RelicModel? ownedPickup;
    private object? ownedPickupRoom;
    private bool OwnsPickup=>ownedPickup!=null&&ReferenceEquals(ownedPickupRoom,service.State.CurrentRoom);
    private void PrepareRelicSelection(RelicModel relic)
    {
        ownedPickup=relic;ownedPickupRoom=service.State.CurrentRoom;eventSelectionTargets=null;
        eventSelection=relic.Id.Entry switch
        {
            "EMPTY_CAGE" or "PRECISE_SCISSORS" or "PRESERVED_FOG" or "PRECARIOUS_SHEARS" or "BIIIG_HUG"=>"remove",
            "ASTROLABE" or "NEW_LEAF" or "CLAWS"=>"transform",
            "DOLLYS_MIRROR"=>"duplicate",
            _=>RelicValue.Evaluate(relic.Id.Entry,SnapshotReader.Read(service.State,service.Player)).Selection
        };
    }
    private bool OwnsCurrentEvent=>ownedEvent!=null&&service.State.CurrentRoom is EventRoom current&&current.CanonicalEvent.Id==ownedEvent.Id;
    private bool TickEvent(object? screen)
    {
        if(CombatManager.Instance.IsInProgress)return false;
        if(!OwnsPickup){ownedPickup=null;ownedPickupRoom=null;}
        if(!OwnsCurrentEvent){ownedEvent=null;if(ownedRestSelection==null&&!OwnsPickup)eventSelectionTargets=null;}
        // Resuming takeover after a manually obtained Neow paperweight (also from Bones).
        if(!CombatManager.Instance.IsInProgress&&!OwnsCurrentEvent&&!OwnsPickup&&screen is NChooseACardSelectionScreen
            &&service.State.CurrentRoom is EventRoom {CanonicalEvent:MegaCrit.Sts2.Core.Models.Events.Neow}
            &&service.Player.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.LeadPaperweight>().FirstOrDefault() is {} paperweight)
            PrepareRelicSelection(paperweight);
        // Scroll Boxes may also be obtained manually or rolled by Neow's Bones.
        // Only adopt its native bundle screen at Neow; never seize arbitrary mod UI.
        if(!OwnsCurrentEvent&&!OwnsPickup&&screen is NChooseABundleSelectionScreen
            &&service.State.CurrentRoom is EventRoom {CanonicalEvent:MegaCrit.Sts2.Core.Models.Events.Neow}
            &&service.Player.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.ScrollBoxes>().FirstOrDefault() is {} boxes)
            PrepareRelicSelection(boxes);
        if(TickCustomEvent(screen))return true;
        if((OwnsCurrentEvent || OwnsPickup || ownedRestSelection!=null&&ReferenceEquals(ownedRestSelection,service.RestRoom)) && screen is Control selector && selector is NDeckCardSelectScreen or NDeckUpgradeSelectScreen or NDeckTransformSelectScreen or NDeckEnchantSelectScreen or NSimpleCardSelectScreen)
        {SelectEventCards(selector);return true;}
        if(screen is not NEventRoom room)return false;
        var plan=EventAdapter.Read(service,room);
        if(plan==null)
        {
            if(GameActions.Field(room,"_event") is EventModel model && model.GetType().Assembly!=typeof(EventModel).Assembly)Manual("新增模组事件尚未适配，请手动选择。");
            else Wait("接管中 · 等待事件选项显示");
            return true;
        }
        service.PresentEvent(plan);
        var target=plan.Buttons.FirstOrDefault(b=>b.GetInstanceId().ToString()==plan.Evaluation.RecommendedKey);
        var fallback=false;
        if(target==null&&FullAutomatic)
        {
            // Exploration policy for unmodelled native branches, not a fake score.
            target=plan.Buttons.Where(b=>b.IsEnabled&&!b.Option.IsLocked&&
                b.Option.WillKillPlayer?.Invoke(service.Player)!=true&&
                plan.Choices.Any(c=>c.Key==b.GetInstanceId().ToString()&&c.Enabled&&!c.Lethal)&&
                (plan.Evaluation.Options.FirstOrDefault(o=>o.Key==b.GetInstanceId().ToString())?.Score??0)>-9999)
                .OrderByDescending(b=>plan.Evaluation.Options.FirstOrDefault(o=>o.Key==b.GetInstanceId().ToString())?.Score??-100)
                .ThenBy(b=>b.Option.TextKey,StringComparer.Ordinal).FirstOrDefault();
            fallback=target!=null;
        }
        if(target==null){Manual("该事件存在未核验分支或致死选项，请手动处理。");return true;}
        var choice=plan.Choices.Single(c=>c.Key==target.GetInstanceId().ToString());
        Wait("接管中 · 事件选择 "+choice.Name);
        var fingerprint=JsonSerializer.Serialize(new{plan.State,plan.Choices});
        if(Claim("event:"+target.GetInstanceId()+":"+fingerprint,target.IsEnabled&&target.MouseFilter!=Control.MouseFilterEnum.Ignore))
        {
            ownedEvent=plan.Model;eventSelection=choice.Selection;eventSelectionTargets=null;
            if(fallback)service.RecordAuto("event_fallback",new{eventId=plan.Model.Id.ToString(),textKey=target.Option.TextKey,
                reason="全局接管：优先已评分合法选项，否则按文本键稳定探索；不是已建模推荐",state=plan.State,evaluation=plan.Evaluation});
            service.RecordAuto("event",new{eventId=plan.Model.Id.ToString(),textKey=target.Option.TextKey,choice,state=plan.State,evaluation=plan.Evaluation});
            GameActions.Invoke(target,"OnRelease");
        }
        return true;
    }
    private void SelectEventCards(Control screen)
    {
        if(GameActions.Field(screen,"_cards") is not IEnumerable<CardModel> list || GameActions.Field(screen,"_prefs") is not CardSelectorPrefs prefs ||
            GameActions.Field(screen,"_selectedCards") is not HashSet<CardModel> selected)
        {Manual("事件选牌接口无法核验。");return;}
        var cards=list.ToArray();var state=service.WithForesight(SnapshotReader.Read(service.State,service.Player));var v=new StrategicValue(state);
        double Value(CardModel c)
        {
            var snap=SnapshotReader.ReadCard(c);
            if(screen is NDeckUpgradeSelectScreen)return v.Upgrade(snap);
            if(screen is NDeckEnchantSelectScreen && GameActions.Field(screen,"_enchantment") is EnchantmentModel enchant)
                return v.Enchant(snap,enchant.Id.Entry,Convert.ToDouble(GameActions.Field(screen,"_enchantmentAmount")));
            if(eventSelection=="transform"&&prefs.MaxSelect==1&&TransformForecast.Supports(ownedEvent))
                return TransformForecast.Read(ownedEvent!,c,state).Value;
            if(eventSelection=="transform"&&prefs.MaxSelect==1&&ownedPickup is MegaCrit.Sts2.Core.Models.Relics.NewLeaf)
                return TransformForecast.ReadNewLeaf(c,state).Value;
            if(screen is NDeckTransformSelectScreen)return v.Transform(snap);
            if(screen is NSimpleCardSelectScreen)return v.Add(snap);
            if(eventSelection=="duplicate")return v.Add(snap);
            if(eventSelection=="pael-upgrade")return v.Upgrade(snap);
            return eventSelection is "remove" or "neow-remove-one" or "neow-remove-pair"?v.Remove(snap):eventSelection=="transform"?v.Transform(snap):double.NaN;
        }
        if(eventSelectorId!=screen.GetInstanceId()||eventSelectionTargets==null)
        {
            if(selected.Count!=0){Manual("检测到你已开始事件选牌，保留人工选择。");return;}
            var ordered=cards.Select((c,i)=>(Card:c,Index:i,Value:Value(c))).OrderByDescending(x=>x.Value).ThenBy(x=>x.Index).ToArray();
            if(ordered.Any(x=>!double.IsFinite(x.Value))){Manual("事件选择的用途尚未核验。");return;}
            var count=prefs.MinSelect==prefs.MaxSelect?prefs.MaxSelect:Math.Clamp(ordered.Count(x=>x.Value>0),prefs.MinSelect,prefs.MaxSelect);
            if(cards.Length<count){Manual("可选卡数量不足，等待游戏处理。");return;}
            eventSelectionTargets=ordered.Take(count).Select(x=>x.Card).ToArray();eventSelectorId=screen.GetInstanceId();
            if(eventSelection is "neow-remove-one" or "neow-remove-pair")
                eventSelectionTargets=NeowPolicy.RemovalPlan(state,cards.Select(SnapshotReader.ReadCard).ToArray(),count).Select(i=>cards[i]).ToArray();
            if(eventSelection=="pael-upgrade")
            {
                var plan=PaelsToothPolicy.Plan(state,cards.Select(SnapshotReader.ReadCard).ToArray());
                if(count!=5||plan.Indices.Length!=5){Manual("佩尔之牙的可选牌或数量发生变化，请手动处理。");return;}
                eventSelectionTargets=plan.Indices.Select(i=>cards[i]).ToArray();
                service.RecordAuto("paels_tooth_plan",new{plan,state,chosen=eventSelectionTargets.Select(SnapshotReader.ReadCard)});
            }
            service.RecordAuto("event_card_plan",new{eventId=ownedEvent?.Id.ToString()??ownedPickup?.Id.ToString()??"REST",purpose=eventSelection,screen=screen.GetType().Name,enchantment=(GameActions.Field(screen,"_enchantment") as EnchantmentModel)?.Id.ToString(),min=prefs.MinSelect,max=prefs.MaxSelect,candidates=ordered.Select(x=>new{card=SnapshotReader.ReadCard(x.Card),score=x.Value,predictedBaseCard=eventSelection=="transform"&&prefs.MaxSelect==1&&TransformForecast.Supports(ownedEvent)?TransformForecast.Read(ownedEvent!,x.Card,state).Result:null}),chosen=eventSelectionTargets.Select(c=>c.Id.ToString())});
        }
        if(selected.Any(c=>!eventSelectionTargets.Contains(c))||eventSelectionTargets.Any(c=>!cards.Contains(c)))
        {Manual("事件选牌目标已被更改，保留人工操作。");return;}
        var next=eventSelectionTargets.FirstOrDefault(c=>!selected.Contains(c));
        if(next!=null)
        {
            Wait("接管中 · 事件选牌 "+next.Title);
            if(Claim($"event-card:{eventSelectorId}:{Array.IndexOf(cards,next)}"))GameActions.Invoke(screen,"OnCardClicked",next);
            return;
        }
        Wait("接管中 · 确认事件选牌");
        // Native enchant confirmation ignores an empty selection. Optional zero-target
        // selections must finish through the native close path, even if Cancelable=false.
        if(screen is NDeckEnchantSelectScreen && eventSelectionTargets.Length==0 && prefs.MinSelect==0)
        {
            if(Claim("event-empty-enchant:"+eventSelectorId))
                GameActions.Invoke(screen,"CloseSelection",GameActions.Field(screen,"_closeButton"));
            return;
        }
        var confirmNames=screen is NDeckUpgradeSelectScreen or NDeckEnchantSelectScreen?
            new[]{"_singlePreviewConfirmButton","_multiPreviewConfirmButton"}:new[]{"_previewConfirmButton"};
        var confirm=confirmNames.Select(n=>GameActions.Field(screen,n)).OfType<NClickableControl>().FirstOrDefault(b=>b.IsEnabled&&b.IsVisibleInTree());
        if(confirm!=null)
        {
            var method=screen is NDeckTransformSelectScreen?"CompleteSelection":"ConfirmSelection";
            if(Claim("event-confirm:"+eventSelectorId))GameActions.Invoke(screen,method,confirm);
            return;
        }
        if(GameActions.Field(screen,"_confirmButton") is NClickableControl preview&&preview.IsEnabled&&preview.IsVisibleInTree())
        {
            if(Claim("event-preview:"+eventSelectorId))
            {
                if(screen is NSimpleCardSelectScreen)GameActions.Invoke(screen,"CompleteSelection");
                else if(screen is NDeckTransformSelectScreen)GameActions.Invoke(screen,"ConfirmSelection",preview);
                else GameActions.PreviewSelection(screen,preview);
            }
        }
    }
}
