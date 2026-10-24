using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckRecovery0614(RunState state,AdvisorService service)
    {
        var p=service.Player;
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Wellspring>(),showTransition:false);
        await RelicCmd.Obtain(ModelDb.Relic<PaelsClaw>().ToMutable(),p);
        Check(p.Deck.Cards.OfType<DefendIronclad>().All(c=>SnapshotReader.ReadCard(c).Enchantment=="ENCHANTMENT.GOOPY"),
            "native PaelsClaw creates Goopy defends matching reported blocker");
        async Task Recover(CardModel c,bool expected,bool mandatory=false)
        {
            // Prepare the same native reward used by the thief, without a simulated battle.
            await CardPileCmd.RemoveFromDeck(c,false);state.AddCard(c,p);
            var reward=new SpecialCardReward(c,p);
            reward.SetCustomDescriptionEncounterSource(ModelDb.Encounter<ThievingHopperWeak>().Id);
            var set=new RewardsSet(p).WithCustomRewards([reward]);
            if(mandatory)set.WithSkippingDisallowed();
            var task=set.Offer();
            await Until(()=>Screen() is NRewardsScreen,"native stolen reward appears");
            ClimbController.SetEnabled(true);
            Check(ClimbController.Enabled,"continuous mode armed for recovery");
            await Until(()=>task.IsCompleted,"continuous recovery settles "+c.Id,45);await task;
            Check(p.Deck.Cards.Count(x=>ReferenceEquals(x,c))==(expected?1:0),"recovery target taken exactly once or intentionally left "+c.Id);
            Check(ClimbController.Enabled&&service.Autopilot.FullAutomatic&&!service.Autopilot.WaitingForManual,
                "continuous mode remains active after recovery "+c.Id);
            ClimbController.Stop("isolated recovery fixture boundary");
            await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
            NMapScreen.Instance!.Close(false);await Delay(.3);
        }
        var ashen=state.CreateCard<AshenStrike>(p);
        await CardPileCmd.Add(ashen,PileType.Deck);
        await Recover(ashen,true);
        // A real native enchantment outside the supported card-profile modifiers.
        var unknown=state.CreateCard<Bash>(p);
        await CardPileCmd.Add(unknown,PileType.Deck);CardCmd.Enchant<Clone>(unknown,1);
        Check(RecoveryPolicy.Evaluate(SnapshotReader.Read(state,p),SnapshotReader.ReadCard(unknown)).Action=="manual",
            "ordinary mode still asks about genuinely unmodelled enchantment");
        await Recover(unknown,true);
        await CardPileCmd.RemoveFromDeck(unknown,false);
        // Plenty of replacement damage/block: preserve the intended free-removal feature.
        for(var i=0;i<6;i++)await CardPileCmd.Add(state.CreateCard<Anger>(p),PileType.Deck);
        for(var i=0;i<5;i++)await CardPileCmd.Add(state.CreateCard<ShrugItOff>(p),PileType.Deck);
        var basic=p.Deck.Cards.OfType<StrikeIronclad>().First();
        await Recover(basic,false);
        await CardPileCmd.Add(basic,PileType.Deck);
        await Recover(basic,true,true);
    }
}
