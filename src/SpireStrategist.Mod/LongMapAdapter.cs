using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

// Optional integration, inspected against the installed YUILongMap 1.1.33.
// Uses its current-run settings and persisted skull registry; never pulls encounters.
internal static class LongMapAdapter
{
    private static Assembly? Assembly=>AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="YUILongMap");
    internal static MapContext Read(IRunState run)
    {
        var map=run.Map;
        var result=new MapContext(run.Acts.Count,map?.GetRowCount()??0,run.CurrentMapCoord?.row??-1,
            map?.BossMapPoint.coord.row??-1,map?.GetType().FullName??"pending",ContentAct:run.CurrentActIndex+1);
        var assembly=Assembly;if(assembly==null)return result;
        try
        {
            var settings=AccessTools.Method(assembly.GetType("YUILongMap.Settings.YuiLongMapSettings"),"GetEffectiveSettings")!.Invoke(null,[run])!;
            int Read(string key)=>(int)settings.GetType().GetProperty(key)!.GetValue(settings)!;
            object?[] scales=[run,null,null,null];
            AccessTools.Method(assembly.GetType("YUILongMap.ExtraActCompat"),"GetScales")!.Invoke(null,scales);
            return result with{LongMap=true,ExtraLayers=Read("ExtraLayerCount"),ExtraActs=Read("ExtraActs"),
                ContentAct=run.CurrentActIndex%3+1,EnemyHpScale=Convert.ToDouble(scales[1]),
                EnemyDamageScale=Convert.ToDouble(scales[2]),CombatGoldScale=Convert.ToDouble(scales[3])};
        }
        catch(Exception ex){return result with{LongMap=true,Error="YUILongMap 兼容读取失败："+ex.GetBaseException().Message};}
    }
    internal static bool IsMiniBoss(IRunState run,MapPoint point)
    {
        var assembly=Assembly;if(assembly==null)return false;
        return (bool)AccessTools.Method(assembly.GetType("YUILongMap.MiniBossRegistry"),"IsMiniBoss")!.Invoke(null,[run,point])!;
    }
}
