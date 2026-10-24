using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckFeedback0610(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        var compatible=SolverBridge.CompatibilityError()==null;
        Check(SnapshotReader.Read(state,p).CombatSolverAvailable==compatible,"snapshot records compatible solver presence without assuming installed");
        async Task Event(EventModel canonical,int hp,string expected)
        {
            auto.Pause("feedback fixture");
            await CreatureCmd.SetCurrentHp(p.Creature,hp);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);await Delay(.6);
            var room=NEventRoom.Instance!;var plan=EventAdapter.Read(service,room)!;
            Check(plan.Buttons.Single(b=>b.GetInstanceId().ToString()==plan.Evaluation.RecommendedKey).Option.TextKey.EndsWith("."+expected),
                canonical.Id+" recommends "+expected);
            var before=JsonSerializer.Serialize(state.Rng.ToSerializable());
            var eventRng=plan.Model.Rng.ToSerializable();
            for(var i=0;i<10;i++)EventAdapter.Read(service,room);
            Check(before==JsonSerializer.Serialize(state.Rng.ToSerializable())&&eventRng.Equals(plan.Model.Rng.ToSerializable()),"event evaluation preserves RNG "+expected);
            auto.Enable(true);await Until(()=>Screen() is NMapScreen,"event completes "+expected,60);
            Check(auto.Enabled&&auto.FullAutomatic&&!auto.WaitingForManual,"event maintains automatic control "+expected);
            auto.Pause("feedback boundary");NMapScreen.Instance!.Close(false);
        }
        var max=p.Creature.MaxHp;
        TestRngInjector.SetRelicOverride<Vajra>();
        await Event(ModelDb.Event<UnrestSite>(),52,"KILL");
        Check(p.Creature.MaxHp==max-8&&p.Creature.CurrentHp==52&&p.GetRelic<Vajra>()!=null&&!p.Deck.Cards.Any(c=>c is PoorSleep),
            "max HP trade grants relic without current HP loss or curse");
        await Event(ModelDb.Event<UnrestSite>(),10,"REST");
        Check(p.Creature.CurrentHp==p.Creature.MaxHp&&p.Deck.Cards.Count(c=>c is PoorSleep)==1,"urgent rest heals and adds actual PoorSleep once");
        await CardPileCmd.RemoveFromDeck(p.Deck.Cards.Single(c=>c is PoorSleep),false);
        p.Gold=145;var gold=p.Gold;
        await Event(ModelDb.Event<SunkenStatue>(),65,"DIVE_INTO_WATER");
        Check(p.Gold>=gold+101&&p.Gold<=gold+121&&p.Creature.CurrentHp==58&&p.GetRelic<SwordOfStone>()==null,"healthy statue grants actual gold and loses seven HP");
        await Event(ModelDb.Event<SunkenStatue>(),10,"GRAB_SWORD");
        Check(p.Creature.CurrentHp==10&&p.GetRelic<SwordOfStone>()!=null,"low HP statue avoids damage");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        var card=state.CreateCard<Stoke>(p);
        var offered=new RewardsSet(p).WithCustomRewards([new CardReward(new CardModel[]{card},CardCreationSource.Encounter,p,CardCreationOptions.ForRoom(p,RoomType.Boss))]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"Stoke native reward");auto.Enable(true);
        await Until(()=>offered.IsCompleted,"Stoke selected",60);await offered;auto.Pause("feedback done");
        Check(p.Deck.Cards.Contains(card),"early unformed deck can actually acquire Stoke");
    }
}
