using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckDefectTransitions0627(RunState state,AdvisorService service)
    {
        // Synthetic transition regression, never a natural result or win-rate sample.
        ClimbController.SetEnabled(true);
        for(var act=1;act<=2;act++)
        {
            service.Panel.SetReviewPaused(true);
            await RunManager.Instance.EnterAct(act,false);
            service.Panel.SetReviewPaused(false);
            await Until(()=>state.CurrentRoom is EventRoom&&Screen() is MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom,
                "fast Defect transition reaches visible ancient in act "+(act+1),60);
            service.Panel.SetReviewPaused(true);
            Check(Screen() is not NMapScreen,"map does not cover entering ancient");
        }
        ClimbController.Stop("synthetic transition fixture finished");
        Check(ClimbController.Batch.Wins+ClimbController.Batch.Losses==0,"synthetic transitions are not wins or losses");
    }
    private static async Task CheckDefect0627(RunState state,AdvisorService service,bool fullRun)
    {
        if(fullRun)
        {
            // Natural native run: no deck, HP, gold, encounter or RNG edits.
            Check(ClimbController.Enabled&&service.Autopilot.Enabled,"continuous control armed");
            var runs=int.TryParse(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_TRAINING_RUNS"),out var count)?Math.Clamp(count,1,10):1;
            var limitSeconds=int.TryParse(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_TRAINING_LIMIT_SECONDS"),out var limit)?Math.Clamp(limit,120,43200):runs*3600;
            ClimbController.Batch.RequestStopAfterRun(runs==1);
            var start=Time.GetTicksMsec();var last= start;var floor=-1;var turn=-1;string? screen=null;
            while(ClimbController.Batch.Results.Count<runs)
            {
                if(AdvisorService.Current is {} current){service=current;state=current.State;}
                if(ClimbController.Batch.Results.Count==runs-1&&ClimbController.Batch.Current!=null)
                    ClimbController.Batch.RequestStopAfterRun(true);
                var battle=MegaCrit.Sts2.Core.Combat.CombatManager.Instance.DebugOnlyGetState();
                var currentTurn=battle?.Players.FirstOrDefault()?.PlayerCombatState?.TurnNumber??-1;
                var currentScreen=Screen()?.GetType().Name;
                if(state.TotalFloor!=floor||turn!=currentTurn||screen!=currentScreen)
                {
                    floor=state.TotalFloor;turn=currentTurn;screen=currentScreen;last=Time.GetTicksMsec();
                    GD.Print($"[StrategistTraining] floor={floor} turn={turn} screen={screen} hp={service.Player.Creature.CurrentHp} {service.Autopilot.Status}");
                }
                if(!ClimbController.Enabled&&!state.IsGameOver)throw new Exception("Autopilot stopped: "+service.Autopilot.Status);
                if(Time.GetTicksMsec()-last>180000)throw new TimeoutException("No progress for 180 seconds: "+service.Autopilot.Status);
                if(Time.GetTicksMsec()-start>(ulong)limitSeconds*1000)throw new TimeoutException("Training reached the configured wall-time limit; not a strategic loss");
                await Delay(.5);
            }
            File.WriteAllText(Path.Combine(OS.GetUserDataDir(),service.Player.Character.GetType().Name.ToLowerInvariant()+"-training-result.json"),JsonSerializer.Serialize(new{
                seed=state.Rng.StringSeed,character=service.Player.Character.GetType().Name,policy=HeroPolicy.VersionFor(service.Player.Character.GetType().Name),results=ClimbController.Batch.Results,
                state=SnapshotReader.Read(state,service.Player),elapsedSeconds=(Time.GetTicksMsec()-start)/1000},Journal.Json));
            Check(!ClimbController.Enabled,"stop-after-run retains result and prevents next launch");
            Check(ClimbController.Batch.Results.All(r=>r.Outcome is "win" or "loss"),"natural training finished with game results, not flow interruptions");
            return;
        }
        var player=service.Player;
        foreach(var original in new CardModel[]{state.CreateCard<AdaptiveStrike>(player),state.CreateCard<Anger>(player)})
        {
            CardCmd.Enchant<Swift>(original,2);
            // Native CardModel.Play executes card OnPlay (clone) BEFORE enchantment OnPlay.
            var clone=original.CreateClone();clone.EnergyCost.SetThisCombat(0);
            original.Enchantment!.Status=EnchantmentStatus.Disabled;
            Check(clone.Enchantment is Swift&&clone.Enchantment.Amount==2,"native clone inherits Swift amount: "+original.Id);
            Check(clone.Enchantment!.Status==EnchantmentStatus.Normal&&!ReferenceEquals(clone.Enchantment,original.Enchantment),"first-play clone inherits unused Swift before parent trigger: "+original.Id);
            Check(original.Enchantment.Status==EnchantmentStatus.Disabled,"clone does not reactivate original enchantment");
            var usedClone=original.CreateClone();
            Check(usedClone.Enchantment!.Status==EnchantmentStatus.Disabled,"cloning an already used instance does not reset Swift");
            var snap=SnapshotReader.ReadCard(clone);
            Check(snap.Cost==0&&snap.Enchantment=="ENCHANTMENT.SWIFT", "native zero copy snapshot records cost and enchantment");
        }
        await CreatureCmd.SetCurrentHp(player.Creature,player.Creature.MaxHp);
        await CardPileCmd.Add(state.CreateCard<Hologram>(player),PileType.Deck);
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>service.Decision?.Kind=="rest","Defect camp evaluated");
        Check(service.Decision!.Options.First(o=>o.Key==service.Decision.Evaluation.RecommendedKey).Kind=="smith","Defect healthy camp prioritizes key upgrade");
        service.Autopilot.Enable(true);
        await Until(()=>player.Deck.Cards.OfType<Hologram>().Any(c=>c.CurrentUpgradeLevel>0),"native Defect Hologram upgrade");
        await Until(()=>Screen() is NMapScreen,"Defect camp returns to map");
        service.Autopilot.Pause("fixture boundary");
        Check(!service.Autopilot.Enabled,"Defect can hand control back");
        await Shot0620("defect-0627-camp");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        var support=state.CreateCard<MasterOfStrategy>(player);
        var unknown=state.CreateCard<TheBomb>(player);
        var supportedReward=new CardReward(new CardModel[]{unknown,support},
            CardCreationSource.Encounter,player,CardCreationOptions.ForRoom(player,RoomType.Monster)){CanReroll=true};
        var offer=new RewardsSet(player).WithCustomRewards([supportedReward]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"mixed known/unknown Defect reward");
        service.Autopilot.Enable(true);
        await Until(()=>offer.IsCompleted,"known positive free card survives unknown alternative",45);
        await offer;service.Autopilot.Pause("fixture boundary");
        Check(player.Deck.Cards.Contains(support)&&!player.Deck.Cards.Contains(unknown),"partial native reward acquires known support, not unknown card");
        Check(supportedReward.CanReroll,"positive known support is selected without spending reroll");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        var poor=new CardReward(new CardModel[]{state.CreateCard<StrikeDefect>(player),state.CreateCard<DefendDefect>(player)},
            CardCreationSource.Encounter,player,CardCreationOptions.ForRoom(player,RoomType.Monster)){CanReroll=true};
        var rerollOffer=new RewardsSet(player).WithCustomRewards([poor]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"reroll-only improvement fixture opens");
        service.Autopilot.Enable(true);
        await Until(()=>rerollOffer.IsCompleted,"native reroll followed by a new reward decision",60);
        await rerollOffer;service.Autopilot.Pause("fixture boundary");
        Check(!poor.CanReroll,"native free reroll consumed once, with refreshed decision");
        await CheckShopReward0629(state,service);
    }
}
