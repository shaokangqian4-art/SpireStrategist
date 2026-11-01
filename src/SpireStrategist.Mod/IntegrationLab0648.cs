using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckBridge0648(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        auto.Pause("0648 fixture");NMapScreen.Instance!.Close(false);
        foreach(var card in p.Deck.Cards.ToArray())await CardPileCmd.RemoveFromDeck(card,false);
        for(var i=0;i<4;i++)await CardPileCmd.Add(state.CreateCard<DefendDefect>(p),PileType.Deck);
        for(var i=0;i<3;i++)await CardPileCmd.Add(state.CreateCard<StrikeDefect>(p),PileType.Deck);
        foreach(var card in new CardModel[]{state.CreateCard<AdaptiveStrike>(p),state.CreateCard<Hologram>(p),state.CreateCard<BulkUp>(p),state.CreateCard<Supercritical>(p)})
            await CardPileCmd.Add(card,PileType.Deck);
        await CreatureCmd.SetCurrentHp(p.Creature,50);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<SlipperyBridge>(),showTransition:false);
        await Delay(1);
        var room=NEventRoom.Instance!;var e=(SlipperyBridge)GameActions.Field(room,"_event")!;
        var core=p.Deck.Cards.OfType<Supercritical>().Single();
        AccessTools.Field(typeof(SlipperyBridge),"_randomCardToLose").SetValue(e,core);
        var s=SnapshotReader.Read(state,p);BridgePlan? best=null;BridgeStop[] sequence=[];
        // Advance ONLY this disposable lab event to exercise the reported two-step trap.
        for(var i=0;i<256;i++)
        {
            sequence=BridgeForecast.Read(e,s);best=BridgePolicy.BestContinuation(s,sequence);
            if(best is {Stop.Steps:2}&&sequence[0].Card.Rarity!="Basic"&&best.Stop.Card.Rarity=="Basic")break;
            e.Rng.NextInt(100);best=null;
        }
        Check(best!=null,"native fixture includes core -> useful card -> basic target");
        var before=e.Rng.ToSerializable();var skipped=GameActions.Field(e,"_skippedRemovals");
        var hidden=JsonSerializer.Serialize(HiddenReader.Read(state,p).Streams);
        var deck=p.Deck.Cards.ToArray();
        for(var i=0;i<20;i++)EventAdapter.Read(service,room);
        Check(before.Equals(e.Rng.ToSerializable())&&ReferenceEquals(skipped,GameActions.Field(e,"_skippedRemovals"))
            &&hidden==JsonSerializer.Serialize(HiddenReader.Read(state,p).Streams)&&p.Deck.Cards.SequenceEqual(deck),"20 bridge forecasts preserve real RNG deck and skipped candidates");
        var plan=EventAdapter.Read(service,room)!;
        Check(plan.Buttons.Single(b=>b.GetInstanceId().ToString()==plan.Evaluation.RecommendedKey).Option.TextKey.Contains("HOLD_ON"),"bridge advice spends HP instead of removing Supercritical");
        Check(plan.Choices.Single(c=>c.Key==plan.Evaluation.RecommendedKey).HpLoss==7,"forecast quotes cumulative 3+4 HP");
        for(var i=0;i<2;i++)
        {
            plan=EventAdapter.Read(service,room)!;
            var button=plan.Buttons.Single(b=>b.GetInstanceId().ToString()==plan.Evaluation.RecommendedKey);
            Check(button.Option.TextKey.Contains("HOLD_ON"),"reevaluation continues planned safe reroll "+i);
            GameActions.Invoke(button,"OnRelease");await Delay(1);
            var actual=(CardModel)GameActions.Field(e,"_randomCardToLose")!;
            Check(actual.Id.ToString()==sequence[i].Card.Id,"predicted bridge candidate equals native candidate "+i);
        }
        Check(p.Creature.CurrentHp==43,"native bridge actually pays 3 then 4 HP");
        plan=EventAdapter.Read(service,room)!;
        Check(plan.Buttons.Single(b=>b.GetInstanceId().ToString()==plan.Evaluation.RecommendedKey).Option.TextKey.EndsWith("OVERCOME"),"stop at the predicted basic card");
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"bridge autopilot completes",60);
        Check(p.Deck.Cards.Contains(core)&&p.Deck.Cards.Count==deck.Length-1&&p.Creature.CurrentHp==43,"auto removes one basic and retains core without further HP payments");
        auto.Pause("0648 done");NMapScreen.Instance!.Close(false);
        // Verify the native fallback pool after all cards have been skipped.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<SlipperyBridge>(),showTransition:false);await Delay(1);
        room=NEventRoom.Instance!;e=(SlipperyBridge)GameActions.Field(room,"_event")!;
        AccessTools.Field(typeof(SlipperyBridge),"_skippedRemovals").SetValue(e,p.Deck.Cards.ToHashSet());
        var next=BridgeForecast.Read(e,SnapshotReader.Read(state,p))[0];
        var hold=EventAdapter.Read(service,room)!.Buttons.Single(b=>b.Option.TextKey.Contains("HOLD_ON"));
        GameActions.Invoke(hold,"OnRelease");await Delay(1);
        Check(((CardModel)GameActions.Field(e,"_randomCardToLose")!).Id.ToString()==next.Card.Id,"exhausted skipped pool matches native all-removable fallback");
    }
}
