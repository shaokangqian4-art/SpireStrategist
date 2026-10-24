using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckFeedback063(RunState state,AdvisorService service)
    {
        var player=service.Player;var auto=service.Autopilot;
        // Export localized names to disambiguate user shorthand from actual game model IDs.
        File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"card-names-063.json"),JsonSerializer.Serialize(
            ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool>().AllCards
                .Concat(ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>().AllCards)
                .Select(c=>new{type=c.GetType().Name,name=c.Title}),Journal.Json));
        foreach(var p in player.Potions.ToArray())await PotionCmd.Discard(p);
        while(player.PotionSlots.Any(p=>p==null))await PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(),player);
        var slots=player.PotionSlots.Count;
        // Since 0.6.11 replacement needs a real reachable merchant, not an
        // unconditional preference. Keep this legacy positive-case fixture explicit.
        var merchant=state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Shop&&p.parents.Count>0);
        state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(merchant.parents.First().coord);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<PotionCourier>(),showTransition:false);
        auto.Enable(true);
        await Until(()=>player.Potions.OfType<FoulPotion>().Count()==Math.Min(3,slots),"courier replaces full slots with Foul Potions",60);
        await Until(()=>Screen() is NMapScreen,"courier completes reward flow",60);
        Check(player.Potions.OfType<FoulPotion>().Count()==Math.Min(3,slots),"native courier pickup replaces rather than silently skipping full slots");
        auto.Pause("fixture boundary");NMapScreen.Instance!.Close(false);

        var curse=state.CreateCard<Normality>(player);await CardPileCmd.Add(curse,PileType.Deck);
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
        player.Gold=500;
        var shop=NMerchantRoom.Instance!;
        var inv=shop.Inventory.Inventory!;
        foreach(var entry in inv.CardEntries)AccessTools.Property(entry.GetType(),"CreationResult").SetValue(entry,null);
        foreach(var slot in shop.Inventory.GetAllSlots())GameActions.Invoke(slot,"UpdateVisual");
        shop.OpenInventory();await Delay(2);
        var layer=(Node)GameActions.Field(service.Panel,"layer")!;
        var disagree=GameActions.Descendants<Button>(layer).Single(b=>b.Text=="有异议");
        Check(!disagree.Disabled,"shop advice and feedback available before enabling automation");
        service.Panel.SetReviewPaused(true);auto.Enable(true);
        await Delay(9);
        Check(player.Gold==500&&player.Deck.Cards.Contains(curse),"review pause prevents shop transactions while retaining takeover");
        disagree.EmitSignal(Button.SignalName.Pressed);
        var reason=GameActions.Descendants<TextEdit>(layer).Single();reason.Text="原生测试：先删常态，附带当时完整状态";
        service.Panel.SetReviewPaused(false);await Delay(3);
        Check(service.Panel.ReviewPaused&&player.Gold==500,"feedback editor independently suspends pending action");
        GameActions.Descendants<Button>(layer).Single(b=>b.Text=="保存异议").EmitSignal(Button.SignalName.Pressed);
        var price=inv.CardRemovalEntry!.Cost;
        await Until(()=>!player.Deck.Cards.Contains(curse),"native shop deletes curse",60);
        auto.Pause("feedback price assertion before subsequent shop purchases");
        Check(player.Gold==500-price,"curse removal charges native discounted price once");
        var records=Directory.GetFiles(Path.Combine(OS.GetUserDataDir(),"SpireStrategist"),"session-*.jsonl")
            .SelectMany(File.ReadLines).Select(line=>JsonDocument.Parse(line)).ToArray();
        try
        {
            var feedback=records.Select(r=>r.RootElement).Last(r=>r.GetProperty("type").GetString()=="feedback").GetProperty("payload");
            Check(feedback.GetProperty("reason").GetString()!.Contains("原生测试")&&feedback.GetProperty("context").GetProperty("kind").GetString()=="shop"
                &&feedback.GetProperty("context").GetProperty("state").GetProperty("gold").GetInt32()==500,"written feedback preserves reason and pre-purchase state");
        }finally{foreach(var r in records)r.Dispose();}
        var solver=new SolverBridge();Check(solver.Bind()==null,"solver adoption public API binds on supported version");
        Check(!solver.TryAdoptAfterBudget(1,80,999999,out _),"no search candidate never triggers forced adoption");
    }
}
