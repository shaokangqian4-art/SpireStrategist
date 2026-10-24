using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckParasol0616(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        async Task Trial(bool basics)
        {
            auto.Pause("parasol fixture setup");
            if(p.GetRelic<LordsParasol>() is {} old)await RelicCmd.Remove(old);
            if(!basics)
            {
                foreach(var c in p.Deck.Cards.Where(c=>c.IsBasicStrikeOrDefend).ToArray())await CardPileCmd.RemoveFromDeck(c,false);
                for(int i=0;i<4;i++)await CardPileCmd.Add(state.CreateCard<Anger>(p),PileType.Deck);
            }
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);await Delay(.5);
            var room=NMerchantRoom.Instance!;var inventory=room.Inventory.Inventory!;
            // Control only the stock so the fixture isolates the native parasol purchase/removal chain.
            var stock=basics?new RelicModel[]{ModelDb.Relic<Anchor>(),ModelDb.Relic<Vajra>(),ModelDb.Relic<OddlySmoothStone>()}:
                new RelicModel[]{ModelDb.Relic<BagOfPreparation>(),ModelDb.Relic<HornCleat>(),ModelDb.Relic<HappyFlower>()};
            var stockIndex=0;
            foreach(var e in inventory.RelicEntries)
                GameActions.Invoke(e,"SetModel",stock[stockIndex++%stock.Length].ToMutable());
            p.Gold=0;
            var relic=ModelDb.Relic<LordsParasol>().ToMutable();await RelicCmd.Obtain(relic,p);
            await relic.AfterRoomEntered(service.State.CurrentRoom!);
            await Until(()=>Screen() is NDeckCardSelectScreen,"parasol opens free mandatory removal",60);
            var selector=Screen();var count=p.Deck.Cards.Count;var removals=p.ExtraFields.CardShopRemovalsUsed;
            var gold=p.Gold;
            Check(inventory.CharacterCardEntries.Concat(inventory.ColorlessCardEntries).All(e=>!e.IsStocked),
                "native parasol grants stock before removal");
            await Delay(3);Check(ReferenceEquals(Screen(),selector),"disabled takeover leaves parasol selector open");
            ClimbController.SetEnabled(true);service.Panel.SetReviewPaused(true);await Delay(3);
            Check(ReferenceEquals(Screen(),selector),"review pause leaves parasol removal untouched");
            service.Panel.SetReviewPaused(false);await Delay(.5);ClimbController.Stop("cancel pending free removal");await Delay(3);
            Check(ReferenceEquals(Screen(),selector)&&p.Deck.Cards.Count==count&&
                ((HashSet<CardModel>)GameActions.Field(selector!,"_selectedCards")!).Count==0,"pause cancels pending free removal");
            ClimbController.SetEnabled(true);
            await Until(()=>Screen() is NMapScreen,"parasol removal resumes continuous map",60);
            Check(p.Deck.Cards.Count==count-1&&p.ExtraFields.CardShopRemovalsUsed==removals+1,
                "native free removal completes exactly once, basics="+basics);
            Check(p.Gold==gold,"parasol removal charges no gold and no duplicate paid purchase");
            Check(ClimbController.Enabled&&auto.FullAutomatic&&!auto.WaitingForManual,
                "continuous mode survives parasol shop, basics="+basics);
            ClimbController.Stop("fixture boundary");NMapScreen.Instance!.Close(false);await Delay(.3);
        }
        await Trial(true);
        await Trial(false);
    }
}
