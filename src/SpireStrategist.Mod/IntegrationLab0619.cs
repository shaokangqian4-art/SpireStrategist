using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckMenu0619(NGame game)
    {
        var asc=(SpinBox)AccessTools.Field(typeof(ClimbController),"ascensionInput").GetValue(null)!;
        var chars=(OptionButton)AccessTools.Field(typeof(ClimbController),"characterInput").GetValue(null)!;
        var enabled=(CheckButton)AccessTools.Field(typeof(ClimbController),"enabledButton").GetValue(null)!;
        var path=Path.Combine(OS.GetUserDataDir(),"SpireStrategist","climb-settings.json");
        Check(asc.Value==0&&ClimbPreferences.Load(path).Ascension==0,"fresh install uses N0");
        Check(chars.ItemCount==5&&Enumerable.Range(1,4).All(i=>chars.GetItemText(i).Contains(UiLocalization.English?"unavailable":"暂不可用")),"five characters with four unavailable labels");
        chars.Select(3);chars.EmitSignal(OptionButton.SignalName.ItemSelected,3L);
        Check(enabled.Disabled&&ClimbPreferences.Load(path).Character=="Necrobinder","unavailable character saved and cannot start loop");
        ClimbController.SetEnabled(true);
        Check(!ClimbController.Enabled&&AdvisorService.Current==null,"programmatic unavailable launch rejected");
        chars.Select(0);chars.EmitSignal(OptionButton.SignalName.ItemSelected,0L);
        await CheckMenu066(game);
        var loaded=ClimbPreferences.Load(path);
        Check(loaded.Ascension==10&&loaded.Character=="Ironclad","N10 menu preference survives launch stop and reload from disk");
    }

    private static async Task CheckGlobalShop0619(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        Check(state.GameMode==GameMode.Custom,"real custom-mode run fixture");
        ClimbController.SetEnabled(true);
        Check(!ClimbController.Enabled,"custom mode still cannot start loop");
        await CreatureCmd.SetCurrentHp(p.Creature,p.Creature.MaxHp);
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);await Delay(.5);
        var room=NMerchantRoom.Instance!;room.OpenInventory();
        await Until(()=>Screen() is NMerchantInventory,"custom shop opened");
        var inventory=(NMerchantInventory)Screen()!;
        var slots=inventory.GetAllSlots().ToArray();
        foreach(var slot in slots)
        {
            if(slot.Entry is MerchantCardEntry c)AccessTools.Property(c.GetType(),"CreationResult").SetValue(c,null);
            if(slot.Entry is MerchantCardRemovalEntry removal)removal.SetUsed();
            if(slot.Entry is MerchantRelicEntry r)AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
        }
        var relic=slots.Select(s=>s.Entry).OfType<MerchantRelicEntry>().First();
        GameActions.Invoke(relic,"SetModel",ModelDb.Relic<Vajra>().ToMutable());
        AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(relic,120);
        var potions=slots.Select(s=>s.Entry).OfType<MerchantPotionEntry>().ToArray();
        foreach(var potion in potions)AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(potion,50);
        foreach(var slot in slots){slot.Visible=slot.Entry.IsStocked;GameActions.Invoke(slot,"UpdateVisual");}
        p.Gold=270;
        var relicCount=p.Relics.Count;var potionCount=p.Potions.Count();
        auto.Toggle();Check(auto.FullAutomatic&&!ClimbController.Enabled,"ordinary start enters single-run full takeover");
        await Delay(3);Check(p.Gold==270&&p.Relics.Count==relicCount,"ordinary purchase still waits more than 3 seconds");
        auto.Pause("cancel pending ordinary purchase");await Delay(6);
        Check(p.Gold==270&&p.Relics.Count==relicCount,"stop cancels purchase beyond original deadline");
        auto.Enable(true);service.Panel.SetReviewPaused(true);await Delay(9);
        Check(p.Gold==270,"review pause also blocks full shop");service.Panel.SetReviewPaused(false);
        await Until(()=>p.Relics.Count==relicCount+1,"ordinary full takeover purchases random relic",20);
        Check(p.Gold==150&&p.Potions.Count()==potionCount,"relic bought before final potion step");
        await Until(()=>Screen() is NMapScreen,"custom full takeover fills potion and exits shop",30);
        auto.Pause("fixture complete");
        Check(p.Gold==100&&p.Potions.Count()==potionCount+1,"one potion, reserve 100, no duplicated purchase");
        Check(!ClimbController.Enabled&&ClimbController.Batch.Results.Count==0,"single-run custom takeover does not create loop sample");
    }
}
