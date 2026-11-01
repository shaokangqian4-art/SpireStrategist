using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckUi0640(RunState state,AdvisorService service)
    {
        SaveManager.Instance.Progress.EnableFtues=false;NModalContainer.Instance!.Clear();
        var panel=service.Panel;
        var tabs=(TabContainer)GameActions.Field(panel,"tabs")!;
        var header=(Label)GameActions.Field(panel,"compactStatus")!;
        var snapshot=SnapshotReader.Read(state,service.Player) with{Character="Defect"};
        var language=UiLocalization.Preference;
        UiLocalization.Set("zh");
        panel.Automation(true,true,"正在评估路线");panel.UpdateState(snapshot);
        Check(header.Text=="单局接管中","snapshot does not replace takeover with experimental advice label");
        foreach(var message in new[]{"等待选牌","正在购买","请手动处理未知事件","准备下一步"})
        {
            panel.UpdateState(snapshot);panel.Automation(true,true,message);panel.UpdateState(snapshot);
            Check(header.Text=="单局接管中","transient action message keeps mode stable: "+message);
        }
        panel.SetReviewPaused(true);panel.UpdateState(snapshot);
        Check(header.Text=="已暂停 · 查看建议","explicit pause still changes the stable mode");
        panel.SetReviewPaused(false);panel.Automation(false,false,"已停止");panel.UpdateState(snapshot);
        Check(header.Text=="故障机器人 · 建议与接管","stopping returns to Defect advice mode");
        Check(tabs.GetTabCount()==6,"settings is a separate sixth page");
        var picker=GameActions.Descendants<OptionButton>(tabs.GetChild(5)).Single(n=>n.Name=="AssistantLanguage");
        Check(!GameActions.Descendants<OptionButton>(tabs.GetChild(2)).Any(n=>n.Name=="AssistantLanguage"),"language removed from loop host");
        panel.SelectTab(5);await Delay(.7);Check(picker.IsVisibleInTree(),"settings picker is visible");
        await Shot0620("0640-settings-zh");
        UiLocalization.Set("en");panel.UpdateState(snapshot);await Delay(.4);
        Check(tabs.GetTabTitle(5)=="Settings","settings tab translates");
        UiLocalization.Set("zh");panel.UpdateState(snapshot);
        panel.SelectTab(2);await Delay(.7);
        var floatingTabs=(TabContainer)AccessTools.Field(typeof(ClimbController),"floatingTabs").GetValue(null)!;
        Check(!floatingTabs.TabsVisible&&floatingTabs.CurrentTab==0,"embedded loop hides redundant floating tabs");
        Check(!GameActions.Descendants<OptionButton>(floatingTabs).Single(n=>n.Name=="AssistantLanguage").IsVisibleInTree(),"embedded loop has no visible language control");
        await Shot0620("0640-loop-zh");
        // The same dock floats on the main menu or when the in-run panel is hidden.
        var root=(PanelContainer)GameActions.Field(panel,"panel")!;
        root.Visible=false;ClimbController.RefreshUi();await Delay(.6);
        Check(floatingTabs.TabsVisible,"floating dock exposes its own settings tab");
        floatingTabs.CurrentTab=1;await Delay(.6);
        Check(GameActions.Descendants<OptionButton>(floatingTabs).Single(n=>n.Name=="AssistantLanguage").IsVisibleInTree(),"floating settings language control remains accessible");
        await Shot0620("0640-floating-settings-zh");
        root.Visible=true;ClimbController.RefreshUi();
        CheckLanguage0622(panel);
        UiLocalization.Set(language);

        // Exercise the actual game history types and snapshot adapter, not a hand-built
        // CombatSummary list that would bypass the original filtering bug.
        MapPointHistoryEntry H(RoomType room,int loss,int hp=40)=>new()
        {
            Rooms=[new(){RoomType=room}],
            PlayerStats=[new(){PlayerId=service.Player.NetId,MaxHp=80,CurrentHp=hp,DamageTaken=loss}]
        };
        var current=H(RoomType.Monster,99);
        var history=new[]{H(RoomType.Elite,54),H(RoomType.Monster,1),H(RoomType.Monster,2),
            H(RoomType.Monster,21),H(RoomType.Monster,18),H(RoomType.Boss,30),H(RoomType.Monster,70,0),current};
        var read=SnapshotReader.ReadRecentBattles(history,current,service.Player.NetId,2);
        Check(read.Where(b=>b.Room=="Monster").Select(b=>b.DamageTaken).SequenceEqual(new[]{2,21,18}),"completed normal damage retained, latest three, excludes current and death");
        Check(read.Any(b=>b.Room=="Elite"&&b.DamageTaken==54)&&read.Any(b=>b.Room=="Boss"),"normal fights do not evict elite or boss history");
        Check(read.All(b=>b.Act==2),"history retains actual content act");
        var completed=SnapshotReader.ReadRecentBattles(history,current,service.Player.NetId,2,new(2,"Monster",null,23,0,0,80));
        Check(completed.Where(b=>b.Room=="Monster").Select(b=>b.DamageTaken).SequenceEqual(new[]{21,18,23}),"post-combat map can include latest damage exactly once before native HP fields are sealed");
    }
}
