using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckShop069(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        async Task<NMerchantInventory> Shop(int gold,int removal)
        {
            auto.Pause("shop 069 fixture");
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);await Delay(.5);
            NMerchantRoom.Instance!.OpenInventory();
            await Until(()=>Screen() is NMerchantInventory,"open controlled shop");
            var inventory=(NMerchantInventory)Screen()!;
            foreach(var slot in inventory.GetAllSlots())
            {
                if(slot.Entry is MerchantCardEntry c)AccessTools.Property(c.GetType(),"CreationResult").SetValue(c,null);
                if(slot.Entry is MerchantRelicEntry r)AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
            }
            var removalEntry=NMerchantRoom.Instance.Inventory.Inventory!.CardRemovalEntry!;
            AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(removalEntry,removal);
            p.Gold=gold;
            await CreatureCmd.SetCurrentHp(p.Creature,70);
            return inventory;
        }
        void Refresh(NMerchantInventory inventory)
        {
            foreach(var slot in inventory.GetAllSlots())
            {
                // Fixture restocks entries cleared above. Native sold-out visuals
                // hide a slot permanently, so restore it when injecting new stock.
                slot.Visible=slot.Entry.IsStocked;
                GameActions.Invoke(slot,"UpdateVisual");
            }
        }
        CardModel StockOffering(NMerchantInventory inventory)
        {
            var entry=inventory.GetAllSlots().Select(s=>s.Entry).OfType<MerchantCardEntry>().First();
            var offering=state.CreateCard<Offering>(p);
            AccessTools.Property(entry.GetType(),"CreationResult").SetValue(entry,new CardCreationResult(offering));
            AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(entry,150);
            return offering;
        }
        async Task Finished(string label)
        {
            await Until(()=>Screen() is MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen,label+" completes",60);
            Check(auto.Enabled&&auto.FullAutomatic&&!auto.WaitingForManual,label+" auto exits shop");
            auto.Pause("shop 069 boundary");
            MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance!.Close(false);
        }
        var shop=await Shop(207,150);
        var relic=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().First();
        GameActions.Invoke(relic,"SetModel",ModelDb.Relic<GhostSeed>().ToMutable());
        AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(relic,204);
        var count=p.Deck.Cards.Count;var removals=p.ExtraFields.CardShopRemovalsUsed;
        Refresh(shop);auto.Enable(true);await Finished("GhostSeed priority");
        Check(p.GetRelic<GhostSeed>()!=null&&p.Gold==3&&p.Deck.Cards.Count==count&&p.ExtraFields.CardShopRemovalsUsed==removals,
            "207 gold buys GhostSeed 204 before removal 150, exactly once");
        await RelicCmd.Remove(p.GetRelic<GhostSeed>()!);

        shop=await Shop(200,100);
        var offering=StockOffering(shop);Refresh(shop);auto.Enable(true);
        await Finished("mutually exclusive Offering");
        Check(p.Deck.Cards.Contains(offering)&&p.Gold==50&&p.ExtraFields.CardShopRemovalsUsed==removals,
            "200 gold buys Offering 150 and forgoes removal 100 despite reserve");
        Check(p.Deck.Cards.Count==count+1,"offering purchased once without removing basic");
        await CardPileCmd.RemoveFromDeck(offering,false);

        shop=await Shop(300,100);offering=StockOffering(shop);Refresh(shop);auto.Enable(true);
        await Until(()=>p.Deck.Cards.Contains(offering)||p.ExtraFields.CardShopRemovalsUsed>removals,"first shop action");
        Check(p.Deck.Cards.Contains(offering)&&p.ExtraFields.CardShopRemovalsUsed==removals,"when both fit Offering still purchased first");
        await Finished("Offering followed by removal");
        Check(p.Deck.Cards.Contains(offering)&&p.Gold==50&&p.ExtraFields.CardShopRemovalsUsed==removals+1&&p.Deck.Cards.Count==count,
            "remaining funds re-evaluated and one basic removed through native selector");
    }
}
