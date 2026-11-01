using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Potions;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

// Native integration fixtures. Only the isolated test launcher supplies BOTH the environment
// switch and marker under its private APPDATA. Ordinary game sessions never enter this code.
[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
internal static partial class AutopilotLab
{
    private static bool started;
    // Only consumed inside the marker-gated isolated native lab; never a user setting.
    private static int trainingSeedIndex;
    internal static string? TrainingSeed()
    {
        if(!started||System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB") is not ("defect-training" or "silent-training"))return null;
        var seed=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LAB_SEED")??"SS-DEFECT";
        return trainingSeedIndex++==0?seed:seed+"-"+trainingSeedIndex;
    }
    private static string stage="starting";
    private static readonly List<string> checks=[];
    internal static void Postfix(NGame __instance)
    {
        if(started || System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LAB")!="1" ||
            System.Environment.GetEnvironmentVariable("COMBATSOLVER_PRECOMBAT_WORKER")!="1" ||
            !File.Exists(Path.Combine(OS.GetUserDataDir(),"strategist-lab.marker"))) return;
        started=true; _=Run(__instance);
    }
    private static async Task Delay(double seconds)=>await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static async Task Until(Func<bool> condition,string label,int seconds=30)
    {
        stage=label;GD.Print("[StrategistLab] "+label);
        var deadline=Time.GetTicksMsec()+(ulong)seconds*1000;
        while(!condition())
        {
            if(Time.GetTicksMsec()>deadline) throw new TimeoutException(label+"; auto="+AdvisorService.Current?.Autopilot.Status+"; screen="+Screen()?.GetType().Name);
            await Delay(.1);
        }
    }
    private static object? Screen()=>ActiveScreenContext.Instance?.GetCurrentScreen();
    private static void Check(bool condition,string name)
    {
        if(!condition)throw new InvalidOperationException(name);
        checks.Add(name);GD.Print("[StrategistLab] PASS "+name);
    }
    private static async Task Run(NGame game)
    {
        string? error=null;
        try
        {
            await Until(()=>game.RootSceneContainer?.CurrentScene is NMainMenu,"main menu",90);
            await Delay(1);
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="menu-0621")
            {await CheckMenu0621(game);return;}
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_CORPUS_ONLY")=="1")
            {ReviewCorpus.Export();checks.Add("review corpus exported");return;}
            foreach(var id in new[]{"rest_site_ftue","power_card_ftue","combat_reward_ftue","obtain_relic_ftue","combat_rules_ftue","shuffle_ftue","map_select_ftue","obtain_potion_ftue","merchant_ftue"}) SaveManager.Instance.MarkFtueAsComplete(id);
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="menu-065")
            {await CheckMenu065(game);return;}
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="menu-0619")
            {await CheckMenu0619(game);return;}
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB") is "menu-066" or "menu-cancel-066" or "menu-0613")
            {await CheckMenu066(game);return;}
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="deps-065")
            {await CheckDependencies065(game);return;}
            var seed=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LAB_SEED")??"SS-AUTO-1";
            var ascension=int.TryParse(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LAB_ASCENSION"),out var level)?level:10;
            if(ascension<0||ascension>10)throw new ArgumentOutOfRangeException(nameof(ascension));
            var gameMode=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="global-shop-0619"?GameMode.Custom:GameMode.Standard;
            var defectMode=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")??"";
            var isDefectLab=defectMode.StartsWith("defect-");
            if(defectMode=="defect-resume")
            {
                var menu=(NMainMenu)game.RootSceneContainer.CurrentScene;
                await Until(()=>GameActions.Field(menu,"_readRunSaveResult")!=null,"isolated saved run ready");
                await (Task)GameActions.Invoke(menu,"OnContinueButtonPressedAsync")!;
                await Until(()=>AdvisorService.Current!=null,"isolated Defect continuation");
                var resumed=AdvisorService.Current!;
                Check(resumed.Player.Character is Defect&&resumed.State.TotalFloor>0,"resume preserves an existing Defect run");
                ClimbController.SetEnabled(true);
                Check(ClimbController.Batch.Current is {FullRun:false},"resumed sample excluded from full automatic statistics");
                NModalContainer.Instance!.Clear();
                await CheckDefect0627(resumed.State,resumed,true);return;
            }
            if(defectMode is "defect-training" or "silent-training" or "regent-training" or "necrobinder-training")
            {
                ClimbController.SetEnabled(true);
                await Until(()=>AdvisorService.Current!=null&&!ClimbController.LaunchInProgress&&ClimbController.Batch.Current!=null,"Defect native menu launch",90);
                var training=AdvisorService.Current!;
                Check(training.State.ExtraFields.StartedWithNeow,"natural run has unlocked Neow");
                Check(training.State.AscensionLevel==ascension&&training.Player.Character.GetType().Name==(defectMode.Split('-')[0] switch{"silent"=>"Silent","regent"=>"Regent","necrobinder"=>"Necrobinder",_=>"Defect"}),"native menu uses saved Defect difficulty");
                Check(ClimbController.Batch.Current!.FullRun,"native menu start recorded as complete automatic run");
                // Fresh isolated profiles show a non-gameplay early-access disclaimer.
                NModalContainer.Instance!.Clear();
                await CheckDefect0627(training.State,training,true);return;
            }
            var state=await game.StartNewSingleplayerRun(defectMode.StartsWith("regent-")?ModelDb.Character<Regent>():defectMode.StartsWith("necrobinder-")?ModelDb.Character<Necrobinder>():defectMode.StartsWith("silent-")?ModelDb.Character<Silent>():isDefectLab?ModelDb.Character<Defect>():ModelDb.Character<Ironclad>(),false,ActModel.GetDefaultList(),[],seed,gameMode,ascension);
            var service=AdvisorService.Current??throw new Exception("No advisor");
            var auto=service.Autopilot;var player=service.Player;
            await Delay(1);NModalContainer.Instance!.Clear();
            File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"native-start.json"),JsonSerializer.Serialize(new{ascension,hp=player.Creature.CurrentHp,maxHp=player.Creature.MaxHp,source="native StartNewSingleplayerRun on map, BEFORE entering Neow and before fixture edits"},Journal.Json));
            Check(!auto.Enabled && !auto.FullAutomatic,"default disabled");
            if(defectMode is "regent-native" or "necrobinder-native"){await CheckHeroNative(state,service);return;}
            if(defectMode is "regent-combat" or "necrobinder-combat"){await CheckHeroCombat(state,service);return;}
            if(defectMode is "regent-loop" or "necrobinder-loop"){await CheckClimb064(state,service);return;}
            if(defectMode=="silent-native"){await CheckSilent(state,service);return;}
            if(defectMode=="silent-mechanics-0652"){await CheckSilent0652(state,service);return;}
            if(defectMode is "neow-common" or "defect-neow-common" or "silent-neow-common")
            {await CheckNeow0651(state,service);return;}
            if(defectMode is "silent-map-transition" or "defect-map-transition" or "ironclad-map-transition")
            {await CheckMapTransition(state,service);return;}
            if(defectMode=="silent-audit"){await CheckEvents(state,service,"audit");return;}
            if(defectMode=="silent-loop"){await CheckClimb064(state,service);return;}
            if(defectMode=="defect-transition-0627"){await CheckDefectTransitions0627(state,service);return;}
            if(defectMode=="defect-foresight-0642"){await CheckForesight0642(state,service);return;}
            if(defectMode=="defect-multiplayer-0645"){await CheckMultiplayer0645(state,service);return;}
            if(defectMode is "defect-advice-0649" or "advice-0649"){await CheckAdvice0649(state,service);return;}
            if(defectMode=="defect-advice-0646"){await CheckAdvice0646(state,service);return;}
            if(defectMode=="defect-camps-0647"){await CheckCamps0647(state,service);return;}
            if(defectMode=="defect-bridge-0648"){await CheckBridge0648(state,service);return;}
            if(defectMode=="defect-potions-0643"||defectMode=="potions-0643"){await CheckPotions0643(state,service);return;}
            if(isDefectLab){if(defectMode=="defect-loop-0627")await CheckClimb064(state,service);else await CheckDefect0627(state,service,false);return;}
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB") is {Length:>0} eventLab)
            {await CheckEvents(state,service,eventLab);return;}
            if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LONGMAP_LAB")=="1")
            {await CheckLongMap(state,service);return;}
            async Task ToMap(string label)
            {
                await Until(()=>Screen() is NMapScreen,label,45);
                Check(auto.Enabled && auto.FullAutomatic,"continuous mode retained: "+label);
                auto.Pause("fixture boundary");NMapScreen.Instance!.Close(false);await Delay(.5);
            }
            async Task Neutral()
            {
                auto.Pause("fixture setup");
                await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
                NMapScreen.Instance!.Close(false);await Delay(.5);
            }
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            await CreatureCmd.SetCurrentHp(player.Creature,10);
            await Until(()=>service.Decision?.Kind=="rest" && service.Decision.State.Hp==10,"low hp advice");
            var upgrades=player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
            auto.Enable(true);await Delay(.5);auto.Pause("lab cancellation");await Delay(3);
            Check(player.Creature.CurrentHp==10 && !auto.FullAutomatic,"stop cancels pending action and disarms standby");
            auto.Enable(true);await Until(()=>player.Creature.CurrentHp>10,"native heal");
            Check(player.Creature.CurrentHp==34 && player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)==upgrades,"heal exactly once");
            await ToMap("heal proceeds to map");
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            await CreatureCmd.SetCurrentHp(player.Creature,player.Creature.MaxHp);
            await Until(()=>service.Decision?.Kind=="rest" && service.Decision.State.Hp==player.Creature.MaxHp,"healthy advice");
            auto.Enable(true);await Until(()=>player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)==upgrades+1,"native smith with confirmation");
            await ToMap("smith proceeds to map");
            Check(player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)==upgrades+1,"smith exactly once");

            var preShopDeck=player.Deck.Cards.ToHashSet();
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
            auto.Enable(true);await Until(()=>auto.WaitingForManual,"shop enters standby");
            Check(auto.Enabled && auto.FullAutomatic,"shop completion retains armed state for manual relic shopping");
            var gold=player.Gold;var relicCount=player.Relics.Count;
            var shopGift=new RewardsSet(player).WithCustomRewards([new GoldReward(17,player),new RelicReward(ModelDb.Relic<Vajra>().ToMutable(),player)]).Offer();
            await Delay(4);
            Check(player.Gold==gold && player.Relics.Count==relicCount && auto.WaitingForManual,"shop rewards are never taken automatically");
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            var preTreasureRelics=player.Relics.ToArray();
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure,showTransition:false);
            await ToMap("standby resumes and collects treasure");
            Check(player.Relics.Count>relicCount,"treasure relic obtained through native picking");
            // End the random treasure fixture before checking exact gold deltas elsewhere.
            // LuckyFysh, for example, legitimately adds 15 gold when a shop card is acquired.
            foreach(var treasureRelic in player.Relics.Where(r=>!preTreasureRelics.Contains(r)).ToArray())
                await RelicCmd.Remove(treasureRelic);

            await Neutral();
            // The recovery fixture requires low attack capacity. A preceding random shop
            // can buy a useful attack under a new policy, so isolate that earlier fixture.
            foreach(var purchase in player.Deck.Cards.Where(c=>!preShopDeck.Contains(c)).ToArray())
                await CardPileCmd.RemoveFromDeck(purchase);
            var stolen=player.Deck.Cards.First(c=>c is StrikeIronclad);
            var beforeRecovery=player.Deck.Cards.Count;
            await CardPileCmd.RemoveFromDeck(stolen,false);
            state.AddCard(stolen,player);
            var recovery=new SpecialCardReward(stolen,player);
            recovery.SetCustomDescriptionEncounterSource(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.ThievingHopperWeak>().Id);
            var recoveryOffer=new RewardsSet(player).WithCustomRewards([recovery,new GoldReward(9,player)]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"stolen card recovery screen");auto.Enable(true);
            await Until(()=>recoveryOffer.IsCompleted,"stolen card recovery completes",45);await recoveryOffer;
            Check(player.Deck.Cards.Count==beforeRecovery && player.Deck.Cards.Count(c=>ReferenceEquals(c,stolen))==1 && auto.FullAutomatic,"stolen card restored once without manual standby");
            await Neutral();
            var supplements=new List<CardModel>();
            for(var i=0;i<5;i++)supplements.Add(state.CreateCard<Anger>(player));
            for(var i=0;i<3;i++)supplements.Add(state.CreateCard<ShrugItOff>(player));
            foreach(var c in supplements)await CardPileCmd.Add(c,PileType.Deck);
            await CardPileCmd.RemoveFromDeck(stolen,false);state.AddCard(stolen,player);
            var leaveRecovery=new SpecialCardReward(stolen,player);
            leaveRecovery.SetCustomDescriptionEncounterSource(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.ThievingHopperWeak>().Id);
            var goldBeforeLeave=player.Gold;
            var leaveOffer=new RewardsSet(player).WithCustomRewards([leaveRecovery,new GoldReward(13,player)]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"obsolete stolen card screen");auto.Enable(true);
            await Until(()=>leaveOffer.IsCompleted,"obsolete recovery skipped after other rewards",45);await leaveOffer;
            Check(!player.Deck.Cards.Contains(stolen) && player.Gold==goldBeforeLeave+13 && auto.FullAutomatic,"obsolete card stays removed, gold collected and full auto retained");
            await Neutral();
            var forcedRecovery=new SpecialCardReward(stolen,player);
            forcedRecovery.SetCustomDescriptionEncounterSource(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.ThievingHopperWeak>().Id);
            var forcedOffer=new RewardsSet(player).WithCustomRewards([forcedRecovery]).WithSkippingDisallowed().Offer();
            await Until(()=>Screen() is NRewardsScreen,"unskippable recovery screen");auto.Enable(true);
            await Until(()=>auto.WaitingForManual,"unskippable recovery manual");
            Check(!forcedOffer.IsCompleted && !player.Deck.Cards.Contains(stolen) && auto.Status.Contains("禁止跳过"),"disallowed skipping never forced");
            auto.Pause("fixture cleanup");RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            await Neutral();
            foreach(var c in supplements)await CardPileCmd.RemoveFromDeck(c,false);
            await CardPileCmd.Add(stolen,PileType.Deck);
            await Neutral();
            var quest=state.CreateCard<StrikeIronclad>(player);
            var specialOffer=new RewardsSet(player).WithCustomRewards([new SpecialCardReward(quest,player)]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"other special reward screen");auto.Enable(true);
            await Until(()=>auto.WaitingForManual,"other special reward abstains");
            Check(!player.Deck.Cards.Contains(quest) && !auto.Status.Contains("先古"),"unrelated special reward is not taken or mislabeled ancient");
            auto.Pause("fixture boundary");RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            await Neutral();
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
            player.Gold=500;
            await RelicCmd.Obtain(ModelDb.Relic<MembershipCard>().ToMutable(),player);
            await RelicCmd.Obtain(ModelDb.Relic<GhostSeed>().ToMutable(),player);
            var shopRoom=NMerchantRoom.Instance!;
            var inv=shopRoom.Inventory.Inventory!;
            // Controlled inventory, native purchase callbacks, prices, removal selector and hooks.
            foreach(var entry in inv.CardEntries)AccessTools.Property(entry.GetType(),"CreationResult").SetValue(entry,null);
            var cardEntry=inv.CardEntries.First();
            var shopCard=state.CreateCard<BattleTrance>(player);
            AccessTools.Property(cardEntry.GetType(),"CreationResult").SetValue(cardEntry,new CardCreationResult(shopCard));
            AccessTools.Field(cardEntry.GetType(),"_cost").SetValue(cardEntry,25);
            // This fixture isolates the ordinary purchase order; priority relics have their own native suite.
            foreach(var entry in inv.RelicEntries.Where(e=>e.Model?.Id.Entry is "MEMBERSHIP_CARD" or "MINIATURE_TENT" or "ICE_CREAM" or "CHEMICAL_X"))
                GameActions.Invoke(entry,"SetModel",ModelDb.Relic<Vajra>().ToMutable());
            foreach(var slot in shopRoom.Inventory.GetAllSlots())GameActions.Invoke(slot,"UpdateVisual");
            var shopCount=player.Deck.Cards.Count;
            var removalCount=player.ExtraFields.CardShopRemovalsUsed;
            var expectedRemovalCost=inv.CardRemovalEntry!.Cost;
            var expectedCardCost=cardEntry.Cost;
            var shopRelics=player.Relics.Count;
            auto.Enable(true);
            await Until(()=>player.ExtraFields.CardShopRemovalsUsed==removalCount+1,"shop native removal with confirmation",45);
            await Until(()=>player.Deck.Cards.Contains(shopCard),"shop buys useful affordable card",45);
            auto.Pause("isolate removal/card transaction assertions before later purchases");
            Check(expectedCardCost<25 && player.Gold==500-expectedRemovalCost-expectedCardCost && player.Deck.Cards.Count==shopCount,"shop charges actual discounted prices once and changes deck correctly");
            Check(player.Relics.Count==shopRelics && !auto.Enabled,"pause leaves later relic purchases to player");
            await Delay(3);
            Check(player.ExtraFields.CardShopRemovalsUsed==removalCount+1 && player.Gold==500-expectedRemovalCost-expectedCardCost,"shop does not repeat completed transactions");

            await Neutral();
            var count=player.Deck.Cards.Count;gold=player.Gold;
            var offered=state.CreateCard<ShrugItOff>(player);
            var relic=ModelDb.Relic<Anchor>().ToMutable();
            var potion=ModelDb.Potion<BlockPotion>().ToMutable();
            var offer=new RewardsSet(player).WithCustomRewards([new GoldReward(17,player),new RelicReward(relic,player),new PotionReward(potion,player),new CardReward(new CardModel[]{offered},CardCreationSource.Encounter,player,CardCreationOptions.ForRoom(player,RoomType.Monster))]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"mixed rewards");auto.Enable(true);
            await Until(()=>offer.IsCompleted,"native mixed reward collection",45);await offer;auto.Pause("fixture boundary");
            Check(player.Gold==gold+17 && player.Deck.Cards.Count==count+1 && player.Deck.Cards.Contains(offered),"gold and recommended card once");
            Check(player.Relics.Contains(relic),"ordinary relic taken");
            Check(player.PotionSlots.Contains(potion),"potion taken in empty slot");
            var popup=MegaCrit.Sts2.Core.Nodes.Ftue.NObtainPotionFtue.Create()!;
            NModalContainer.Instance.Add(popup);auto.Enable(true);
            await Until(()=>auto.WaitingForManual,"modal enters standby");
            Check(auto.FullAutomatic && auto.Enabled,"modal keeps automatic mode armed");
            await Delay(1);
            popup.GetType().GetMethod("CloseFtue",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(popup,[GameActions.Field(popup,"_confirmButton")]);
            await Until(()=>!auto.WaitingForManual,"resume after manual modal close");
            Check(auto.Enabled,"modal dismissal automatically resumes");auto.Pause("fixture boundary");
            while(player.PotionSlots.Any(p=>p==null))await PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(),player);
            var belt=player.PotionSlots.ToArray();
            var excess=ModelDb.Potion<FirePotion>().ToMutable();
            offer=new RewardsSet(player).WithCustomRewards([new PotionReward(excess,player)]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"full potion belt reward");auto.Enable(true);
            await Until(()=>offer.IsCompleted,"excess potion left behind");await offer;auto.Pause("fixture boundary");
            Check(player.PotionSlots.SequenceEqual(belt) && !player.PotionSlots.Contains(excess),"equal-tier attack reward does not displace defensive belt");

            var ancient=ModelDb.Relic<GoldenPearl>().ToMutable();gold=player.Gold;
            offer=new RewardsSet(player).WithCustomRewards([new RelicReward(ancient,player)]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"ancient reward");auto.Enable(true);
            await Until(()=>auto.WaitingForManual,"ancient reward waits");
            Check(!player.Relics.Contains(ancient) && player.Gold==gold && auto.FullAutomatic,"ancient relic not auto-selected");
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();await Neutral();

            count=player.Deck.Cards.Count;
            var unknown=state.CreateCard<StrikeSilent>(player);
            offer=new RewardsSet(player).WithCustomRewards([new CardReward(new CardModel[]{unknown},CardCreationSource.Encounter,player,CardCreationOptions.ForRoom(player,RoomType.Monster))]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"unknown reward list");auto.Enable(true);
            await Until(()=>auto.WaitingForManual,"unknown selection standby");
            Check(auto.Enabled && player.Deck.Cards.Count==count,"unknown selection waits without disarming");
            if(service.CardScreen!=null && GameActions.RewardReady(service.CardScreen))GameActions.SelectSkip(service.CardScreen,0);
            await Until(()=>offer.IsCompleted,"resume after manual skip");
            Check(auto.Enabled && auto.FullAutomatic,"manual selection resumes automatic proceed");auto.Pause("fixture boundary");
            await Neutral();
            await RelicCmd.Obtain(ModelDb.Relic<MawBank>().ToMutable(),player);
            Check(RouteAdapter.Economy(service).GoldPerRoom==12,"live active Maw Bank reflected in arrival estimate");
            player.Relics.OfType<MawBank>().Single().HasItemBeenBought=true;
            Check(RouteAdapter.Economy(service).GoldPerRoom==0,"spent Maw Bank excluded from arrival estimate");

            var encounter=ModelDb.GetById<EncounterModel>(new ModelId("ENCOUNTER","FUZZY_WURM_CRAWLER_WEAK")).ToMutable();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster,model:encounter,showTransition:false);
            await Delay(1);var solver=new SolverBridge();Check(solver.Bind()==null,"solver binds");
            auto.Enable(true);await Until(()=>solver.FullAuto,"combat handoff",60);
            solver.Stop(CombatManager.Instance.DebugOnlyGetState()!);
            await Until(()=>auto.WaitingForManual,"solver stop enters standby");await Delay(3);
            Check(!solver.FullAuto && auto.Enabled && auto.FullAutomatic,"solver stop is not reversed by standby");
            auto.Enable(true);await Until(()=>solver.FullAuto,"explicit resume",60);
            await Until(()=>!CombatManager.Instance.IsInProgress || auto.WaitingForManual,"combat completes",120);
            Check(!CombatManager.Instance.IsInProgress && player.Creature.CurrentHp>0,"native combat won");
            await ToMap("postcombat rewards proceed to map");
            Check(!solver.FullAuto,"solver released after victory");

            NMapScreen.Instance!.Open();
            await Until(()=>RouteAdapter.Legal(NMapScreen.Instance).Length>0,"legal map candidates");
            var legal=RouteAdapter.Legal(NMapScreen.Instance);
            var plan=RouteAdapter.Evaluate(service,legal);
            var selected=legal.Single(p=>RouteAdapter.Key(p.Point)==plan.RecommendedNode).Point.coord;
            auto.Enable(true);
            await Until(()=>state.CurrentMapCoord==selected,"native map travel",45);
            Check(state.CurrentMapCoord==selected && auto.Enabled,"native route selection reaches scored legal node");
            await Until(()=>!NMapScreen.Instance!.IsTraveling,"route transition completes",45);
            if(state.CurrentRoom is EventRoom)
            {
                Check(auto.FullAutomatic,"event retains automatic mode after route selection");
            }
        }
        catch(Exception ex){error=ex.ToString();GD.PrintErr("[StrategistLab] "+error);}
        finally
        {
            AdvisorService.Current?.Autopilot.Pause("lab complete");
            File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"strategist-autopilot-result.json"),JsonSerializer.Serialize(new{passed=error==null,stage,checks,error},new JsonSerializerOptions{WriteIndented=true}));
            game.GetTree().Quit(error==null?0:1);
        }
    }

    private static async Task CheckLongMap(RunState state,AdvisorService service)
    {
        var extra=int.Parse(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LONGMAP_LAYERS")!);
        var acts=int.Parse(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_LONGMAP_ACTS")!);
        var auto=service.Autopilot;
        var totalSkulls=0;
        Check(state.Acts.Count==3+acts,"LongMap actual total acts");
        foreach(var index in new[]{0,Math.Min(3,state.Acts.Count-1),state.Acts.Count-1}.Distinct())
        {
            if(state.CurrentActIndex!=index)await RunManager.Instance.EnterAct(index,false);
            await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
            await Delay(.5);
            var layout=LongMapAdapter.Read(state);
            Check(layout.LongMap && layout.Error==null && layout.ExtraLayers==extra && layout.ExtraActs==acts,"LongMap run settings read act "+(index+1));
            var original=extra>0?(int)AccessTools.Property(state.Map.GetType(),"OriginalRowCount").GetValue(state.Map)!:state.Map.GetRowCount();
            Check(layout.Rows==original+extra && layout.BossRow>=layout.Rows-1,"actual rows and shifted boss act "+(index+1));
            var rngBefore=JsonSerializer.Serialize(HiddenReader.Read(state,service.Player).Streams);
            var nodes=RouteAdapter.Nodes(state);var ids=nodes.Select(n=>n.Id).ToHashSet();
            Check(nodes.All(n=>n.Children.All(c=>ids.Contains(c)&&c!=n.Id)),"no missing or self-loop graph edges act "+(index+1));
            var points=state.Map.GetAllMapPoints().ToArray();
            Check(points.All(p=>p.Children.All(c=>nodes.Single(n=>n.Id==RouteAdapter.Key(p)).Children.Contains(RouteAdapter.Key(c)))),"all actual map edges preserved act "+(index+1));
            var skulls=points.Where(p=>LongMapAdapter.IsMiniBoss(state,p)).ToArray();
            totalSkulls+=skulls.Length;
            Check(skulls.All(p=>nodes.Single(n=>n.Id==RouteAdapter.Key(p)).Kind=="MiniBoss"),"skulls classified separately act "+(index+1));
            Check(skulls.All(p=>LongMapAdapter.IsMiniBoss(state,new MegaCrit.Sts2.Core.Map.MapPoint(p.coord.col,p.coord.row))),"persisted skull registry works without original point identity act "+(index+1));
            GD.Print($"[StrategistLab] act {index+1}: rows={layout.Rows}, skulls={skulls.Length}, totalActs={layout.TotalActs}");
            var economy=RouteAdapter.Economy(service);
            Check(economy.CombatRewardMultiplier==layout.CombatGoldScale && economy.CombatRiskMultiplier==layout.EnemyHpScale*layout.EnemyDamageScale,"live scaling reflected in forecast act "+(index+1));
            Check(rngBefore==JsonSerializer.Serialize(HiddenReader.Read(state,service.Player).Streams),"compatibility reads do not advance RNG act "+(index+1));
            Check(SnapshotReader.Read(state,service.Player).Hidden!.Acts.Length==state.Acts.Count,"hidden forecast retains all acts");
            var parent=points.Where(p=>p.Children.Count>0 && p.coord.row>=original-2 && p.coord.row<layout.Rows-1)
                .OrderByDescending(p=>p.Children.Any(c=>c.PointType.ToString()=="RestSite")).ThenByDescending(p=>p.coord.row).First();
            state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(parent.coord);
            GameActions.Invoke(RunManager.Instance,"AfterMapLocationChanged");
            var screen=NMapScreen.Instance!;screen.Open();GameActions.Invoke(screen,"RecalculateTravelability");screen.SetTravelEnabled(true);
            await Until(()=>RouteAdapter.Legal(screen).Length>0,"long map legal extra-row candidates");
            var legal=RouteAdapter.Legal(screen);var plan=RouteAdapter.Evaluate(service,legal);
            var target=legal.Single(p=>RouteAdapter.Key(p.Point)==plan.RecommendedNode).Point.coord;
            auto.Enable(true);await Until(()=>state.CurrentMapCoord==target,"long map native travel",45);auto.Pause("fixture boundary");
            await Until(()=>!screen.IsTraveling,"long map travel transition",45);
            Check(state.CurrentActIndex==index && state.CurrentMapCoord==target,"extra-row travel stays in actual act "+(index+1));
        }
        if(extra>=10)Check(totalSkulls>0,"generated skull coverage across tested acts");
        if(acts>0)
        {
            await RunManager.Instance.EnterAct(2,false);
            await RunManager.Instance.EnterNextAct();
            Check(state.CurrentActIndex==3 && LongMapAdapter.Read(state).EnemyHpScale==1.5,"third act transitions to scaled fourth act");
        }
    }
}
