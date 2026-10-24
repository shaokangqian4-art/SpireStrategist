using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireStrategist.Core;
using System.IO.Compression;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckMenu065(NGame game)
    {
        Check(AdvisorService.Current==null&&!SaveManager.Instance.HasRunSave,"isolated empty menu");
        var solver=new SolverBridge();Check(solver.Bind()==null,"verified solver integration");
        var initialAuto=solver.AutoStart;
        ClimbController.SetEnabled(true);
        await Until(()=>AdvisorService.Current!=null&&ClimbController.Batch.Current!=null,"start directly from menu",45);
        var service=AdvisorService.Current!;
        service.Panel.SetReviewPaused(true);
        Check(ClimbController.Batch.Current!.FullRun&&service.State.TotalFloor==0,"menu start counts complete autonomous run");
        Check(solver.AutoStart&&service.Autopilot.FullAutomatic,"global control arms solver auto enable setting");
        await Until(()=>AccessTools.Field(typeof(ClimbController),"transitioning").GetValue(null) is false,
            "initial menu launch completes before restart fixture");
        ClimbController.Stop("fixture fresh enable");
        Check(solver.AutoStart==initialAuto,"stopping restores solver setting");
        ClimbController.SetEnabled(true);service.Panel.SetReviewPaused(true);
        Check(ClimbController.Batch.Current!.FullRun,"untouched player-created opening is also full run");
        // Debug room entry skips normal autosaves. Create the isolated run and backup
        // before testing native Abandon, whose cleanup expects both files to exist.
        await Until(()=>service.State.CurrentRoom is MapRoom,"opening map ready");
        await SaveManager.Instance.SaveRun(null);
        await SaveManager.Instance.SaveRun(null);
        Check(SaveManager.Instance.HasRunSave,"menu-started run uses isolated native save");
        ClimbController.Batch.RequestStopAfterRun(true);
        // Native combat, fabricated solver results exercise exact stop branches, not search quality.
        var encounter=ModelDb.GetById<EncounterModel>(new ModelId("ENCOUNTER","FUZZY_WURM_CRAWLER_WEAK")).ToMutable();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster,model:encounter,showTransition:false);
        var combat=CombatManager.Instance.DebugOnlyGetState()!;
        solver.Stop(combat);await Delay(1);
        await Until(()=>solver.CanStart(combat),"combat turn ready for stop fixture");
        Check(!solver.TryDeathStop(combat,out _),"manual stop never creates defeat evidence");
        var controller=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("CombatSolver.SolverController")).First(t=>t!=null)!;
        solver.Stop(combat);
        await Until(()=>AccessTools.Property(controller,"IsSearching").GetValue(null) is false&&
            AccessTools.Property(controller,"IsDeploying").GetValue(null) is false,"solver is idle before synthetic stop results");
        var resultType=controller.Assembly.GetType("CombatSolver.SolverResult")!;
        var result=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(resultType);
        void Set(string name,object value)=>AccessTools.Property(resultType,name).SetValue(result,value);
        var expectedTurn=service.Player.PlayerCombatState!.TurnNumber;
        Set("StartTurnNumber",expectedTurn);Set("DeathTurn",expectedTurn);
        AccessTools.Method(controller,"SetStopFullAutoOnWorseRecalculation").Invoke(null,[true,false]);
        Set("PreviousProjectedBattleHpLost",0);Set("ProjectedBattleHpLost",1);
        AccessTools.Method(controller,"StartFullAutoDeployment").Invoke(null,[game,combat,result]);
        Check(!solver.TryDeathStop(combat,out _),"worse recalculation stop is not a death-route concession");
        Set("ProjectedBattleHpLost",0);
        AccessTools.Method(controller,"StartFullAutoDeployment").Invoke(null,[game,combat,result]);
        var stamp=AccessTools.Field(typeof(SolverBridge),"deathStamp").GetValue(null);
        var stampNow=AccessTools.Method(controller.Assembly.GetType("CombatSolver.LiveCombatStamp"),"Capture").Invoke(null,[combat]);
        GD.Print("[StrategistLab] stop evidence "+System.Text.Json.JsonSerializer.Serialize(new{
            expectedTurn,liveTurn=combat.Players.Single().PlayerCombatState?.TurnNumber,
            capturedTurn=AccessTools.Field(typeof(SolverBridge),"deathTurn").GetValue(null),
            sameCombat=ReferenceEquals(combat,AccessTools.Field(typeof(SolverBridge),"deathCombat").GetValue(null)),
            solver.FullAuto,searching=AccessTools.Property(controller,"IsSearching").GetValue(null),
            deploying=AccessTools.Property(controller,"IsDeploying").GetValue(null),hasStamp=stamp!=null,stampMatches=Equals(stamp,stampNow)}));
        Check(solver.TryDeathStop(combat,out var turn)&&turn==expectedTurn,"exact native death-turn branch produces evidence");
        var hp=service.Player.Creature.CurrentHp;
        await CreatureCmd.SetCurrentHp(service.Player.Creature,hp-1);
        Check(!solver.TryDeathStop(combat,out _),"changed combat state invalidates old death-route evidence");
        await CreatureCmd.SetCurrentHp(service.Player.Creature,hp);
        AccessTools.Method(controller,"StartFullAutoDeployment").Invoke(null,[game,combat,result]);
        service.Panel.SetReviewPaused(false);
        await Until(()=>ClimbController.Batch.Results.Any(r=>r.Reason.Contains("solver_death_turn")),"native abandonment settles concession",60);
        var resultRow=ClimbController.Batch.Results.Last();
        Check(resultRow.Outcome=="loss"&&resultRow.FullRun&&ClimbController.Batch.Losses==1,"autonomous concession is a counted loss");
        Check(RunManager.Instance.IsAbandoned&&!ClimbController.Enabled,"native history abandoned and pre-stop respected");
        ClimbController.Ended(service.State,false);
        Check(ClimbController.Batch.Losses==1,"duplicate end cannot double count");
        await Delay(9);
        Check(AdvisorService.Current?.State==service.State,"no next run after pre-stop");
        var journal=new Journal(Path.Combine(OS.GetUserDataDir(),"feedback-fixture"));
        journal.Write("feedback",new{vote="disagree",reason="中文反馈：希望升级核心牌"});
        var path=journal.ExportFeedback(new{seed="fixture",version=Policy.Version});
        using var zip=ZipFile.OpenRead(path);
        Check(zip.Entries.Count==3&&zip.GetEntry(UiLocalization.Text("反馈说明.txt"))!=null,"self-contained feedback ZIP");
        using var reader=new StreamReader(zip.GetEntry(UiLocalization.Text("本局记录.jsonl"))!.Open());
        Check(reader.ReadToEnd().Contains("中文反馈"),"feedback text preserved in export");
    }

    private static async Task CheckDependencies065(NGame game)
    {
        Check(SolverBridge.CompatibilityError()?.Contains("未安装")==true,"missing solver clearly identified");
        ClimbController.SetEnabled(true);
        Check(!ClimbController.Enabled&&AdvisorService.Current==null,"no solver cannot auto-launch");
        var controls=(Control)AccessTools.Field(typeof(ClimbController),"controls").GetValue(null)!;
        Check(controls.IsVisibleInTree()&&((CheckButton)AccessTools.Field(typeof(ClimbController),"enabledButton").GetValue(null)!).Disabled,"missing solver keeps visible locked loop controls");
        Check(((Label)AccessTools.Field(typeof(ClimbController),"solverNotice").GetValue(null)!).Text.Contains(UiLocalization.Text(AdvisorUi.SolverLocked)),"installation instruction names CombatSolver");
        await Shot0620("12-no-solver");
        var state=await game.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(),false,ActModel.GetDefaultList(),[],"SS-NO-DEPS",GameMode.Standard,0);
        var service=AdvisorService.Current!;
        Check(SnapshotReader.Read(state,service.Player).Map?.LongMap!=true,"standard map works without extra layers mod");
        service.Autopilot.Enable(true);
        Check(service.Autopilot.FullAutomatic,"noncombat automation remains available");
        service.Autopilot.Pause("fixture");
    }

    private static async Task CheckTooth065(RunState state,AdvisorService service)
    {
        await CreatureCmd.SetCurrentHp(service.Player.Creature,service.Player.Creature.MaxHp);
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        foreach(var id in new[]{"BATTLE_TRANCE","INFLAME","SHRUG_IT_OFF","TWIN_STRIKE","POMMEL_STRIKE","OFFERING","SHRUG_IT_OFF","TWIN_STRIKE"})
            await CardPileCmd.Add(state.CreateCard(ModelDb.GetById<CardModel>(new ModelId("CARD",id)),service.Player),PileType.Deck);
        var relic=(PaelsTooth)ModelDb.Relic<PaelsTooth>().ToMutable();
        service.Autopilot.Enable(true);
        GameActions.Invoke(service.Autopilot,"PrepareRelicSelection",relic);
        var before=service.Player.Deck.Cards.ToArray();
        var obtain=RelicCmd.Obtain(relic,service.Player);
        await Until(()=>Screen() is NDeckCardSelectScreen,"native tooth selector");
        await Until(()=>obtain.IsCompleted,"tooth selects five and completes native pickup",60);await obtain;
        service.Autopilot.Pause("fixture");
        var removed=before.Except(service.Player.Deck.Cards).ToArray();
        Check(removed.Length==5&&relic.SerializableCards.Count==5,"native tooth temporarily stores five");
        Check(removed.Count(c=>c.Rarity!=MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Basic)>=3,"tooth chooses substantial upgrades rather than only basics");
        // Native after-combat hook, with no battle: mechanics fixture, never a win sample.
        await relic.AfterCombatEnd(null!);
        Check(relic.SerializableCards.Count==4&&service.Player.Deck.Cards.Count==before.Length-4,"one native random upgraded card returns");
    }
}
