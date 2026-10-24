using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Timeline.Epochs;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static int menuMusicStops;
    private static void CountMenuMusicStop()=>menuMusicStops++;
    private static async Task ClickClimb066(NGame game)
    {
        if(AdvisorService.Current is {} service){service.Panel.SelectTab(2);await Delay(.2);}
        var button=(CheckButton)AccessTools.Field(typeof(ClimbController),"enabledButton").GetValue(null)!;
        GD.Print("[StrategistLab] click loop: visible="+button.IsVisibleInTree()+" panel="+AdvisorService.Current?.Panel.Visible+" dock="+AdvisorService.Current?.Panel.LoopRect+" tab="+(AdvisorService.Current==null?-1:((TabContainer)GameActions.Field(AdvisorService.Current.Panel,"tabs")!).CurrentTab));
        var position=button.GetGlobalRect().GetCenter();
        game.GetViewport().PushInput(new InputEventMouseMotion{Position=position,GlobalPosition=position},true);
        game.GetViewport().PushInput(new InputEventMouseButton{Position=position,GlobalPosition=position,
            ButtonIndex=MouseButton.Left,Pressed=true,ButtonMask=MouseButtonMask.Left},true);
        await Delay(.1);
        game.GetViewport().PushInput(new InputEventMouseButton{Position=position,GlobalPosition=position,
            ButtonIndex=MouseButton.Left,Pressed=false},true);
        await Delay(.1);
    }
    private static async Task CheckMenu066(NGame game)
    {
        var musicTest=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="menu-0613";
        if(musicTest)new Harmony("SpireStrategist.Lab.MenuMusic").Patch(
            AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager),"StopMusic"),
            prefix:new HarmonyMethod(typeof(AutopilotLab),nameof(CountMenuMusicStop)));
        var musicStopsBefore=menuMusicStops;
        var stats=SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<Ironclad>().Id);
        stats.TotalWins=1;stats.MaxAscension=10;
        SaveManager.Instance.Progress.EnableFtues=false;
        SaveManager.Instance.Progress.ObtainEpochOverride(EpochModel.GetId<NeowEpoch>(),EpochState.Revealed);
        await Delay(.6);
        ((SpinBox)AccessTools.Field(typeof(ClimbController),"ascensionInput").GetValue(null)!).Value=10;
        var button=(CheckButton)AccessTools.Field(typeof(ClimbController),"enabledButton").GetValue(null)!;
        await ClickClimb066(game);
        if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_EVENT_LAB")=="menu-cancel-066")
        {
            Check(ClimbController.Enabled&&AdvisorService.Current==null,"cancel fixture starts during actual loading");
            await ClickClimb066(game);
            Check(!ClimbController.Enabled,"explicit mouse cancel works during loading");
            await ClickClimb066(game);
            Check(!ClimbController.Enabled,"repeated start cannot launch a second overlapping run");
            await Until(()=>AdvisorService.Current!=null&&
                AccessTools.Field(typeof(ClimbController),"transitioning").GetValue(null) is false,"cancelled launch finishes loading",45);
            await Delay(3);
            Check(!ClimbController.Enabled&&!AdvisorService.Current!.Autopilot.Enabled,
                "finishing an already cancelled launch never rearms automation");
            Check(ClimbController.Batch.Current==null&&ClimbController.Batch.Results.Count==0,
                "cancelled pre-attach launch creates no fabricated sample");
            return;
        }
        game.GetViewport().PushInput(new InputEventAction{Action="ui_accept",Pressed=true},true);
        game.GetViewport().PushInput(new InputEventAction{Action="ui_accept",Pressed=false},true);
        Check(ClimbController.Enabled,"game confirmation input does not toggle climb off after mouse click");
        Check(!button.HasFocus(),"climb checkbox does not retain gameplay keyboard focus");
        await Until(()=>AdvisorService.Current!=null,"mouse click starts run",45);
        var service=AdvisorService.Current!;
        await Until(()=>service.State.TotalFloor>=1&&service.State.CurrentRoom is EventRoom or CombatRoom,
            "first room reached without manual intervention",45);
        // Hold this fixture's room stable: fast mode otherwise advances through an
        // arbitrary randomly selected ancient's modal while we test mouse routing.
        // Loading cancellation is exercised separately by menu-cancel-066.
        service.Panel.SetReviewPaused(true);
        await Delay(3);
        Check(ClimbController.Enabled&&service.Autopilot.Enabled,"mouse-started climb remains enabled through opening");
        Check(ClimbController.Batch.Current?.FullRun==true,"mouse-started run remains full automatic sample");
        Check(service.State.AscensionLevel==10,"menu-selected ascension retained");
        if(musicTest)Check(menuMusicStops>musicStopsBefore,"menu-launched climb calls native StopMusic before entering run");
        button.ButtonPressed=false; // Passive UI changes cannot be interpreted as a stop command.
        await Delay(.6);
        Check(ClimbController.Enabled&&button.ButtonPressed,"state refresh does not cancel climb");
        GD.Print("[StrategistLab] stop diagnostics screen="+Screen()?.GetType().Name+" button="+button.GetGlobalRect()+" visible="+button.IsVisibleInTree()+" disabled="+button.Disabled+
            " layers="+string.Join(";",GameActions.Descendants<CanvasLayer>(game).Select(l=>l.GetPath()+":"+l.Layer)));
        await ClickClimb066(game);
        GD.Print("[StrategistLab] after mouse hover="+game.GetViewport().GuiGetHoveredControl()?.GetPath()+" enabled="+ClimbController.Enabled);
        Check(!ClimbController.Enabled&&!service.Autopilot.Enabled,"explicit mouse stop still immediately returns control");
        Check(ClimbController.Batch.Results.Count==1&&ClimbController.Batch.Results[0].FullRun,
            "cancelled original run recorded once with correct origin");
        await ClickClimb066(game);
        Check(ClimbController.Enabled&&ClimbController.Batch.Current?.FullRun==false,
            "deliberate rejoin of an existing run remains separately classified");
        await ClickClimb066(game);
    }
}
