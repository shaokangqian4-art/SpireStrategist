using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckReview0672(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        foreach(var owned in new[]{true,false})
        {
            auto.Pause("bundle fixture");
            if(p.GetRelic<ScrollBoxes>() is {} old)await RelicCmd.Remove(old);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);await Delay(.5);
            if(owned)AccessTools.Field(typeof(PartialAutopilot),"ownedEvent").SetValue(auto,GameActions.Field(NEventRoom.Instance!,"_event"));
            var before=p.Deck.Cards.Count;
            var task=RelicCmd.Obtain(ModelDb.Relic<ScrollBoxes>().ToMutable(),p);
            await Until(()=>Screen() is NChooseABundleSelectionScreen,"0672 native bundle visible "+owned);
            auto.Enable(true);
            await Until(()=>task.IsCompleted,"0672 bundle confirmed "+(owned?"owned event":"manual acquisition then takeover"),45);
            await task;auto.Pause("bundle finished");
            Check(p.Deck.Cards.Count==before+3,"0672 obtains exactly one native pack "+owned);
        }
        // The new recovery path still leaves an explicitly chosen player preview alone.
        await RelicCmd.Remove(p.GetRelic<ScrollBoxes>()!);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);await Delay(.5);
        var pending=RelicCmd.Obtain(ModelDb.Relic<ScrollBoxes>().ToMutable(),p);
        await Until(()=>Screen() is NChooseABundleSelectionScreen,"0672 manual preview fixture");
        var screen=(NChooseABundleSelectionScreen)Screen()!;
        var bundle=GameActions.Descendants<NCardBundle>(screen).First(n=>n.IsVisibleInTree());
        GameActions.Invoke(screen,"OnBundleClicked",bundle);auto.Enable(true);
        await Until(()=>auto.WaitingForManual,"0672 manual preview respected");
        Check(!pending.IsCompleted,"0672 never confirms a player's preselected bundle");auto.Pause("preview cleanup");
        GameActions.Invoke(screen,"ConfirmSelection",GameActions.Field(screen,"_previewConfirmButton"));await pending;

        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        await CardPileCmd.Add(state.CreateCard<StrikeRegent>(p),PileType.Deck);
        await RelicCmd.Obtain(ModelDb.Relic<BookOfFiveRings>().ToMutable(),p);
        var book=p.GetRelic<BookOfFiveRings>()!;
        Check(book.CardsAdded==0,"0672 five rings does not retroactively count earlier purchases");
        await CreatureCmd.SetCurrentHp(p.Creature,20);
        for(var i=0;i<5;i++)await CardPileCmd.Add(state.CreateCard<StrikeRegent>(p),PileType.Deck);
        Check(book.CardsAdded==5&&p.Creature.CurrentHp==40,"0672 book acquired before cards counts five and heals twenty");
        await RelicCmd.Obtain(ModelDb.Relic<StoneHumidifier>().ToMutable(),p);
        await CreatureCmd.SetCurrentHp(p.Creature,(int)(p.Creature.MaxHp*.7m));
        var snap=SnapshotReader.Read(state,p);
        Check(CampMechanics.HealExtra(service,snap)<new StrategicValue(snap).MaxHp(5)*.3,"0672 native humidifier bonus discounted at healthy HP");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<HungryForMushrooms>(),showTransition:false);await Delay(.5);
        var eventPlan=EventAdapter.Read(service,NEventRoom.Instance!)!;
        var recommended=eventPlan.Buttons.Single(b=>b.GetInstanceId().ToString()==eventPlan.Evaluation.RecommendedKey);
        Check(recommended.Option.TextKey.EndsWith("FRAGRANT_MUSHROOM"),"0672 actual mushroom menu rejects unprotected large mushroom");
    }
}
