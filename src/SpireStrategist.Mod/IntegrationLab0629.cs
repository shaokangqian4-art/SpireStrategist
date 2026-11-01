using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using System.Text.Json;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckShopReward0629(RunState state,AdvisorService service)
    {
        var player=service.Player;
        // Isolated fixture only: compare read-only predictions with actual native pulls.
        for(var i=0;i<12;i++)
        {
            var rng=player.PlayerRng.Rewards.ToSerializable();
            var before=JsonSerializer.Serialize(player.RelicGrabBag.ToSerializable(),Journal.Json);
            var shared=JsonSerializer.Serialize(state.SharedRelicGrabBag.ToSerializable(),Journal.Json);
            var predicted=ThisOrThatForecast.Read(player);
            Check(predicted!=null,"ThisOrThat native relic prediction available");
            Check(rng.Equals(player.PlayerRng.Rewards.ToSerializable())&&
                before==JsonSerializer.Serialize(player.RelicGrabBag.ToSerializable(),Journal.Json)&&
                shared==JsonSerializer.Serialize(state.SharedRelicGrabBag.ToSerializable(),Journal.Json),
                "ThisOrThat forecast preserves Rewards and both real relic bags");
            Check(RelicFactory.PullNextRelicFromFront(player).Id==predicted!.Id,"ThisOrThat forecast matches native pull");
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
        var room=NMerchantRoom.Instance!;room.OpenInventory();
        await Until(()=>Screen() is NMerchantInventory,"Orrery fixture foreground shop");
        var slots=room.Inventory.GetAllSlots().ToArray();
        foreach(var slot in slots)
        {
            if(slot.Entry is MerchantCardEntry card)AccessTools.Property(card.GetType(),"CreationResult").SetValue(card,null);
            if(slot.Entry is MerchantCardRemovalEntry removal)removal.SetUsed();
            if(slot.Entry is MerchantRelicEntry relic)AccessTools.Property(relic.GetType(),"Model").SetValue(relic,null);
            if(slot.Entry is MerchantPotionEntry)AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(slot.Entry,99999);
        }
        var entry=slots.Select(s=>s.Entry).OfType<MerchantRelicEntry>().First();
        GameActions.Invoke(entry,"SetModel",ModelDb.Relic<Orrery>().ToMutable());
        AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(entry,120);
        foreach(var slot in slots){slot.Visible=slot.Entry.IsStocked;GameActions.Invoke(slot,"UpdateVisual");}
        player.Gold=300;ClimbController.SetEnabled(true);
        await Until(()=>Screen() is NCardRewardSelectionScreen,"Orrery purchase opens native child card reward",30);
        service.Panel.SetReviewPaused(true);await Delay(.7);
        Check(room.Inventory.IsOpen&&service.Decision?.Kind=="cards","foreground reward is evaluated over still-open shop");
        service.Panel.SetReviewPaused(false);
        await Until(()=>Screen() is NMapScreen,"all Orrery rewards complete and shop exits",75);
        Check(player.Gold==180&&player.GetRelic<Orrery>()!=null,"Orrery purchased once without duplicate spending");
        Check(service.Autopilot.Enabled&&!service.Autopilot.WaitingForManual,"automatic control survives nested shop rewards");
        ClimbController.Stop("synthetic shop fixture finished");
    }
}
