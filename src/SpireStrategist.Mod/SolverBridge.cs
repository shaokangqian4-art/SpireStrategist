using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;

namespace SpireStrategist.Mod;

internal sealed class SolverBridge
{
    private Type? controller;
    private ulong? searchingSince;
    private bool adoptionRequested;
    private bool? previousAutoStart;
    private static bool stopHookInstalled;
    private static CombatState? deathCombat;
    private static int deathTurn;
    private static object? deathStamp;
    private static MethodInfo? captureStamp;
    internal static string? CompatibilityError()=>new SolverBridge().Bind();
    // Snapshot reads only observe an already validated bridge; they never install
    // hooks, enable full auto, or touch live combat/RNG.
    internal static bool AvailableForEvaluation => stopHookInstalled &&
        ModManager.GetLoadedMods().Any(m=>m.manifest?.id=="CombatSolver"&&m.manifest.version=="0.50.1");
    internal string? Bind()
    {
        var mod = ModManager.GetLoadedMods().FirstOrDefault(m => m.manifest?.id == "CombatSolver");
        if(mod==null)return "未安装战斗路线求解器；战斗请手动处理，连续爬塔不可用。";
        if (mod.manifest?.version != "0.50.1") return "求解器版本尚未适配；当前支持 0.50.1，连续爬塔不可用。";
        controller = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("CombatSolver.SolverController")).FirstOrDefault(t => t != null);
        if (controller == null) return "战斗求解器尚未加载。";
        foreach(var name in new[]{"SetFullAuto", "CanSolve", "StopSearchByUser", "CancelDeployment", "AdoptCurrentRoute"})
            if (AccessTools.Method(controller,name)==null) return "求解器接口不兼容："+name;
        var settings=controller.Assembly.GetType("CombatSolver.SolverSettings");
        var data=controller.Assembly.GetType("CombatSolver.SolverSettingsData");
        var overlay=controller.Assembly.GetType("CombatSolver.SolverOverlay");
        var stopped=AccessTools.Method(overlay,"ShowFullAutoStoppedAtDeathTurn", [typeof(int)]);
        captureStamp=AccessTools.Method(controller.Assembly.GetType("CombatSolver.LiveCombatStamp"),"Capture");
        if(settings==null||data?.GetProperty("AutoEnableFullAuto")==null||AccessTools.Method(settings,"Update")==null||stopped==null||captureStamp==null)
            return "求解器自动启动或停止原因接口不兼容。";
        if(!stopHookInstalled)
        {
            var harmony=new Harmony("local.SpireStrategist.solver");
            harmony.Patch(stopped,postfix:new HarmonyMethod(typeof(SolverBridge),nameof(DeathStopped)));
            harmony.Patch(AccessTools.Method(controller,"StopSearchByUser"),prefix:new HarmonyMethod(typeof(SolverBridge),nameof(ClearDeathStop)));
            harmony.Patch(AccessTools.Method(controller,"SetFullAuto"),prefix:new HarmonyMethod(typeof(SolverBridge),nameof(ClearDeathStop)));
            stopHookInstalled=true;
        }
        return null;
    }
    // Observe the solver's exact death-turn stop branch. A timeout, empty search, worse
    // recalculation, live-risk pause or manual stop must never be interpreted as defeat.
    private static void DeathStopped(int __0)
    {
        Entry.Safe(()=>
        {
            ClearDeathStop();
            var combat=CombatManager.Instance.DebugOnlyGetState();
            if(combat==null)return;
            deathStamp=captureStamp!.Invoke(null,[combat]);deathCombat=combat;deathTurn=__0;
        });
    }
    private static void ClearDeathStop(){deathCombat=null;deathStamp=null;}
    internal bool TryDeathStop(CombatState? combat,out int turn)
    {
        turn=deathTurn;
        if(combat==null||!ReferenceEquals(combat,deathCombat)||combat.Players.SingleOrDefault()?.PlayerCombatState?.TurnNumber!=deathTurn
            ||FullAuto||Flag("IsSearching")||Flag("IsDeploying"))return false;
        if(deathStamp==null||!deathStamp.Equals(captureStamp!.Invoke(null,[combat])))
        {ClearDeathStop();return false;}
        return true;
    }
    private object Settings=>AccessTools.Property(controller!.Assembly.GetType("CombatSolver.SolverSettings"),"Current").GetValue(null)!;
    internal bool AutoStart=>(bool)AccessTools.Property(Settings.GetType(),"AutoEnableFullAuto").GetValue(Settings)!;
    private void SetAutoStart(bool value)
    {
        var settings=Settings;
        var copy=AccessTools.Method(typeof(object),"MemberwiseClone").Invoke(settings,null)!;
        AccessTools.Property(copy.GetType(),"AutoEnableFullAuto").SetValue(copy,value);
        AccessTools.Method(controller!.Assembly.GetType("CombatSolver.SolverSettings"),"Update").Invoke(null,[copy]);
    }
    internal void ArmAutoStart()
    {
        previousAutoStart??=AutoStart;
        if(!AutoStart)SetAutoStart(true);
    }
    internal void ReleaseAutoStart()
    {
        if(previousAutoStart is not bool previous)return;
        previousAutoStart=null;
        // Respect a player turning the setting off while the assistant is running.
        if(AutoStart)SetAutoStart(previous);
    }
    internal bool FullAuto => controller?.GetProperty("FullAutoEnabled")?.GetValue(null) is true;
    private bool Flag(string name)=>controller?.GetProperty(name)?.GetValue(null) is true;
    internal bool TryAdoptAfterBudget(int hp,int maxHp,ulong now,out double budget)
    {
        budget=60+180*Math.Pow(1-Math.Clamp(hp/Math.Max(1d,maxHp),0,1),2);
        if(!Flag("IsSearching")||Flag("IsDeploying")){searchingSince=null;adoptionRequested=false;return false;}
        searchingSince??=now;
        if(adoptionRequested||now-searchingSince.Value<budget*1000||Flag("IsStoppingSearch")||Flag("IsAdoptingCurrentRoute")||!Flag("CanAdoptCurrentRoute"))return false;
        // The solver's public UI action validates the current candidate. Never cancel first:
        // doing so would disable full-auto and can discard the very route we want to adopt.
        AccessTools.Method(controller,"AdoptCurrentRoute").Invoke(null,null);
        adoptionRequested=true;return true;
    }
    internal bool CanStart(CombatState state)
    {
        object?[] args=[state,null];
        return AccessTools.Method(controller,"CanSolve").Invoke(null,args) is true;
    }
    internal void Start(CombatState state) {ClearDeathStop();searchingSince=null;adoptionRequested=false;AccessTools.Method(controller,"SetFullAuto").Invoke(null,[NGame.Instance,state,true]);}
    internal void Stop(CombatState state)
    {
        if (controller == null || NGame.Instance == null) return;
        AccessTools.Method(controller,"SetFullAuto").Invoke(null,[NGame.Instance,state,false]);
        AccessTools.Method(controller,"CancelDeployment").Invoke(null,null);
        AccessTools.Method(controller,"StopSearchByUser").Invoke(null,[NGame.Instance]);
    }
}
