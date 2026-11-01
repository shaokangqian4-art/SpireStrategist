using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckFoulShop0638(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        foreach(var opened in new[]{false,true})
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);await Delay(.5);
            var room=NMerchantRoom.Instance!;
            // Empty stock isolates native potion conversion from subsequent spending.
            foreach(var slot in room.Inventory.GetAllSlots())
            {
                if(slot.Entry is MerchantCardEntry c)AccessTools.Property(c.GetType(),"CreationResult").SetValue(c,null);
                if(slot.Entry is MerchantCardRemovalEntry removal)removal.SetUsed();
                if(slot.Entry is MerchantRelicEntry r)AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
                if(slot.Entry is MerchantPotionEntry)AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(slot.Entry,999999);
                slot.Visible=slot.Entry.IsStocked;GameActions.Invoke(slot,"UpdateVisual");
            }
            var count=opened?2:1;
            for(var i=0;i<count;i++)await PotionCmd.TryToProcure(ModelDb.Potion<FoulPotion>().ToMutable(),p);
            Check(p.Potions.OfType<FoulPotion>().Count()==count,"fixture has actual Foul potions");
            p.Gold=100;var hp=p.Creature.CurrentHp;
            if(opened)room.OpenInventory();
            auto.Enable(true);service.Panel.SetReviewPaused(true);await Delay(3);
            Check(p.Gold==100&&p.Potions.OfType<FoulPotion>().Count()==count,"review pause blocks potion use");
            service.Panel.SetReviewPaused(false);
            await Until(()=>!p.Potions.OfType<FoulPotion>().Any()&&p.Gold==100+100*count,"native potion conversion",35);
            Check(p.Creature.CurrentHp==hp,"merchant conversion does not damage player");
            await Until(()=>Screen() is NMapScreen,"shop reopens and finishes after potion use",35);
            auto.Pause("fixture complete");await Delay(1);
            Check(p.Gold==100+100*count,"each potion converts exactly once");
        }
    }
}
