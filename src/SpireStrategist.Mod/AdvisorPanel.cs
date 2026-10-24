using Godot;
using SpireStrategist.Core;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;

namespace SpireStrategist.Mod;

// Built-in Godot controls only: no custom script registration or additional PCK required.
internal sealed class AdvisorPanel
{
    private readonly CanvasLayer layer;
    private readonly RichTextLabel body;
    private readonly VBoxContainer details;
    private readonly Label status;
    private readonly Button agree;
    private readonly Button disagree;
    private readonly Button autoButton;
    private readonly Label autoStatus;
    private readonly Label integrations;
    private readonly CheckButton fullAutoButton;
    private bool collapsed;
    private readonly PanelContainer panel;
    private readonly string layoutPath;
    private readonly Godot.Timer dragTimer;
    private Vector2 dragOffset;
    private PanelLayout layout;
    private readonly VBoxContainer feedbackEditor;
    private readonly CheckButton reviewPause;
    internal bool ReviewPaused => reviewPause.ButtonPressed || feedbackEditor.Visible;
    internal void SetReviewPaused(bool paused)=>reviewPause.SetPressedNoSignal(paused);
    private readonly TabContainer tabs;
    private readonly RichTextLabel stateBody;
    private readonly Label summary;
    private readonly Control loopHost;
    private readonly Label compactStatus;
    private AdvisorDisplaySettings display = AdvisorDisplaySettings.Load();
    internal bool HighlightItems => display.Items;
    internal bool DrawRoutes => display.Routes;
    internal bool Visible => GodotObject.IsInstanceValid(panel)&&panel.IsInsideTree()&&panel.IsVisibleInTree();
    internal Rect2? LoopRect => GodotObject.IsInstanceValid(loopHost)&&loopHost.IsInsideTree()&&loopHost.IsVisibleInTree() ? new Rect2(loopHost.GetGlobalTransformWithCanvas().Origin, loopHost.Size) : null;
    internal void SelectTab(int index) { tabs.CurrentTab = index; Callable.From(ClimbController.RefreshUi).CallDeferred(); }
    internal AdvisorPanel(Node parent, Action<string,string> feedback, Action beginFeedback, Action export, Action toggleAuto,Action<bool> setFullAuto)
    {
        layer = new CanvasLayer { Name = "SpireStrategistOverlay", Layer = 90 };
        parent.AddChild(layer);
        layoutPath = Path.Combine(OS.GetUserDataDir(), "SpireStrategist", "panel-layout.json");
        try { layout = JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(layoutPath), Journal.Json) ?? new(Y:190); }
        catch { layout = new(Y:190); }
        collapsed = layout.Collapsed;
        panel = new PanelContainer { Name="AdvisorPanel", Position = new Vector2(layout.X,layout.Y), CustomMinimumSize = new Vector2(390, 0), Theme=AdvisorUi.Theme() };
        panel.AddThemeStyleboxOverride("panel", AdvisorUi.Surface());
        layer.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation",8); panel.AddChild(box);
        var header = new HBoxContainer(); box.AddChild(header);
        var title = new Label { Text = "策略助手", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move,
            TooltipText = "v0.6.22 · 拖动标题可在全屏内移动；双击复位。请自行避开药水等操作区域。" };
        title.AddThemeColorOverride("font_color",AdvisorUi.Accent); header.AddChild(title);
        var toggle = AdvisorUi.Button(collapsed ? "展开" : "收起"); header.AddChild(toggle);
        autoButton = AdvisorUi.Button("开始接管");
        autoButton.TooltipText="接管当前单局；常规动作前等待2秒，商店购买等待8秒。停止立即取消待执行动作，不自动开下一局。";
        header.AddChild(autoButton); autoButton.Pressed += toggleAuto;
        compactStatus=AdvisorUi.Text("建议模式 · v0.6.22"); box.AddChild(compactStatus);
        details = new VBoxContainer { Visible = !collapsed }; box.AddChild(details);
        summary=AdvisorUi.Text(""); details.AddChild(summary);
        var pauseRow=new HBoxContainer(); details.AddChild(pauseRow);
        reviewPause=new CheckButton{Text="暂停操作 / 查看建议",FocusMode=Control.FocusModeEnum.None,
            TooltipText="暂停后续自动动作；已经提交的动作或求解器可能仍在完成。取消勾选后恢复。"};
        pauseRow.AddChild(reviewPause);
        // Retain the internal state API; the primary button is the single visible takeover control.
        fullAutoButton=new CheckButton{Visible=false}; details.AddChild(fullAutoButton);
        fullAutoButton.Toggled+=value=>setFullAuto(value);
        autoStatus=AdvisorUi.Text("建议模式 · 点击开始接管");
        tabs=new TabContainer{CustomMinimumSize=new Vector2(366,390)}; details.AddChild(tabs);
        RichTextLabel Reader() => new(){BbcodeEnabled=false,ScrollActive=true,SelectionEnabled=true,FitContent=false,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};
        var advice=new VBoxContainer{Name="建议"};tabs.AddChild(advice);advice.AddChild(autoStatus);
        body=Reader();advice.AddChild(body);
        stateBody=Reader(); stateBody.Name="局面"; tabs.AddChild(stateBody);
        loopHost=new Control{Name="循环",MouseFilter=Control.MouseFilterEnum.Ignore}; tabs.AddChild(loopHost);
        var displayPage=new ScrollContainer{Name="显示",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};tabs.AddChild(displayPage);
        var settings=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};displayPage.AddChild(settings);settings.AddThemeConstantOverride("separation",12);
        settings.AddChild(AdvisorUi.Text("推荐标记"));
        var itemToggle=new CheckButton{Text="选牌 / 商店推荐框",ButtonPressed=display.Items,FocusMode=Control.FocusModeEnum.None};
        var routeToggle=new CheckButton{Text="实时推荐路线",ButtonPressed=display.Routes,FocusMode=Control.FocusModeEnum.None};
        settings.AddChild(itemToggle);settings.AddChild(routeToggle);
        settings.AddChild(AdvisorUi.Text("绿色框：当前推荐或待执行的物品。\n金色实线：下一步；青色虚线：后续预计路径。\n随血量、牌组、遗物和地图变化重算。\n标记不接收点击；关闭仅隐藏显示，不改变决策。"));
        void SaveDisplay() { try { display.Save(); } catch(Exception ex) { Status("显示设置保存失败："+ex.Message); } }
        itemToggle.Toggled+=value=>{display=display with{Items=value};SaveDisplay();};
        routeToggle.Toggled+=value=>{display=display with{Routes=value};SaveDisplay();};
        integrations=AdvisorUi.Text(""); settings.AddChild(integrations);
        UiLocalization.AddPicker(settings);
        var feedbackScroll=new ScrollContainer{Name="反馈",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};tabs.AddChild(feedbackScroll);
        var feedbackPage=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};feedbackScroll.AddChild(feedbackPage);feedbackPage.AddThemeConstantOverride("separation",10);
        var actions=new HBoxContainer();feedbackPage.AddChild(actions);
        agree=AdvisorUi.Button("赞同");disagree=AdvisorUi.Button("有异议");var save=AdvisorUi.Button("导出反馈包");
        agree.Disabled=disagree.Disabled=true;
        actions.AddChild(agree);actions.AddChild(disagree);actions.AddChild(save);
        agree.Pressed+=()=>feedback("agree","");save.Pressed+=export;
        feedbackEditor=new VBoxContainer{Visible=false};feedbackPage.AddChild(feedbackEditor);
        var reason=new TextEdit{PlaceholderText="你希望选什么？为什么？",CustomMinimumSize=new Vector2(340,110)};feedbackEditor.AddChild(reason);
        var feedbackButtons=new HBoxContainer();feedbackEditor.AddChild(feedbackButtons);
        var submit=AdvisorUi.Button("保存异议");var cancel=AdvisorUi.Button("取消");feedbackButtons.AddChild(submit);feedbackButtons.AddChild(cancel);
        disagree.Pressed+=()=>{beginFeedback();reason.Text="";feedbackEditor.Visible=true;reason.GrabFocus();};
        submit.Pressed+=()=>{feedback("disagree",reason.Text.Length>2000?reason.Text[..2000]:reason.Text);feedbackEditor.Visible=false;};
        cancel.Pressed+=()=>feedbackEditor.Visible=false;
        feedbackPage.AddChild(AdvisorUi.Text("只保存在本机，不会自动上传或训练。\n反馈给作者：保存意见 → 导出反馈包 → 发送 ZIP。无需 GPT。\n反馈包包含本局；循环批次汇总及逐局记录另保存在本地日志目录。"));
        status=AdvisorUi.Text("可记录当前建议的赞同或异议");feedbackPage.AddChild(status);
        // Detailed state remains available without forcing the full message onto the HUD.

        tabs.TabChanged+=_=>Callable.From(ClimbController.RefreshUi).CallDeferred();
        loopHost.Resized+=()=>Callable.From(ClimbController.RefreshUi).CallDeferred();
        loopHost.VisibilityChanged+=()=>Callable.From(ClimbController.RefreshUi).CallDeferred();
        toggle.Pressed+=()=>{collapsed=!collapsed;details.Visible=!collapsed;toggle.Text=collapsed?"展开":"收起";panel.Size=Vector2.Zero;SaveLayout();ClimbController.RefreshUi();};
        dragTimer=new Godot.Timer{WaitTime=.016};layer.AddChild(dragTimer);
        dragTimer.Timeout+=()=>{
            if(!Input.IsMouseButtonPressed(MouseButton.Left)){dragTimer.Stop();SaveLayout();return;}
            panel.Position=panel.GetGlobalMousePosition()-dragOffset;ClampPosition();ClimbController.RefreshUi();
        };
        title.GuiInput+=input=>{
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:true} mouse){
                if(mouse.DoubleClick){panel.Position=new Vector2(16,190);SaveLayout();}
                else{dragOffset=panel.GetGlobalMousePosition()-panel.Position;dragTimer.Start();}title.AcceptEvent();
            }
        };
        AdvisorUi.PassiveContainers(box);title.MouseFilter=Control.MouseFilterEnum.Stop;
        var layoutTimer=new Godot.Timer{WaitTime=.25};layer.AddChild(layoutTimer);
        layoutTimer.Timeout+=()=>{UiLocalization.Apply(panel);tabs.CustomMinimumSize=new Vector2(366,Math.Clamp(panel.GetViewportRect().Size.Y-380,240,440));ClampPosition();};layoutTimer.Start();
        UiLocalization.Apply(panel);
        Callable.From(ClampPosition).CallDeferred();
    }

    internal void UpdateState(RunSnapshot s)
    {
        var text = new StringBuilder();
        var solverError=SolverBridge.CompatibilityError();
        summary.Text=$"第 {s.Act} 幕 · {s.Floor} 层 · N{s.Ascension}    生命 {s.Hp}/{s.MaxHp}\n金币 {s.Gold}    牌组 {s.Deck.Length} 张";
        integrations.Text=(solverError==null?"战斗交给求解器 · CombatSolver":AdvisorUi.SolverLocked+"\n"+solverError)+
            (s.Map?.LongMap==true?$"\n更多层数已加载 · 每幕额外 {s.Map.ExtraLayers} 层 · 额外 {s.Map.ExtraActs} 幕":"");
        text.AppendLine($"第 {s.Act} 幕 · {s.Floor} 层 · 进阶 {s.Ascension}");
        if(s.Map is {} map)text.AppendLine($"地图 {map.Rows} 行 · 共 {map.TotalActs} 幕"+(map.LongMap?$" · 每幕额外 {map.ExtraLayers} 层":""));
        if(s.Map?.Error is {} error)text.AppendLine(error);
        text.AppendLine($"生命 {s.Hp}/{s.MaxHp}  金币 {s.Gold}  牌组 {s.Deck.Length} 张");
        if (s.Hidden is { SourceUnchanged: true } hidden)
        {
            var act = hidden.Acts.FirstOrDefault(a => a.Act == s.Act);
            text.AppendLine($"隐藏信息：{hidden.Streams.Length} 条随机流已读取（副本预览）");
            if (act != null)
            {
                text.AppendLine("下次普通战：" + EncounterName(NextEncounter(act.NormalEncounters, act.NormalVisited)));
                text.AppendLine("精英池下个：" + EncounterName(NextEncounter(act.EliteEncounters, act.EliteVisited)));
                if(s.Map?.LongMap==true)text.AppendLine("骷髅房会替换为小首领，不使用上述精英预测。");
                text.AppendLine("首领：" + EncounterName(act.Boss));
            }
            text.AppendLine("后续取决于路径与随机流消耗；完整数据可导出。");
        }
        stateBody.Text = text.ToString();UiLocalization.Apply(panel);
    }
    internal void Show(RunSnapshot s, Evaluation? evaluation, string? decisionId, string? message = null)
    {
        UpdateState(s);
        if(feedbackEditor.Visible)return;
        var text=new StringBuilder();
        if (evaluation == null) text.AppendLine(message ?? "等待选牌或营火。当前状态已记录。");
        else
        {
            var best = evaluation.Options.FirstOrDefault(o => o.Key == evaluation.RecommendedKey);
            text.AppendLine(best != null ? $"建议：{best.Name}" : "暂不作整体推荐");
            text.AppendLine($"记录：{decisionId?[..8]}");
            text.AppendLine("分数用于相对比较，不是胜率。");
            foreach (var option in evaluation.Options)
            {
                text.AppendLine();
                text.AppendLine($"{option.Name}：{(!option.Enabled ? "不可选" : option.Score?.ToString("0.0") ?? "未适配")}");
                foreach (var part in option.Parts.OrderByDescending(p => Math.Abs(p.Value)))
                    text.AppendLine($"  {part.Value:+0.0;-0.0;0}  {part.Reason}");
                foreach (var note in option.Notes) text.AppendLine("  " + note);
            }
            text.AppendLine();
            foreach (var warning in evaluation.Warnings) text.AppendLine("· " + warning);
        }
        body.Text = text.ToString();
        agree.Disabled = disagree.Disabled = evaluation == null;
        status.Text = "可记录赞同或异议";UiLocalization.Apply(panel);
    }

    internal void Status(string message) {status.Text = message;UiLocalization.Apply(panel);}
    internal void Automation(bool enabled,bool full,string message) {autoButton.Text=enabled?"停止接管":"开始接管";fullAutoButton.SetPressedNoSignal(full);autoStatus.Text=message;compactStatus.Text=enabled?(ReviewPaused?"已暂停 · 查看建议":message.Contains("请手动")||message.Contains("需手动")?"等待人工处理 · 详情见建议":ClimbController.Enabled?"循环接管中":"单局接管中"):"建议模式 · v0.6.22";compactStatus.TooltipText=message;UiLocalization.Apply(panel);}
    internal void ShowRoute(RouteEvaluation route)
    {
        if(feedbackEditor.Visible)return;
        var text=new StringBuilder("路线评分（能力指标不是胜率）\n");
        foreach(var option in route.Options)
        {
            text.AppendLine($"{(option.Node==route.RecommendedNode?"推荐 ":"")}{PartialAutopilot.RouteName(option.Kind)} [{option.Node}]：{option.Score:F1}");
            foreach(var reason in option.Reasons)text.AppendLine("  "+reason);
            foreach(var shop in option.Shops)text.AppendLine($"  到商店 [{shop.Node}] 预计 {shop.Expected:F0} 金币，情景区间 {shop.Low:F0}–{shop.High:F0}；新遗物潜在加成约 {shop.PossibleRelicBonus:F1}");
        }
        foreach(var note in route.Notes)text.AppendLine("· "+note);
        body.Text=text.ToString();agree.Disabled=disagree.Disabled=false;UiLocalization.Apply(panel);
    }
    internal void ShowShop(ShopEvaluation plan)
    {
        if(feedbackEditor.Visible)return;
        var text=new StringBuilder("商店评估（待校准）\n");
        foreach(var choice in plan.Choices.OrderByDescending(c=>c.Score))
        {
            text.AppendLine($"{(choice==plan.Recommendation?"推荐 ":"")}{(choice.Kind=="remove"?"删":"买")} {choice.Name}：{choice.Price} 金币 / {choice.Score:F1} 分");
            foreach(var reason in choice.Reasons)text.AppendLine("  "+reason);
        }
        foreach(var note in plan.Notes)text.AppendLine(note);
        body.Text=text.ToString();agree.Disabled=disagree.Disabled=false;UiLocalization.Apply(panel);
    }
    private static string? NextEncounter(string[] pool, int visited) => pool.Length == 0 ? null : pool[Math.Max(0, visited) % pool.Length];
    private static string EncounterName(string? id)
    {
        if (id == null) return "未知";
        try { return ModelDb.GetById<EncounterModel>(ModelId.Deserialize(id)).Title.GetFormattedText(); }
        catch { return id; }
    }
    private void ClampPosition()
    {
        var viewport = panel.GetViewportRect().Size;
        layout = new PanelLayout(panel.Position.X, panel.Position.Y, collapsed).Clamp(panel.Size.X, panel.Size.Y, viewport.X, viewport.Y);
        panel.Position = new Vector2(layout.X,layout.Y);
        layout=layout with{X=panel.Position.X,Y=panel.Position.Y};
    }
    private void SaveLayout()
    {
        ClampPosition();
        try { Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!); File.WriteAllText(layoutPath, JsonSerializer.Serialize(layout, Journal.Json)); }
        catch (Exception ex) { status.Text = "位置保存失败：" + ex.Message; }
    }
    internal void Error(string message)
    {
        body.Text = "助手暂时停止本次建议：\n" + message + "\n\n游戏仍可手动操作。";
        agree.Disabled = disagree.Disabled = true;UiLocalization.Apply(panel);
    }
}
