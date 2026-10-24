using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static class SnapshotReader
{
    internal static RunSnapshot Read(IRunState run, Player player) => new()
    {
        Character = player.Character.GetType().Name,
        PlayerCount = run.Players.Count,
        Hp = player.Creature.CurrentHp,
        MaxHp = player.Creature.MaxHp,
        Gold = player.Gold,
        Energy = player.MaxEnergy,
        CombatSolverAvailable = SolverBridge.AvailableForEvaluation,
        Act = run.CurrentActIndex + 1,
        Floor = run.TotalFloor,
        Ascension = run.AscensionLevel,
        Room = run.CurrentRoom?.GetType().Name ?? "Transition",
        Deck = player.Deck.Cards.Select(ReadCard).ToArray(),
        Relics = player.Relics.Select(r => r.Id.ToString()).ToArray(),
        PotionSlots = player.PotionSlots.Select(p => p?.Id.ToString()).ToArray(),
        NextNodeTypes = run.CurrentMapPoint?.Children.Select(p => p.PointType.ToString())
            .Order(StringComparer.Ordinal).ToArray() ?? [],
        Hidden = HiddenReader.Read(run, player),
        Map = LongMapAdapter.Read(run),
        Commerce = ReadCommerce(run,player),
        RecentBattles = run.MapPointHistory.ElementAtOrDefault(run.CurrentActIndex)?.TakeLast(9)
            .Where(h=>!ReferenceEquals(h,run.CurrentMapPointHistoryEntry) && h.Rooms.Count==1 && h.Rooms[0].RoomType is MegaCrit.Sts2.Core.Rooms.RoomType.Elite or MegaCrit.Sts2.Core.Rooms.RoomType.Boss)
            .Select(h=>(Room:h.Rooms[0],Stats:h.GetEntry(player.NetId)))
            .Where(x=>x.Stats.MaxHp>0&&x.Stats.CurrentHp>0).TakeLast(3)
            .Select(x=>new CombatSummary(run.CurrentActIndex+1,x.Room.RoomType.ToString(),x.Room.ModelId?.ToString(),x.Stats.DamageTaken,x.Stats.HpHealed,x.Stats.PotionUsed.Count,x.Stats.MaxHp)).ToArray()??[]
    };

    internal static CommerceContext? ReadCommerce(IRunState run,Player player)
    {
        if(run.CurrentMapPoint is not {} current)return null;
        var queue=new Queue<(MegaCrit.Sts2.Core.Map.MapPoint Point,int Distance)>();
        foreach(var child in current.Children)queue.Enqueue((child,1));
        // A remaining Winged Boots charge can reach another branch at a later row.
        if(player.Relics.Any(r=>r.GetType().Name=="WingedBoots"&&!r.IsUsedUp))
            foreach(var p in run.Map.GetAllMapPoints().Where(p=>p.coord.row>current.coord.row))
                queue.Enqueue((p,Math.Max(1,p.coord.row-current.coord.row)));
        var seen=new HashSet<string>();var shops=0;int? nearest=null;var unknowns=0;
        while(queue.TryDequeue(out var item))
        {
            if(!seen.Add(RouteAdapter.Key(item.Point)))continue;
            if(item.Point.PointType.ToString()=="Shop"){shops++;nearest=Math.Min(nearest??int.MaxValue,item.Distance);}
            if(item.Point.PointType.ToString()=="Unknown")unknowns++;
            foreach(var child in item.Point.Children)queue.Enqueue((child,item.Distance+1));
        }
        var map=LongMapAdapter.Read(run);
        return new(shops,nearest,unknowns,Math.Max(0,map.TotalActs-(run.CurrentActIndex+1)));
    }

    internal static CardSnapshot ReadCard(CardModel c) => new()
    {
        Id = c.Id.ToString(),
        ClassName = c.GetType().Assembly == typeof(CardModel).Assembly ? c.GetType().Name : c.GetType().FullName ?? "ModCard",
        Name = c.Title,
        Type = c.Type.ToString(),
        Rarity = c.Rarity.ToString(),
        Description = c.GetDescriptionForPile(PileType.Deck),
        Cost = c.EnergyCost.GetWithModifiers(CostModifiers.Local),
        CostsX = c.EnergyCost.CostsX || c.HasStarCostX,
        UpgradeLevel = c.CurrentUpgradeLevel,
        CanUpgrade = c.IsUpgradable,
        Modified = c.Enchantment != null || c.Affliction != null,
        Enchantment = c.Enchantment?.Id.ToString(),
        EnchantmentAmount = c.Enchantment?.Amount ?? 0,
        Affliction = c.Affliction?.Id.ToString(),
        Keywords = c.Keywords.Select(k => k.ToString()).Order(StringComparer.Ordinal).ToArray(),
        Variables = c.DynamicVars.OrderBy(v => v.Key, StringComparer.Ordinal)
            .ToDictionary(v => v.Key, v => v.Value.BaseValue)
    };
}
