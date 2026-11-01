using System.Runtime.Loader;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;
using System.Text.Encodings.Web;

AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var file = Path.Combine(Environment.GetEnvironmentVariable("STS2_GAME_DATA") ?? AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
};
return Lab.Run(args);

static class Lab
{
    public static int Run(string[] args)
    {
        TestMode.IsOn = true;
        var types = typeof(ModelDb).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(AbstractModel)) && !t.IsAbstract && !t.ContainsGenericParameters && t.GetConstructor(Type.EmptyTypes) != null).ToArray();
        typeof(ModelDb).GetField("_allAbstractModelSubtypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, types);
        ModelDb.Init();
        if(args.FirstOrDefault()=="export-tags")
        {
            var factory=typeof(ModelDb).GetMethods().Single(m=>m.Name=="Card"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0);
            foreach(var type in types.Where(t=>t.IsSubclassOf(typeof(CardModel))))
            {
                var card=(CardModel)factory.MakeGenericMethod(type).Invoke(null,null)!;
                var tags=card.Tags.Select(t=>t.ToString()).Where(t=>t is "Strike" or "Defend").ToArray();
                if(tags.Length>0)Console.WriteLine(type.Name+":"+card.Rarity+":"+string.Join(",",tags));
            }
            return 0;
        }
        if(args.FirstOrDefault()=="export-items")
        {
            object[] Export(Type baseType,string factory)=>types.Where(t=>t.IsSubclassOf(baseType)).Select(t=>
            {
                var model=(AbstractModel)typeof(ModelDb).GetMethods().Single(m=>m.Name==factory&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0).MakeGenericMethod(t).Invoke(null,null)!;
                Dictionary<string,decimal> vars=[];string? error=null;
                try{vars=(model is RelicModel r?r.DynamicVars:((PotionModel)model).DynamicVars).ToDictionary(v=>v.Key,v=>v.Value.BaseValue);}
                catch(Exception ex){error=ex.GetType().Name+": native localization required";}
                return (object)new {id=model.Id.ToString(),className=t.Name,rarity=t.GetProperty("Rarity")?.GetValue(model)?.ToString(),
                    pool=t.GetProperty("Pool")?.GetValue(model)?.GetType().Name,variables=vars,exportWarning=error};
            }).ToArray();
            File.WriteAllText("data/native-items-v0.111.0.json",JsonSerializer.Serialize(new{relics=Export(typeof(RelicModel),"Relic"),potions=Export(typeof(PotionModel),"Potion")},new JsonSerializerOptions{WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
            return 0;
        }
        Console.WriteLine("Game models initialized");
        var cards = ModelDb.CardPool<IroncladCardPool>().AllCards.Concat(ModelDb.CardPool<ColorlessCardPool>().AllCards).ToArray();
        Console.WriteLine("Cards: " + cards.Length);
        if (args.FirstOrDefault() is "export" or "export-legacy" or "export-defect" or "export-silent" or "export-regent" or "export-necrobinder")
        {
            var legacy=args[0]=="export-legacy";
            var defect=args[0]=="export-defect";
            var silent=args[0]=="export-silent";
            var regent=args[0]=="export-regent";var necro=args[0]=="export-necrobinder";
            if(regent)cards=ModelDb.CardPool<RegentCardPool>().AllCards.ToArray();
            if(necro)cards=ModelDb.CardPool<NecrobinderCardPool>().AllCards.ToArray();
            if(silent)cards=ModelDb.CardPool<SilentCardPool>().AllCards.ToArray();
            if(defect)cards=ModelDb.CardPool<DefectCardPool>().AllCards.ToArray();
            if(legacy)cards=(CardModel[])typeof(MegaCrit.Sts2.Core.Models.Events.TrashHeap).GetProperty("Cards",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            var rows = cards.Select(c =>
            {
                var mutable = c.ToMutable();
                var basic = Snapshot(mutable);
                mutable.UpgradeInternal();
                mutable.FinalizeUpgradeInternal();
                return new { pool = legacy?"EventLegacy":c.Pool.GetType().Name, multiplayerConstraint=c.MultiplayerConstraint.ToString(), basic, upgraded = Snapshot(mutable) };
            }).ToArray();
            Directory.CreateDirectory("data");
            File.WriteAllText(regent?"data/regent-cards-v0.111.0.json":necro?"data/necrobinder-cards-v0.111.0.json":silent?"data/silent-cards-v0.111.0.json":defect?"data/defect-cards-v0.111.0.json":legacy?"data/event-cards-v0.111.0.json":"data/cards-v0.111.0.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine("Exported base and upgrade data for " + rows.Length + " cards");
            return 0;
        }
        Console.Error.WriteLine("Use 'export'. Full combat requires the isolated native host: scripts/run-combat-batch.ps1.");
        return 2;
    }
    static CardSnapshot Snapshot(CardModel c) => new()
    {
        Id = c.Id.ToString(), ClassName = c.GetType().Name, Name = c.GetType().Name,
        Type = c.Type.ToString(), Rarity = c.Rarity.ToString(), Cost = c.EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.Local),
        CostsX = c.EnergyCost.CostsX || c.HasStarCostX, StarCost=c.CurrentStarCost, CostsStarX=c.HasStarCostX,
        CanUpgrade = c.IsUpgradable, UpgradeLevel = c.CurrentUpgradeLevel,
        Keywords = c.Keywords.Select(k => k.ToString()).ToArray(), Variables = c.DynamicVars.ToDictionary(v => v.Key, v => v.Value.BaseValue)
    };
}
