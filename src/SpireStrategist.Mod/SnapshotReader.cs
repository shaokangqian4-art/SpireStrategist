using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static class SnapshotReader
{
    // Network role matters even if only one player remains in a co-op session.
    internal static bool IsMultiplayer(IRunState run)=>IsMultiplayer(run.Players.Count,RunManager.Instance.NetService?.Type);
    internal static bool IsMultiplayer(int playerCount,MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType? networkType)=>playerCount>1||
        networkType is MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host
            or MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client;
    internal static int? PendingAncientHp(IRunState run,Player player)
    {
        // Called only for a visible card reward. Never invoke a live heal/RNG.
        if(player.Creature.CurrentHp<=0||MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress
            ||run.CurrentRoom?.RoomType!=MegaCrit.Sts2.Core.Rooms.RoomType.Boss
            ||run.CurrentActIndex+1>=run.Acts.Count)return null;
        // On double-boss acts the first boss is not a safe recovery boundary.
        if(run.Map.SecondBossMapPoint!=null&&run.CurrentMapCoord==run.Map.BossMapPoint.coord)return null;
        var id=run.Acts[run.CurrentActIndex+1].ToSave().SerializableRooms.AncientId;
        if(id is not {} ancientId)return null;
        var ancient=ModelDb.GetById<AncientEventModel>(ancientId);
        if(ancient.GetType().Assembly!=typeof(AncientEventModel).Assembly)return null;
        var fraction=run.AscensionLevel>=(int)MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.WearyTraveler?.8:1;
        return player.Creature.CurrentHp+(int)Math.Floor((player.Creature.MaxHp-player.Creature.CurrentHp)*fraction);
    }

    internal static RunSnapshot Read(IRunState run, Player player) => new()
    {
        Character = player.Character.GetType().Name,
        PlayerCount = run.Players.Count,
        MultiplayerSession = IsMultiplayer(run),
        Hp = player.Creature.CurrentHp,
        MaxHp = player.Creature.MaxHp,
        Gold = player.Gold,
        Energy = player.MaxEnergy,
        CombatSolverAvailable = !IsMultiplayer(run)&&SolverBridge.AvailableForEvaluation,
        Act = run.CurrentActIndex + 1,
        Floor = run.TotalFloor,
        Ascension = run.AscensionLevel,
        Room = run.CurrentRoom?.GetType().Name ?? "Transition",
        Deck = player.Deck.Cards.Select(ReadCard).ToArray(),
        PendingToothCards = player.GetRelic<MegaCrit.Sts2.Core.Models.Relics.PaelsTooth>()?.SerializableCards
            .Select(c=>ReadCard(CardModel.FromSerializable(c))).ToArray() ?? [],
        Relics = player.Relics.Select(r => r.Id.ToString()).ToArray(),
        GiryaLifts = player.GetRelic<MegaCrit.Sts2.Core.Models.Relics.Girya>()?.TimesLifted,
        PotionSlots = player.PotionSlots.Select(p => p?.Id.ToString()).ToArray(),
        NextNodeTypes = run.CurrentMapPoint?.Children.Select(p => p.PointType.ToString())
            .Order(StringComparer.Ordinal).ToArray() ?? [],
        Hidden = HiddenReader.Read(run, player),
        Map = LongMapAdapter.Read(run),
        Commerce = ReadCommerce(run,player),
        RecentBattles = ReadRecentBattles(run.MapPointHistory.ElementAtOrDefault(run.CurrentActIndex)??[],
            run.CurrentMapPointHistoryEntry,player.NetId,run.CurrentActIndex+1,CompletedMapCombat(run,player))
    };

    private static CombatSummary? CompletedMapCombat(IRunState run,Player player)
    {
        // Native history seals CurrentHp/MaxHp only when the next node is entered.
        // At a travel-enabled post-combat map use live HP, without editing history.
        // Merely opening the map mid-combat is not evidence that combat has ended.
        if(player.Creature.CurrentHp<=0 || MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress ||
            run.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.CombatRoom ||
            MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance is not {IsOpen:true,IsTravelEnabled:true,IsTraveling:false} ||
            run.CurrentMapPointHistoryEntry is not {} h || h.Rooms.Count!=1)return null;
        var room=h.Rooms[0];
        if(room.RoomType is not (MegaCrit.Sts2.Core.Rooms.RoomType.Monster or MegaCrit.Sts2.Core.Rooms.RoomType.Elite or MegaCrit.Sts2.Core.Rooms.RoomType.Boss))return null;
        var stats=h.GetEntry(player.NetId);
        return new(run.CurrentActIndex+1,room.RoomType.ToString(),room.ModelId?.ToString(),
            stats.DamageTaken,stats.HpHealed,stats.PotionUsed.Count,player.Creature.MaxHp);
    }

    internal static CombatSummary[] ReadRecentBattles(
        IEnumerable<MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry> history,
        MegaCrit.Sts2.Core.Runs.History.MapPointHistoryEntry? current,ulong playerId,int act,CombatSummary? completedCurrent=null)
        => history.Where(h=>!ReferenceEquals(h,current) && h.Rooms.Count==1 &&
                h.Rooms[0].RoomType is MegaCrit.Sts2.Core.Rooms.RoomType.Monster or MegaCrit.Sts2.Core.Rooms.RoomType.Elite or MegaCrit.Sts2.Core.Rooms.RoomType.Boss)
            .Select(h=>(Room:h.Rooms[0],Stats:h.GetEntry(playerId)))
            // Three per kind; normal fights must not evict the elite/boss samples
            // used by camp decisions. Current/incomplete rooms remain excluded.
            .Where(x=>x.Stats.MaxHp>0&&x.Stats.CurrentHp>0)
            .Select(x=>new CombatSummary(act,x.Room.RoomType.ToString(),x.Room.ModelId?.ToString(),
                x.Stats.DamageTaken,x.Stats.HpHealed,x.Stats.PotionUsed.Count,x.Stats.MaxHp))
            .Concat(completedCurrent is {} last?new[]{last}:[])
            .Select((battle,index)=>(battle,index))
            .GroupBy(x=>x.battle.Room).SelectMany(g=>g.TakeLast(3)).OrderBy(x=>x.index)
            .Select(x=>x.battle).ToArray();

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
        StarCost = c.CurrentStarCost, CostsStarX = c.HasStarCostX,
        UpgradeLevel = c.CurrentUpgradeLevel,
        CanUpgrade = c.IsUpgradable,
        Modified = c.Enchantment != null || c.Affliction != null,
          MultiplayerOnly = c.MultiplayerConstraint==CardMultiplayerConstraint.MultiplayerOnly,
        Enchantment = c.Enchantment?.Id.ToString(),
        EnchantmentAmount = c.Enchantment?.Amount ?? 0,
        Affliction = c.Affliction?.Id.ToString(),
        Keywords = c.Keywords.Select(k => k.ToString()).Order(StringComparer.Ordinal).ToArray(),
        Variables = c.DynamicVars.OrderBy(v => v.Key, StringComparer.Ordinal)
            .ToDictionary(v => v.Key, v => v.Value.BaseValue)
    };
}
