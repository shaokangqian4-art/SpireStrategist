using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Entities.RestSite;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static class RouteAdapter
{
    internal static string Key(MapPoint p)=>$"{p.coord.col},{p.coord.row}";
    internal static NMapPoint[] Legal(NMapScreen screen)=>!screen.IsOpen || !screen.IsTravelEnabled || screen.IsTraveling || screen.IsDebugTravelEnabled ? [] :
        GameActions.Descendants<NMapPoint>(screen).Where(p=>p.IsEnabled && p.State.ToString()=="Travelable" && GameActions.Invoke(p,"IsInputAllowed") is true).ToArray();
    internal static RouteEvaluation Evaluate(AdvisorService service,NMapPoint[] legal)
    {
        var run=service.State;
        var state=SnapshotReader.Read(run,service.Player);
        if(state.Map?.Error is {} error)return new(null,0,0,[],[error]);
        try{return RoutePolicy.Evaluate(state,Nodes(run),legal.Select(p=>Key(p.Point)).ToArray(),Economy(service));}
        catch(Exception ex){return new(null,0,0,[],["地图兼容读取失败："+ex.GetBaseException().Message]);}
    }
    internal static RouteEvaluation Preview(AdvisorService service,NMapScreen screen)
    {
        var legal=Legal(screen);
        if(legal.Length>0)return Evaluate(service,legal);
        var state=SnapshotReader.Read(service.State,service.Player);
        if(state.Map?.Error is {} error)return new(null,0,0,[],[error]);
        try
        {
            var nodes=Nodes(service.State);var economy=Economy(service);
            var current=service.State.CurrentMapPoint??service.State.Map.StartingMapPoint;
            var roots=nodes.FirstOrDefault(n=>n.Id==Key(current))?.Children??[];
            return RoutePolicy.Evaluate(state,nodes,roots.Concat(economy.FlightRoots).Distinct().ToArray(),economy);
        }
        catch(Exception ex){return new(null,0,0,[],["路线预览暂不可用："+ex.GetBaseException().Message]);}
    }
    internal static RouteNode[] Nodes(MegaCrit.Sts2.Core.Runs.IRunState run)
    {
        var map=run.Map;
        var points=map.GetAllMapPoints().Append(map.StartingMapPoint).Append(map.BossMapPoint)
            .Concat(map.SecondBossMapPoint is {} second?new[]{second}:[]).DistinctBy(Key).ToArray();
        return points.Select(p=>new RouteNode(Key(p),LongMapAdapter.IsMiniBoss(run,p)?"MiniBoss":p.PointType.ToString(),
            p.Children.Count>0 ? p.Children.Select(Key).Distinct().ToArray() :
            p==map.BossMapPoint && map.SecondBossMapPoint!=null ? [Key(map.SecondBossMapPoint)] :
            p!=map.BossMapPoint && p!=map.SecondBossMapPoint && p.coord.row==map.GetRowCount()-1 ? [Key(map.BossMapPoint)] : [],p.coord.row)).ToArray();
    }
    internal static RouteEconomy Economy(AdvisorService service)
    {
        var player=service.Player;var notes=new List<string>();
        RelicModel? Find(string name)=>player.Relics.FirstOrDefault(r=>r.GetType().Name==name && !r.IsUsedUp);
        double Var(string name,string key,double fallback=0)
        {
            var relic=Find(name);return relic==null?fallback:(double)relic.DynamicVars[key].BaseValue;
        }
        var mul=Find("Ectoplasm")!=null?0:Var("BowlerHat","GoldIncrease",1);
        var bank=Var("MawBank","Gold");var combat=Var("AmethystAubergine","Gold");
        var card=Var("LuckyFysh","Gold");var loss=Var("SealOfGold","Gold");
        if(mul==0)notes.Add("异化之物：新增金币按 0 计算。");
        if(bank>0)notes.Add($"巨口储蓄罐尚未失效：每到一房间（含商店）+{bank} 金币。");
        if(combat>0)notes.Add($"紫水晶茄子：每场战斗额外 +{combat} 金币。");
        if(card>0)notes.Add($"幸运鱼：每次加牌 +{card}；前瞻按半数战斗拿牌估算，范围含跳过。");
        if(loss>0)notes.Add($"黄金印章：每回合扣 {loss} 金币；普通战估 4 回合，精英估 6 回合。");
        if(mul!=1 && mul>0)notes.Add($"现有金币增益倍率 ×{mul:F2}。");
        var discount=Var("MembershipCard","Discount",100)/100*(1-Var("TheCourier","Discount")/100);
        if(discount<1)notes.Add($"商店购买力按现有折扣 ×{discount:F2} 修正。");
        // A low-weight prior, not a peek/pull from the relic bag. Do not consume any RNG.
        var bag=player.RelicGrabBag.ToSerializable();
        var oldCoin=ModelDb.Relic<OldCoin>();
        var eligible=bag.RelicIdLists.TryGetValue(RelicRarity.Rare,out var rare) && rare.Contains(oldCoin.Id);
        var possible=eligible ? .1*(double)oldCoin.DynamicVars.Gold.BaseValue/Math.Max(1,rare!.Count):0;
        if(possible>0)notes.Add($"沿途精英/宝箱取得旧钱币的先验收益约 {possible:F1}/次；仅 20% 纳入路线分，不当作到店现款。稀有遗物概率暂按 10% 估计。");
        var normal=service.State.Act.AllEncounters.Where(e=>e.RoomType==RoomType.Monster).ToArray();
        var elite=service.State.Act.AllEliteEncounters.ToArray();
        var bosses=service.State.Act.AllBossEncounters.ToArray();
        var layout=LongMapAdapter.Read(service.State);
        var rooms=service.State.Act.ToSave().SerializableRooms;
        var normalIds=rooms.NormalEncounterIds??[];
        var regular=service.State.Act.AllRegularEncounters.ToArray();
        var upcoming=normalIds.Count==0?[]:Enumerable.Range(rooms.NormalEncountersVisited,13).Select(index=>
        {
            var encounter=ModelDb.GetById<EncounterModel>(normalIds[index%normalIds.Count]);
            // Native lists wrap; installed YUILongMap replaces wrapped weak encounters with regular ones.
            if(layout.LongMap&&index>=normalIds.Count&&encounter.IsWeak&&regular.Length>0)
                encounter=regular[index%regular.Length];
            return new NormalEncounterForecast(encounter.IsWeak,encounter.MinGoldReward,encounter.MaxGoldReward);
        }).ToArray();
        var unknown=service.State.Odds.UnknownMapPoint;
        var roomTypes=new[]{RoomType.Monster,RoomType.Elite,RoomType.Treasure,RoomType.Shop};
        var allowed=Hook.ModifyUnknownMapPointRoomTypes(service.State,roomTypes.Append(RoomType.Event).ToHashSet());
        var bases=(Dictionary<RoomType,float>)GameActions.Field(unknown,"_baseOdds")!;
        var boots=Find("WingedBoots") as WingedBoots;
        var current=service.State.CurrentMapPoint;
        var flights=boots!=null&&current!=null?service.State.Map.GetAllMapPoints()
            .Where(p=>p.coord.row==current.coord.row+1&&!current.Children.Contains(p)).Select(Key).ToArray():[];
        notes.Add($"问号普通战概率字段 {(allowed.Contains(RoomType.Monster)?Math.Clamp(unknown.MonsterOdds,0,1):0):P0}；新手局强制规则另计。按原生允许类型与概率递增估计，后续概率为期望近似，非确定预言。");
        notes.Add($"已消耗普通遭遇 {rooms.NormalEncountersVisited} 个（包括问号小怪）；下一场{(upcoming.FirstOrDefault()?.Weak==true?"弱怪":"强怪")}，采用实际生成序列，不硬编码前三层。");
        notes.Add("后置商店按本幕真实地图进度增加权重，增益随到店购买力提高；兼容额外层数。");
        if(layout.LongMap)notes.Add($"更长地图：实际 {layout.Rows} 行，共 {layout.TotalActs} 幕；骷髅房按小首领识别。敌人生命 ×{layout.EnemyHpScale:0.##}、伤害 ×{layout.EnemyDamageScale:0.##}、战斗金币 ×{layout.CombatGoldScale:0.##}；战损仍为保守估算。");
        notes.Add("金币区间采用本幕遭遇模型奖励范围；问号可能付钱/给钱，尚未预测具体事件结果。拿到新遗物后立即重算。");
        var restOptions=new List<RestSiteOption>{new HealRestSiteOption(player),new SmithRestSiteOption(player)};
        Hook.ModifyRestSiteOptions(service.State,player,restOptions);
        return new(){GoldMultiplier=mul,GoldPerRoom=bank,GoldPerCombat=combat,GoldPerCard=card,TurnGoldCost=loss,
            PriceMultiplier=discount,ShopHealing=Var("MealTicket","Heal"),PossibleRelicGold=possible,
            CampHealing=(double)HealRestSiteOption.GetHealAmount(player),
            CanRest=restOptions.Any(o=>o is HealRestSiteOption&&o.IsEnabled),
            CanSmith=restOptions.Any(o=>o is SmithRestSiteOption&&o.IsEnabled),
            NormalGoldMin=normal.Length==0?10:normal.Min(e=>e.MinGoldReward),NormalGoldMax=normal.Length==0?20:normal.Max(e=>e.MaxGoldReward),
            EliteGoldMin=elite.Length==0?25:elite.Min(e=>e.MinGoldReward),EliteGoldMax=elite.Length==0?35:elite.Max(e=>e.MaxGoldReward),
            BossGoldMin=bosses.Length==0?100:bosses.Min(e=>e.MinGoldReward),BossGoldMax=bosses.Length==0?100:bosses.Max(e=>e.MaxGoldReward),
            CombatRewardMultiplier=layout.CombatGoldScale,CombatRiskMultiplier=layout.EnemyHpScale*layout.EnemyDamageScale,
            NormalEncounters=upcoming,UnknownOdds=[unknown.MonsterOdds,unknown.EliteOdds,unknown.TreasureOdds,unknown.ShopOdds],
            UnknownBaseOdds=roomTypes.Select(t=>(double)bases[t]).ToArray(),
            UnknownIncrements=roomTypes.Select(t=>(double)Hook.ModifyOddsIncreaseForUnrolledRoomType(service.State,t,bases[t])).ToArray(),
            UnknownAllowed=roomTypes.Select(allowed.Contains).ToArray(),UnknownAllowsEvent=allowed.Contains(RoomType.Event),
            TutorialUnknownsVisited=service.State.UnlockState.NumberOfRuns==0?service.State.MapPointHistory.SelectMany(h=>h).Count(h=>h.MapPointType==MapPointType.Unknown):-1,
            FlightRoots=flights,FlightCharges=boots?.DisplayAmount??0,PreviousWasShop=service.State.CurrentRoom is MerchantRoom,
            Notes=notes.ToArray()};
    }
}
