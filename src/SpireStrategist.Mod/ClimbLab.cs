using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    // Artificial lifecycle fixtures, never win-rate samples. All writes stay in the lab APPDATA.
    private static async Task CheckClimb064(RunState state,AdvisorService service)
    {
        Check(!ClimbController.Enabled,"batch off by default");
        await CreatureCmd.SetCurrentHp(service.Player.Creature,service.Player.Creature.MaxHp);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.TheArchitect>(),showTransition:false);
        ClimbController.SetEnabled(true);
        Check(ClimbController.Enabled&&service.Autopilot.FullAutomatic&&!ClimbController.Batch.Current!.FullRun,"join is assisted and arms autopilot");
        var firstSeed=state.Rng.StringSeed;
        await Until(()=>ClimbController.Batch.Results.Count==1,"architect dialogue completes native win",60);
        RunManager.Instance.OnEnded(true);
        Check(ClimbController.Batch.Results.Count==1&&ClimbController.Batch.Wins==0,"native duplicate settlement counted once; joined win excluded");
        await Until(()=>AdvisorService.Current is {} next&&next.State!=state&&ClimbController.Batch.Current!=null,"native next random run",60);
        var second=AdvisorService.Current!;
        Check(second.Player.Character.GetType()==service.Player.Character.GetType(),"next run retains selected character");
        Check(second.State.Rng.StringSeed!=firstSeed&&second.State.AscensionLevel==state.AscensionLevel,"next seed differs and ascension preserved");
        Check(ClimbController.Batch.Current!.FullRun&&second.Autopilot.FullAutomatic,"new native run automatically controlled");
        await Until(()=>second.State.TotalFloor>=1&&second.Player.Creature.CurrentHp>0,"new run enters Neow automatically",45);
        await Until(()=>AccessTools.Field(typeof(ClimbController),"transitioning").GetValue(null) is false,
            "new run launch task completes before synthetic settlement",90);
        second.Panel.SetReviewPaused(true);
        await Delay(1);
        Check(SnapshotReader.Read(second.State,second.Player).Map?.ExtraLayers==SnapshotReader.Read(state,service.Player).Map?.ExtraLayers,"new run preserves configured extra layers");
        // Enable on a new run can precede Neow's healing. Give the fixture HP before simulated loss.
        await CreatureCmd.SetCurrentHp(second.Player.Creature,second.Player.Creature.MaxHp);
        ClimbController.Batch.RequestStopAfterRun(true);
        RunManager.Instance.OnEnded(false);
        RunManager.Instance.OnEnded(false);
        Check(!ClimbController.Enabled&&ClimbController.Batch.Losses==1&&ClimbController.Batch.Results.Count==2,"stop after loss records exactly once");
        await Delay(10);
        Check(AdvisorService.Current?.State==second.State&&!second.Autopilot.Enabled,"pre-stop prevents another run");
        // Use a fresh native state to test immediate cancellation separately from game-over.
        await MegaCrit.Sts2.Core.Nodes.NGame.Instance!.ReturnToMainMenuAfterRun();
        await MegaCrit.Sts2.Core.Nodes.NGame.Instance.StartNewSingleplayerRun(
            ModelDb.GetById<CharacterModel>(service.Player.Character.Id),false,ActModel.GetDefaultList(),[],"SS-CLIMB-STOP",GameMode.Standard,state.AscensionLevel);
        var third=AdvisorService.Current!;
        await CreatureCmd.SetCurrentHp(third.Player.Creature,third.Player.Creature.MaxHp);
        ClimbController.SetEnabled(true);
        Check(ClimbController.Enabled,"fresh third fixture can enable: "+ClimbController.Status);
        ClimbController.Stop("lab immediate cancellation");
        var deck=third.Player.Deck.Cards.Count;var gold=third.Player.Gold;
        await Delay(3);
        Check(!ClimbController.Enabled&&!third.Autopilot.Enabled&&third.Player.Deck.Cards.Count==deck&&third.Player.Gold==gold,"immediate stop cancels pending control");
        Check(ClimbController.Batch.Results.Last().Outcome=="interrupted"&&ClimbController.Batch.Losses==1,"interruption is not a loss");
    }

    private static async Task CheckClimbShop064(RunState state,AdvisorService service)
    {
        var player=service.Player;
        await CreatureCmd.SetCurrentHp(player.Creature,player.Creature.MaxHp);
        async Task<NMerchantInventory> Shop()
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
            await Delay(.5);
            GameActions.Invoke(NMerchantRoom.Instance!,"OpenInventory");
            await Until(()=>Screen() is NMerchantInventory,"fixture merchant inventory");
            var inventory=(NMerchantInventory)Screen()!;
            foreach(var slot in inventory.GetAllSlots())
            {
                if(slot.Entry is MerchantCardEntry card)AccessTools.Property(card.GetType(),"CreationResult").SetValue(card,null);
                if(slot.Entry is MerchantCardRemovalEntry removal)removal.SetUsed();
            }
            return inventory;
        }
        var shop=await Shop();
        var relicEntries=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().ToArray();
        foreach(var r in relicEntries)AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
        GameActions.Invoke(relicEntries[0],"SetModel",ModelDb.Relic<Vajra>().ToMutable());
        player.Gold=relicEntries[0].Cost+100;
        var before=player.Relics.Count;
        ClimbController.SetEnabled(true);
        await Until(()=>Screen() is NMapScreen,"random relic budget purchase and leave",60);
        ClimbController.Stop("shop fixture boundary");
        Check(player.Gold==100&&player.Relics.Count==before+1&&player.Relics.Any(r=>r is Vajra),"relic purchased with exactly 100 reserved");
        NMapScreen.Instance!.Close(false);
        shop=await Shop();
        foreach(var r in shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>())
            AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
        var potions=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantPotionEntry>().ToArray();
        player.Gold=100+potions.Min(p=>p.Cost);var potionCount=player.Potions.Count();
        ClimbController.SetEnabled(true);
        await Until(()=>Screen() is NMapScreen,"fill potion after relics and leave",60);
        ClimbController.Stop("potion fixture complete");
        Check(player.Gold==100&&player.Potions.Count()==potionCount+1,"potion bought last and reserve respected");
        foreach(var id in new[]{"EMPTY_CAGE","ASTROLABE","DOLLYS_MIRROR"})
        {
            NMapScreen.Instance!.Close(false);shop=await Shop();
            var entries=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().ToArray();
            foreach(var r in entries)AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
            GameActions.Invoke(entries[0],"SetModel",ModelDb.GetById<RelicModel>(new ModelId("RELIC",id)).ToMutable());
            var deckCount=player.Deck.Cards.Count;
            player.Gold=entries[0].Cost+100;
            ClimbController.SetEnabled(true);
            await Until(()=>Screen() is NMapScreen,"shop relic selector "+id,60);
            ClimbController.Stop("selector fixture complete");
            Check(player.Relics.Any(r=>r.Id.Entry==id)&&player.Gold==100,"shop selector purchase and reserve "+id);
            Check(player.Deck.Cards.Count==deckCount+(id=="EMPTY_CAGE"?-2:id=="DOLLYS_MIRROR"?1:0),"shop selector deck effect "+id);
        }
    }
}
