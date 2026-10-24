using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using SpireStrategist.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpireStrategist.Mod;

// Lives under NGame, not NRun: the stop control remains available across game-over/menu transitions.
internal static class ClimbController
{
    internal static readonly ClimbBatch Batch=new();
    internal static bool Enabled=>Batch.Enabled;
    internal static bool LaunchInProgress=>launching;
    private static NGame? game;
    private static CheckButton? enabledButton,stopButton;
    private static Label? label;
    private static VBoxContainer? controls;
    private static PanelContainer? shell;
    private static Label? solverNotice;
    private static HBoxContainer? floatingHeader;
    private static ScrollContainer? contentScroll;
    private static Button? collapseButton,floatingStop;
    private static PanelLayout? floatingLayout;
    private static string FloatingLayoutPath=>Path.Combine(OS.GetUserDataDir(),"SpireStrategist","climb-panel-layout.json");
    private static Godot.Timer? dragTimer;
    private static Vector2 dragOffset;
    internal static void RefreshUi()=>Refresh();
    private static SpinBox? ascensionInput;
    private static OptionButton? characterInput;
    private static bool refreshingPreferences;
    private static ClimbPreferences preferences=new();
    private static string PreferencesPath=>Path.Combine(OS.GetUserDataDir(),"SpireStrategist","climb-settings.json");
    private static HBoxContainer? ascensionRow;
    private static Button? exportLast;
    private static RunState? attached;
    private static Journal? journal;
    private static bool launching,transitioning;
    private static ulong endedAt;
    private static int generation,ascension;
    private static bool shouldSave=true;
    private static RunState? conceding;
    private static ulong concessionStarted;
    private const string ConcessionReason="solver_death_turn：求解器当前路线将在本回合死亡，助手主动放弃；不代表已证明本局无解";
    internal static int RelicReserve {get;set;}=100;
    private static string randomSalt="";
    internal static string Status {get;private set;}="自动爬塔已关闭";
    internal static void Initialize(NGame instance)
    {
        if(game!=null&&GodotObject.IsInstanceValid(game))return;
        game=instance;
        try{preferences=ClimbPreferences.Load(PreferencesPath);preferences.Save(PreferencesPath);}
        catch(Exception ex){GD.PrintErr("[SpireStrategist] 无法读取爬塔设置："+ex.Message);}
        var layer=new CanvasLayer{Layer=91,Name="StrategistClimbControls"};game.AddChild(layer);
        exportLast=new Button{Text="导出最近一局反馈",
            TooltipText="仅导出最近一局，不包含整个循环批次。全部对局和批次汇总仍保存在本地 SpireStrategist 日志目录。"};

        exportLast.Pressed+=()=>Entry.Safe(()=>
        {
            var path=AdvisorService.ExportLastFeedback();
            if(path==null){exportLast.Text="本次启动还没有可导出的已结束对局";return;}
            exportLast.Text="已导出反馈包 · 把 ZIP 发给作者";
            OS.ShellOpen(Path.GetDirectoryName(path)!);
        });
        shell=new PanelContainer{Name="ClimbDock",Theme=AdvisorUi.Theme()};
        shell.AddThemeStyleboxOverride("panel",AdvisorUi.Surface());layer.AddChild(shell);
        try{floatingLayout=JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(FloatingLayoutPath),Journal.Json);}catch{floatingLayout=null;}
        var outer=new VBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore};shell.AddChild(outer);
        floatingHeader=new HBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore};outer.AddChild(floatingHeader);
        var dragTitle=new Label{Text="策略助手",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,
            MouseFilter=Control.MouseFilterEnum.Stop,MouseDefaultCursorShape=Control.CursorShape.Move,
            TooltipText="拖动标题可在全屏内移动；双击复位。位置与收起状态会保存。"};
        dragTitle.AddThemeColorOverride("font_color",AdvisorUi.Accent);floatingHeader.AddChild(dragTitle);
        collapseButton=AdvisorUi.Button("收起");floatingHeader.AddChild(collapseButton);
        floatingStop=AdvisorUi.Button("停止接管");floatingHeader.AddChild(floatingStop);
        floatingStop.Pressed+=()=>Stop("玩家从浮动面板停止接管");
        collapseButton.Pressed+=()=>{
            floatingLayout=(floatingLayout??new(shell.Position.X,shell.Position.Y)) with{Collapsed=!(floatingLayout?.Collapsed??false)};
            Refresh();SaveFloatingLayout();};
        dragTimer=new Godot.Timer{WaitTime=.016};layer.AddChild(dragTimer);
        dragTimer.Timeout+=()=>{
            if(!Input.IsMouseButtonPressed(MouseButton.Left)){dragTimer.Stop();SaveFloatingLayout();return;}
            MoveFloatingPanel(shell.GetGlobalMousePosition()-dragOffset);};
        dragTitle.GuiInput+=input=>{
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:true} mouse){
                if(mouse.DoubleClick){MoveFloatingPanel(new Vector2(Math.Max(0,shell.GetViewportRect().Size.X-410),190));SaveFloatingLayout();}
                else{dragOffset=shell.GetGlobalMousePosition()-shell.Position;dragTimer.Start();}dragTitle.AcceptEvent();}
        };
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=Control.SizeFlags.ExpandFill};outer.AddChild(scroll);contentScroll=scroll;
        var box=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,MouseFilter=Control.MouseFilterEnum.Ignore};
        box.AddThemeConstantOverride("separation",10);scroll.AddChild(box);
        box.AddChild(AdvisorUi.Text("循环爬塔 · 快速测试",310));
        solverNotice=AdvisorUi.Text("",310);box.AddChild(solverNotice);
        controls=box;

        var font=new SystemFont{FontNames=["Microsoft YaHei","Noto Sans CJK SC"]};
        box.Theme=new Theme{DefaultFont=font,DefaultFontSize=15};
        var row=new VBoxContainer();box.AddChild(row);
        enabledButton=new CheckButton{Text="循环爬塔",FocusMode=Control.FocusModeEnum.None,
            TooltipText="快速连续战士随机种子测试；局间等待1秒。完整自动对局按角色/难度记录最高连胜。关闭交还控制；中途加入和人工参与会中断当前连胜。"};
        stopButton=new CheckButton{Text="本局结束后停止",FocusMode=Control.FocusModeEnum.None,
            TooltipText="打完当前局，保存结果，不再开新局。"};
        row.AddChild(enabledButton);row.AddChild(stopButton);
        ascensionRow=new HBoxContainer();box.AddChild(ascensionRow);
        ascensionRow.AddChild(new Label{Text="新局进阶"});
        ascensionInput=new SpinBox{MinValue=0,MaxValue=10,Step=1,Value=preferences.Ascension};
        ascensionRow.AddChild(ascensionInput);
        characterInput=new OptionButton{FocusMode=Control.FocusModeEnum.None,
            TooltipText="角色选择会保存；其他四个角色暂不可用，选择后无法开启循环。"};
        foreach(var name in new[]{"铁甲战士","静默猎手（暂不可用）","储君（暂不可用）","亡灵契约师（暂不可用）","故障机器人（暂不可用）"})characterInput.AddItem(name);
        characterInput.Select(Array.IndexOf(ClimbPreferences.Characters,preferences.Character));
        box.AddChild(characterInput);
        UiLocalization.AddPicker(box);
        box.AddChild(new Label{Text="标准模式 · 随机种子 · 勾选即开局"});
        ascensionInput.ValueChanged+=value=>
        {if(refreshingPreferences)return;preferences.Ascension=(int)value;SavePreferences();Refresh();};
        characterInput.ItemSelected+=index=>
        {preferences.Character=ClimbPreferences.Characters[(int)index];SavePreferences();Refresh();};
        // Persistent controls must not retain ui_accept focus across scene loading.
        // Only activation is a command; a checkbox state update is just presentation.
        // Finish the GUI input callback before loading scenes (which may pump input).
        enabledButton.Pressed+=()=>
        {
            var requested=!Enabled;
            Callable.From(()=>Entry.Safe(()=>
            {
                SetEnabled(requested);
                Write("control_requested",new{requested,enabled=Enabled,transitioning,
                    scene=game.RootSceneContainer?.CurrentScene?.GetType().Name});
            })).CallDeferred();
        };
        stopButton.Toggled+=value=>{Batch.RequestStopAfterRun(value);Write("stop_after_run",new{value});Refresh();};
        label=new Label{Text=Status,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new Vector2(310,0)};
        box.AddChild(label);box.AddChild(exportLast);AdvisorUi.PassiveContainers(box);
        var timer=new Godot.Timer{WaitTime=.5};game.AddChild(timer);
        timer.Timeout+=()=>Entry.Safe(Tick);timer.Start();
    }
    internal static void MoveFloatingPanel(Vector2 position)
    {
        if(shell==null||AdvisorService.Current?.Panel.LoopRect!=null)return;
        var viewport=shell.GetViewportRect().Size;
        floatingLayout=new PanelLayout(position.X,position.Y,floatingLayout?.Collapsed??false)
            .Clamp(shell.Size.X,shell.Size.Y,viewport.X,viewport.Y);
        shell.Position=new Vector2(floatingLayout.X,floatingLayout.Y);
    }
    internal static void SaveFloatingLayout()
    {
        if(floatingLayout==null)return;
        try{Directory.CreateDirectory(Path.GetDirectoryName(FloatingLayoutPath)!);File.WriteAllText(FloatingLayoutPath,JsonSerializer.Serialize(floatingLayout,Journal.Json));}
        catch(Exception ex){GD.PrintErr("[SpireStrategist] 循环面板位置保存失败："+ex.Message);}
    }
    internal static void SetEnabled(bool value)
    {
        if(!value){Stop("玩家立即关闭，交还控制");return;}
        if(Enabled)return;
        if(transitioning)
        {Status="新局仍在加载，接管已取消；加载完成后可以重新开启。";Refresh();return;}
        var service=AdvisorService.Current;
        if(SolverBridge.CompatibilityError() is {} error){Status=error;Refresh();return;}
        var menu=game?.RootSceneContainer?.CurrentScene is NMainMenu&&service==null;
        if(menu&&SaveManager.Instance.HasRunSave)
        {Status="存在未完成的单人存档：请先继续该局再开启接管，或手动结束该局。";Refresh();return;}
        if(menu&&!preferences.Supported)
        {Status="所选角色暂不可用，请选择铁甲战士。";Refresh();return;}
        if(!menu&&(service==null||service.State.IsGameOver||service.State.GameMode!=GameMode.Standard||service.State.Players.Count!=1||
           service.Player.Character is not Ironclad||service.State.Modifiers.Count!=0)
        )
        {Status="请在单人标准战士局内开启（不支持每日/自定义模式）。";Refresh();return;}
        generation++;endedAt=0;attached=null;
        ascension=menu?(int)(ascensionInput?.Value??0):service!.State.AscensionLevel;
        preferences.Ascension=ascension;preferences.Character="Ironclad";SavePreferences();
        shouldSave=menu||RunManager.Instance.ShouldSave;
        journal??=new Journal(Path.Combine(OS.GetUserDataDir(),"SpireStrategist","climb-batches",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]));
        Batch.Enable();
        if(menu){Status="正在启动新随机种子";transitioning=true;_=NextRun(generation,true);}
        else Attach(service!,service!.State.TotalFloor==0&&service.State.CurrentRoom is null or MapRoom);
        Refresh();
    }
    internal static void Attach(AdvisorService service,bool? full=null)
    {
        if(!Enabled)return;
        if(attached==service.State)return;
        if(Batch.Current!=null){Stop("检测到未经批次启动的换局/读档，本局标为中断");return;}
        attached=service.State;endedAt=0;
        randomSalt=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        Batch.Start(service.State.Rng.StringSeed,full??launching);
        preferences.Begin(Batch.Current!,"Ironclad",service.State.AscensionLevel);SavePreferences();
        service.Panel.SetReviewPaused(false);service.Autopilot.Enable(true);
        Write("run_start",new{run=Batch.Current,state=SnapshotReader.Read(service.State,service.Player),
            policy=Policy.Version,gameVersion=NGame.GetGameVersion(),randomSalt,relicReserve=RelicReserve,
            mods=ModManager.GetLoadedMods().Where(m=>m.manifest!=null).Select(m=>new{m.manifest!.id,m.manifest.version}),
            solverSettings=ReadSolverSettings()});
        Status="连续爬塔中"+(Batch.Current!.FullRun?"":" · 本局中途加入，单独记录");Refresh();
    }
    private static JsonElement? ReadSolverSettings()
    {
        var path=Path.Combine(OS.GetUserDataDir(),"combat_solver_settings.json");
        return File.Exists(path)?JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)):null;
    }
    internal static int RandomIndex(string context,int count)
    {
        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes(randomSalt+"|"+context));
        return new Random(BitConverter.ToInt32(bytes,0)).Next(count);
    }
    internal static void Ended(RunState? state,bool win)
    {
        var concession=state!=null&&ReferenceEquals(state,conceding);
        if(concession){conceding=null;AdvisorService.Current?.RecordAuto("solver_concession_end",new{outcome="loss",reason=ConcessionReason});}
        if(!Enabled||state!=attached||Batch.Current==null)return;
        if(RunManager.Instance.IsAbandoned&&!concession){Stop("玩家放弃本局，记录为中断");return;}
        var service=AdvisorService.Current;
        var result=Batch.Finish(win&&!concession,concession?ConcessionReason:null);endedAt=Time.GetTicksMsec();
        preferences.Complete(result);SavePreferences();
        Write("run_end",new{result,state=service==null?null:SnapshotReader.Read(service.State,service.Player)});
        service?.Autopilot.Pause("本局完成");
        Status=Enabled?"本局已记账，等待原生结算就绪后快速开始下一局":"本局完成，已按预中断要求停止";Refresh();
    }
    internal static void Concede(AdvisorService service,int turn)
    {
        if(conceding!=null||!service.Autopilot.Enabled||!service.Autopilot.FullAutomatic)return;
        conceding=service.State;
        concessionStarted=Time.GetTicksMsec();
        service.RecordAuto("solver_concession_requested",new{turn,reason=ConcessionReason,state=SnapshotReader.Read(service.State,service.Player)});
        service.Autopilot.Pause("求解器在死亡回合停止，正在放弃并记录失败");
        Status="求解器死亡路线停止 · 正在结算失败";
        // Use the native abandon flow: native history truthfully remains abandoned.
        // The batch ledger separately records an autonomous strategic concession as loss.
        try{RunManager.Instance.Abandon();}
        catch {conceding=null;Stop("放弃调用失败，请检查游戏并导出日志");throw;}
    }
    internal static void Stop(string reason)
    {
        generation++;var result=Batch.Stop(reason);endedAt=0;
        preferences.Complete(result);SavePreferences();
        Write("stopped",new{reason,result});AdvisorService.Current?.Autopilot.Pause(reason);
        Status="自动爬塔已关闭 · "+reason;Refresh();
    }
    internal static void LeftRun(RunState state)
    {
        if(Enabled&&state==attached&&Batch.Current!=null&&!transitioning)Stop("本局未结算即离开，记录为中断");
    }
    internal static void Assisted(string reason)
    {Batch.MarkAssisted(reason);preferences.Assisted();SavePreferences();Write("assisted",new{reason,current=Batch.Current});}
    private static void Tick()
    {
        Refresh();
        if(conceding!=null)
        {
            if(Time.GetTicksMsec()-concessionStarted>30000)
            {conceding=null;Stop("放弃未在 30 秒内结算，请检查游戏；未自动开启新局");}
            return;
        }
        if(!Enabled||transitioning||game==null)return;
        if(endedAt>0)
        {
            if(Batch.StopAfterRun){Stop("已结算，取消下一局");return;}
            // OnEnded occurs before the native death/victory tasks finish. Starting a
            // new run there can leave an old task acting on the next RunManager state.
            // Shorten the optional delay, never the native settlement readiness check.
            var settled=GameActions.Descendants<MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen.NGameOverScreen>(game)
                .Any(s=>s.IsVisibleInTree()&&
                    (GameActions.Field(s,"_continueButton") is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl {IsEnabled:true}||
                     GameActions.Field(s,"_mainMenuButton") is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl {IsEnabled:true}));
            if(settled&&Time.GetTicksMsec()-endedAt>=AutomationTiming.NextRunDelay){transitioning=true;_=NextRun(generation);}
            return;
        }
        if(AdvisorService.Current is {} service&&!service.Autopilot.Enabled&&service.Player.Creature.CurrentHp>0)
            Stop("接管停止："+service.Autopilot.Status);
    }
    private static async Task NextRun(int token,bool fromMenu=false)
    {
        try
        {
            if(!Enabled||token!=generation)return;
            if(!fromMenu)await game!.ReturnToMainMenuAfterRun();
            if(!Enabled||token!=generation)return;
            var seed=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
            Write("launch_requested",new{seed,ascension});
            launching=true;
            // Native NCharacterSelectScreen.BeginRun stops menu music before launching.
            NAudioManager.Instance?.StopMusic();
            await game!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(),shouldSave,
                ActModel.GetDefaultList(),[],seed,GameMode.Standard,ascension);
            if(!Enabled||token!=generation){AdvisorService.Current?.Autopilot.Pause("换局期间已关闭自动爬塔");return;}
            if(AdvisorService.Current is {} service)Attach(service,true);
        }
        catch(Exception ex){Write("launch_error",new{error=ex.ToString()});Stop("新局启动失败，已保留日志");}
        finally{launching=false;transitioning=false;}
    }
    private static void Write(string type,object data)
    {
        journal?.Write(type,data);
        journal?.Latest("summary",new{version="0.6.21",Batch.Enabled,Batch.StopAfterRun,Batch.Current,
            Batch.Wins,Batch.Losses,completed=Batch.Wins+Batch.Losses,
            winRate=Batch.Wins+Batch.Losses==0?(double?)null:Batch.Wins/(double)(Batch.Wins+Batch.Losses),
            results=Batch.Results,streaks=preferences.Streaks,notes="从开局全程接管的自然结算及求解器死亡路线主动放弃计入胜负；主动放弃在 reason 中单列，中途加入、人工干预和中断另列。连胜按角色/难度，只计完整自动对局。"});
    }
    private static void SavePreferences()
    {
        try{preferences.Save(PreferencesPath);}
        catch(Exception ex){GD.PrintErr("[SpireStrategist] 无法保存爬塔设置："+ex.Message);Status="设置/连胜保存失败，请检查日志目录权限";}
    }
    private static void Refresh()
    {
        var compatible=SolverBridge.CompatibilityError()==null;
        if(exportLast!=null)exportLast.Visible=game?.RootSceneContainer?.CurrentScene is NMainMenu;
        var dock=AdvisorService.Current?.Panel.LoopRect;
        var menu=AdvisorService.Current==null&&(game?.RootSceneContainer?.CurrentScene is NMainMenu||launching||transitioning);
        if(shell!=null)
        {
            shell.Visible=menu||dock!=null||AdvisorService.Current is {Panel.Visible:false};
            var viewport=shell.GetViewportRect().Size;
            var floating=dock==null;
            var collapsed=floating&&(floatingLayout?.Collapsed??false);
            if(floatingHeader!=null)floatingHeader.Visible=floating;
            if(contentScroll!=null)contentScroll.Visible=!collapsed;
            if(collapseButton!=null)collapseButton.Text=collapsed?"展开":"收起";
            if(floatingStop!=null)floatingStop.Disabled=!Enabled;
            shell.Size=dock?.Size??new Vector2(390,collapsed?0:Math.Clamp(viewport.Y-230,300,570));
            if(dock is {} area)shell.Position=area.Position;
            else{
                floatingLayout??=new PanelLayout(Math.Max(16,viewport.X-410),190);
                MoveFloatingPanel(new Vector2(floatingLayout.X,floatingLayout.Y));
            }
        }
        if(solverNotice!=null)solverNotice.Text=compatible?"战斗交给求解器 · CombatSolver":AdvisorUi.SolverLocked+"\n"+SolverBridge.CompatibilityError();
        if(characterInput!=null)characterInput.Visible=AdvisorService.Current==null&&!Enabled;
        if(ascensionRow!=null)ascensionRow.Visible=AdvisorService.Current==null&&!Enabled;
        if(ascensionInput!=null)
        {
            refreshingPreferences=true;
            ascensionInput.MaxValue=SaveManager.Instance.Progress.GetStatsForCharacter(ModelDb.Character<Ironclad>().Id)?.MaxAscension??0;
            ascensionInput.Value=Math.Min(preferences.Ascension,ascensionInput.MaxValue);
            refreshingPreferences=false;
        }
        enabledButton?.SetPressedNoSignal(Enabled);stopButton?.SetPressedNoSignal(Batch.StopAfterRun);
        characterInput?.Select(Array.IndexOf(ClimbPreferences.Characters,preferences.Character));
        if(enabledButton!=null)enabledButton.Disabled=!Enabled&&(!compatible||AdvisorService.Current==null&&!preferences.Supported);
        if(stopButton!=null)stopButton.Disabled=!Enabled;
        var mode=ClimbPreferences.Mode(Enabled?"Ironclad":preferences.Character,Enabled?ascension:preferences.Ascension);
        var streak=preferences.Get(mode);
        if(label!=null)label.Text=$"{Status}\n所有已结算：{Batch.Results.Count(r=>r.Outcome=="win")} 胜 / {Batch.Results.Count(r=>r.Outcome=="loss")} 负"+
            $"\n完整自动样本：{Batch.Wins} 胜 / {Batch.Losses} 负；人工参与、中途加入或中断 {Batch.Results.Count(r=>!r.FullRun||r.Outcome=="interrupted")} 局"+
            $"\n{(Enabled?"铁甲战士":characterInput?.GetItemText(characterInput.Selected))} N{(Enabled?ascension:preferences.Ascension)}：当前连胜 {streak.Current} · 历史最高 {streak.Best}";
        if(shell!=null)UiLocalization.Apply(shell);
    }
}

[HarmonyPatch(typeof(NGame),nameof(NGame._Ready))]
internal static class ClimbReadyPatch {static void Postfix(NGame __instance)=>Entry.Safe(()=>ClimbController.Initialize(__instance));}
[HarmonyPatch(typeof(RunManager),nameof(RunManager.OnEnded))]
internal static class ClimbEndedPatch
{
    static void Prefix(RunManager __instance,out RunState? __state)=>__state=__instance.DebugOnlyGetState();
    static void Postfix(bool isVictory,RunState? __state)=>Entry.Safe(()=>ClimbController.Ended(__state,isVictory));
}
