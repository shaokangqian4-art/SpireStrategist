using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckMenu0621(NGame game)
    {
        var shell=(PanelContainer)AccessTools.Field(typeof(ClimbController),"shell").GetValue(null)!;
        var collapse=(Button)AccessTools.Field(typeof(ClimbController),"collapseButton").GetValue(null)!;
        var content=(Control)AccessTools.Field(typeof(ClimbController),"contentScroll").GetValue(null)!;
        ClimbController.MoveFloatingPanel(new Vector2(10,0));ClimbController.SaveFloatingLayout();await Delay(.7);
        Check(shell.Position==new Vector2(10,0),"menu dock can occupy top edge and refresh preserves position");
        var saved=JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(Path.Combine(OS.GetUserDataDir(),"SpireStrategist","climb-panel-layout.json")),Journal.Json)!;
        Check(saved.X==10&&saved.Y==0,"menu position persisted");
        await ClickUi0620(collapse);await Delay(.7);
        Check(!content.IsVisibleInTree()&&shell.Size.Y<100,"menu collapse hides body and releases old input area");
        await Shot0620("10-menu-collapsed");
        saved=JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(Path.Combine(OS.GetUserDataDir(),"SpireStrategist","climb-panel-layout.json")),Journal.Json)!;
        Check(saved.Collapsed,"menu collapse persisted");
        await ClickUi0620(collapse);await Delay(.5);
        Check(content.IsVisibleInTree(),"menu native expand click restores controls");
        ClimbController.MoveFloatingPanel(new Vector2(100000,100000));await Delay(.6);
        Check(shell.GetGlobalRect().End.X<=shell.GetViewportRect().Size.X+1&&shell.GetGlobalRect().End.Y<=shell.GetViewportRect().Size.Y+1,"menu remains reachable at screen edges");
        ClimbController.MoveFloatingPanel(new Vector2(1000,30));ClimbController.SaveFloatingLayout();
        await Shot0620("11-menu-expanded");
        await CheckMenu0619(game);
    }
    private static async Task CheckEnchant0621(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        var panel=(PanelContainer)GameActions.Field(service.Panel,"panel")!;
        panel.Position=new Vector2(0,0);GameActions.Invoke(service.Panel,"SaveLayout");await Delay(.6);
        Check(panel.Position==Vector2.Zero,"in-run advisor can move above relics to screen origin");
        var saved=JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(Path.Combine(OS.GetUserDataDir(),"SpireStrategist","panel-layout.json")),Journal.Json)!;
        Check(saved.X==0&&saved.Y==0,"in-run free position persists");
        panel.Position=new Vector2(16,190);GameActions.Invoke(service.Panel,"SaveLayout");
        foreach(var n in new[]{3,2})
        {
            if(p.GetRelic<Kifuda>() is {} old)await RelicCmd.Remove(old);
            foreach(var c in p.Deck.Cards.ToArray())await CardPileCmd.RemoveFromDeck(c,false);
            foreach(var c in new CardModel[]{state.CreateCard<Anger>(p),state.CreateCard<PommelStrike>(p),state.CreateCard<ShrugItOff>(p)}.Take(n))
                await CardPileCmd.Add(c,PileType.Deck);
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);await Delay(.5);
            var room=NMerchantRoom.Instance!;room.OpenInventory();await Until(()=>Screen() is NMerchantInventory,"native Kifuda shop");
            var slots=room.Inventory.GetAllSlots().ToArray();
            foreach(var slot in slots)
            {
                if(slot.Entry is MerchantCardEntry card)AccessTools.Property(card.GetType(),"CreationResult").SetValue(card,null);
                if(slot.Entry is MerchantCardRemovalEntry removal)removal.SetUsed();
                if(slot.Entry is MerchantRelicEntry relic)AccessTools.Property(relic.GetType(),"Model").SetValue(relic,null);
                if(slot.Entry is MerchantPotionEntry)AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(slot.Entry,99999);
            }
            var entry=slots.Select(s=>s.Entry).OfType<MerchantRelicEntry>().First();
            GameActions.Invoke(entry,"SetModel",ModelDb.Relic<Kifuda>().ToMutable());
            AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(entry,120);
            foreach(var slot in slots){slot.Visible=slot.Entry.IsStocked;GameActions.Invoke(slot,"UpdateVisual");}
            p.Gold=300;ClimbController.SetEnabled(true);
            await Until(()=>Screen() is NDeckEnchantSelectScreen,"purchase opens actual enchant selector");
            service.Panel.SetReviewPaused(true);await Delay(1.2);
            Check(Screen() is NDeckEnchantSelectScreen&&p.Deck.Cards.All(c=>c.Enchantment==null),"review pause holds purchased enchant selector, n="+n);
            service.Panel.SetReviewPaused(false);
            await Until(()=>Screen() is NMapScreen,"Kifuda selection preview confirm and shop exit",40);
            Check(p.Deck.Cards.Count(c=>c.Enchantment is Adroit{Amount:3})==n,"native Kifuda enchants exactly "+n+" cards");
            Check(p.Gold==180&&p.Relics.Count(r=>r is Kifuda)==1,"Kifuda purchased exactly once, n="+n);
            Check(auto.Enabled&&ClimbController.Enabled&&!auto.WaitingForManual,"loop continues after enchant, n="+n);
            ClimbController.Stop("fixture boundary");NMapScreen.Instance!.Close(false);
        }
        // An optional unsupported enchant must finish with zero selections, not loop
        // forever in the native preview whose confirm handler ignores an empty set.
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        var clean=state.CreateCard<Anger>(p);await CardPileCmd.Add(clean,PileType.Deck);
        var prefs=new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt,0,3){Cancelable=false,RequireManualConfirmation=true};
        auto.Enable(true);GameActions.Invoke(auto,"PrepareRelicSelection",ModelDb.Relic<Kifuda>().ToMutable());
        var selector=NDeckEnchantSelectScreen.ShowScreen([clean],ModelDb.Enchantment<Momentum>(),1,prefs);
        var selection=selector.CardsSelected();
        await Until(()=>selection.IsCompleted,"optional unknown enchant closes with zero selections",15);
        Check(!(await selection).Any()&&clean.Enchantment==null,"zero selection neither enchants nor stalls");
        auto.Pause("fixture complete");
    }
}
