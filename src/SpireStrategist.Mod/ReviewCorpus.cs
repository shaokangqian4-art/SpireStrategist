using Godot;
using MegaCrit.Sts2.Core.Models;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace SpireStrategist.Mod;

// Isolated lab only: no event execution and no run RNG consumption.
internal static class ReviewCorpus
{
    internal static void Export()
    {
        var output=Path.Combine(OS.GetUserDataDir(),"review-corpus");
        Directory.CreateDirectory(output);
        foreach(var name in new[]{"events","ancients","relics","cards","potions","enchantments"})
        {
            var path=$"res://localization/zhs/{name}.json";
            if(Godot.FileAccess.FileExists(path)) File.WriteAllText(Path.Combine(output,name+".json"),Godot.FileAccess.GetFileAsString(path));
        }
        var opts=new JsonSerializerOptions{WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
        var all=typeof(ModelDb).Assembly.GetTypes().Where(t=>!t.IsAbstract && t.IsSubclassOf(typeof(EventModel)) && t.Namespace=="MegaCrit.Sts2.Core.Models.Events");
        var events=new List<object>();
        foreach(var t in all)
        {
            var e=ModelDb.GetById<EventModel>(ModelDb.GetId(t));
            var ancient=e is AncientEventModel;
            string[] choices=[];string? error=null;
            try {if(e is AncientEventModel a) choices=a.AllPossibleOptions.Select(o=>o.TextKey).Distinct().ToArray();}
            catch(Exception ex){error=ex.GetBaseException().Message;}
            events.Add(new{type=t.Name,id=e.Id.Entry,ancient,active=ancient?ModelDb.AllAncients.Any(a=>a.Id==e.Id):ModelDb.AllEvents.Any(a=>a.Id==e.Id),vars=e.DynamicVars.ToDictionary(v=>v.Key,v=>v.Value.BaseValue),choices,error});
        }
        File.WriteAllText(Path.Combine(output,"models.json"),JsonSerializer.Serialize(events,opts));
        var relics=typeof(ModelDb).Assembly.GetTypes().Where(t=>!t.IsAbstract && t.IsSubclassOf(typeof(RelicModel)) && t.Namespace=="MegaCrit.Sts2.Core.Models.Relics")
            .Select(t=>ModelDb.GetById<RelicModel>(ModelDb.GetId(t))).Select(r=>new{type=r.GetType().Name,id=r.Id.Entry,rarity=r.Rarity.ToString(),vars=r.DynamicVars.ToDictionary(v=>v.Key,v=>v.Value.BaseValue)}).ToArray();
        File.WriteAllText(Path.Combine(output,"relic-models.json"),JsonSerializer.Serialize(relics,opts));
        GD.Print("[StrategistLab] Review corpus exported: "+output);
    }
}
