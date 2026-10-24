using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task ClickUi0620(Control control)
    {
        var pos=control.GetGlobalTransformWithCanvas()*(control.Size/2);
        var viewport=NGame.Instance!.GetViewport();
        viewport.PushInput(new InputEventMouseMotion{Position=pos,GlobalPosition=pos},true);
        await Delay(.15); // Native targeting samples the hovered creature during _Process.
        viewport.PushInput(new InputEventMouseButton{Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=true,ButtonMask=MouseButtonMask.Left},true);
        await Delay(.1);
        viewport.PushInput(new InputEventMouseButton{Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=false},true);
        await Delay(.2);
    }
    private static async Task Shot0620(string name)
    {
        if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")!="1")return;
        await Delay(.5);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var dir=Path.Combine(OS.GetUserDataDir(),"ui-review");Directory.CreateDirectory(dir);
        var panels=new List<PanelContainer>();
        if(AdvisorService.Current is {} current)panels.Add((PanelContainer)GameActions.Field(current.Panel,"panel")!);
        panels.Add((PanelContainer)AccessTools.Field(typeof(ClimbController),"shell").GetValue(null)!);
        var texts=panels.SelectMany(p=>GameActions.Descendants<Control>(p)).Select(c=>c switch{Label l=>l.Text,Button b=>b.Text,RichTextLabel t=>t.Text,_=>""}).Distinct();
        File.WriteAllLines(Path.Combine(dir,name+".txt"),texts);
        if(UiLocalization.English)
        {
            var leaks=texts.Where(t=>System.Text.RegularExpressions.Regex.IsMatch(t.Replace("Language / 语言",""),"[\\u3400-\\u9fff]")).ToArray();
            Check(leaks.Length==0,"English assistant text complete at "+name+": "+string.Join(" | ",leaks));
        }
        NGame.Instance.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir,name+".png"));
    }
    private static async Task CheckUi0620(RunState state,AdvisorService service)
    {
        CheckLanguage0622(service.Panel);
        var rendered=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1";
        SaveManager.Instance.Progress.EnableFtues=false;NModalContainer.Instance!.Clear();
        var panel=(PanelContainer)GameActions.Field(service.Panel,"panel")!;
        var tabs=(TabContainer)GameActions.Field(service.Panel,"tabs")!;
        Check(tabs.GetTabCount()==5,"five categorized pages");
        var paintLayer=GameActions.Descendants<CanvasLayer>(NGame.Instance!).Single(n=>n.Name=="StrategistRecommendationPaint");
        Check(!GameActions.Descendants<Control>(paintLayer).Any(),"paint layer contains no mouse-intercepting controls");
        service.Panel.SelectTab(2);await Delay(.6);
        var loop=(PanelContainer)AccessTools.Field(typeof(ClimbController),"shell").GetValue(null)!;
        Check(loop.IsVisibleInTree()&&loop.Position.Y>=190&&panel.Position.Y>=190,"loop dock and advisor reserve potion HUD");
        await Shot0620("01-loop");
        service.Panel.SelectTab(0);await Delay(.6);
        Check(!loop.IsVisibleInTree(),"leaving loop tab removes its entire input area");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Open();await Delay(1.5);
        var rng=JsonSerializer.Serialize(state.Rng.ToSerializable());
        var route=RouteAdapter.Preview(service,NMapScreen.Instance!);service.ShowRoute(route);
        Check(route.RecommendedNode!=null&&route.Options.Any(o=>o.Path.Length>1),"advice-only map builds multi-room route");
        await Shot0620("02-route");
        if(rendered)Check(service.Visuals.RouteSegments>0,"native rendering paints route segments");
        await CreatureCmd.SetCurrentHp(service.Player.Creature,7);await Delay(1.5);
        var shown=(SpireStrategist.Core.RouteEvaluation)GameActions.Field(service.Visuals,"route")!;
        Check(shown.Options.Any(o=>o.Reasons.Any(r=>r.Contains("7/"))),"open map replans after live HP change");
        Check(rng==JsonSerializer.Serialize(state.Rng.ToSerializable()),"preview and drawing leave run RNG unchanged");
        await Shot0620("03-route-low-health");
        service.Panel.SelectTab(3);
        var settings=tabs.GetChild<ScrollContainer>(3).GetChild<VBoxContainer>(0);
        var toggles=settings.GetChildren().OfType<CheckButton>().ToArray();
        toggles[1].ButtonPressed=false;await Delay(.2);
        Check(!service.Panel.DrawRoutes&&!AdvisorDisplaySettings.Load().Routes,"route switch persists independently");
        if(rendered)Check(service.Visuals.RouteSegments==0,"route disabled removes line immediately");
        toggles[1].ButtonPressed=true;service.Panel.SelectTab(0);
        NMapScreen.Instance.Close(false);
        await CreatureCmd.SetCurrentHp(service.Player.Creature,70);
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
        service.Player.Gold=600;NMerchantRoom.Instance!.OpenInventory();await Delay(1.5);
        await Shot0620("04-shop");
        if(rendered)Check(service.Visuals.VisibleItemKey!=null,"native shop highlights current recommendation");
        service.Panel.SelectTab(3);toggles[0].ButtonPressed=false;await Delay(.2);
        Check(!AdvisorDisplaySettings.Load().Items&&AdvisorDisplaySettings.Load().Routes,"item switch persists without changing route switch");
        if(rendered)Check(service.Visuals.VisibleItemKey==null,"item disabled removes frame immediately");
        await Shot0620("05-settings");toggles[0].ButtonPressed=true;service.Panel.SelectTab(0);
        GameActions.Invoke(NMerchantRoom.Instance.Inventory,"Close");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance.Close(false);
        var cards=new CardModel[]{state.CreateCard<Offering>(service.Player),state.CreateCard<StrikeIronclad>(service.Player),state.CreateCard<DefendIronclad>(service.Player)};
        var offer=new RewardsSet(service.Player).WithCustomRewards([new CardReward(cards,CardCreationSource.Encounter,service.Player,CardCreationOptions.ForRoom(service.Player,RoomType.Boss))]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"UI card reward fixture");
        var button=GameActions.Descendants<NRewardButton>((Node)Screen()!).First(b=>b.Reward is CardReward);
        var claim=GameActions.ClaimReward(button);await Until(()=>service.Decision?.Kind=="cards","UI card advice");await Delay(.7);
        await Shot0620("06-cards");
        if(rendered)Check(service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"card border matches displayed recommendation");
        var choice=int.Parse(service.Decision!.Evaluation.RecommendedKey![5..]);
        var target=service.CardScreen!.GetCardHolder(service.CardOptions[choice].Card);
        await ClickUi0620(target.Hitbox);await Until(()=>claim.IsCompleted,"manual card click through recommendation frame");await claim;
        Check(service.Player.Deck.Cards.Contains(cards[choice]),"manual click through painted card acquires native reward");
        if(Screen() is NRewardsScreen)RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();await offer;
        service.Panel.SelectTab(4);await Shot0620("07-feedback");service.Panel.SelectTab(2);
        await PotionCmd.TryToProcure(ModelDb.Potion<FirePotion>().ToMutable(),service.Player);
        MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance!.Clear();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster,showTransition:false);await Delay(3);
        NModalContainer.Instance.Clear();
        var solver=new SolverBridge();solver.Bind();solver.Stop(CombatManager.Instance.DebugOnlyGetState()!);await Delay(1);
        // Isolated fixture only: solver defaults to an expanded window over the HUD.
        // Arrange its own window at the right, as in the user's real configuration,
        // so the test measures assistant interception rather than the solver's header.
        var overlay=AccessTools.TypeByName("CombatSolver.SolverOverlay");
        AccessTools.Method(overlay,"SetCollapsed").Invoke(null,[true]);
        AccessTools.Field(overlay,"_requestedPanelPosition").SetValue(null,new Vector2(1280,210));
        AccessTools.Field(overlay,"_panelPosition").SetValue(null,new Vector2(1280,210));
        AccessTools.Method(overlay,"ApplyResponsiveLayout").Invoke(null,null);await Delay(.4);
        await Shot0620("08-before-potion");
        var holder=GameActions.Descendants<NPotionHolder>(NRun.Instance!).First(p=>p.Potion?.Model is FirePotion);
        GD.Print("[StrategistLab] potion holder="+holder.GetPath()+" rect="+holder.GetGlobalRect()+" visible="+holder.IsVisibleInTree()+" enabled="+holder.IsEnabled);
        await ClickUi0620(holder);
        GD.Print("[StrategistLab] potion hover="+NGame.Instance!.GetViewport().GuiGetHoveredControl()?.GetPath());
        var popup=GameActions.Field(holder,"_popup") as NPotionPopup;
        Check(popup!=null&&popup.IsVisibleInTree(),"manual potion click opens popup while loop page is displayed");
        await Shot0620("08-potion-popup");
        var use=(NClickableControl)GameActions.Field(popup!,"_useButton")!;
        Check(use.IsEnabled,"native throw action enabled");
        // PushInput exercises GUI hit testing without moving the user's desktop pointer.
        // The offscreen popup's outside-click handler reads the physical pointer instead;
        // disable only that dismiss handler in this isolated fixture.
        popup!.SetProcessInput(false);
        GD.Print("[StrategistLab] throw button="+use.GetGlobalRect()+" cancel="+GameActions.Invoke(holder,"ShouldCancelTargeting"));
        await ClickUi0620(use);await Delay(.4);
        Check(MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager.Instance.IsInSelection,"manual throw button starts native targeting");
        var enemy=CombatManager.Instance.DebugOnlyGetState()!.Enemies!.First(e=>e.IsAlive);
        var enemyHitbox=NCombatRoom.Instance!.GetCreatureNode(enemy)!.Hitbox;
        GD.Print("[StrategistLab] enemy hitbox="+enemyHitbox.GetGlobalRect());
        await ClickUi0620(enemyHitbox);
        GD.Print("[StrategistLab] enemy hover="+NGame.Instance!.GetViewport().GuiGetHoveredControl()?.GetPath());
        await Shot0620("08-after-throw");
        await Until(()=>!service.Player.Potions.Any(p=>p is FirePotion),"manual potion throw reaches enemy through assistant UI");
        Check(!service.Autopilot.Enabled,"visualization and manual potion use never enable takeover");
        var collapse=GameActions.Descendants<Button>(panel).First(b=>b.Text==UiLocalization.Text("收起"));await ClickUi0620(collapse);
        await Shot0620("09-collapsed");
        Check(panel.Size.Y<120&&!loop.IsVisibleInTree(),"collapsed panel compact and loop hit area hidden");
    }
}
