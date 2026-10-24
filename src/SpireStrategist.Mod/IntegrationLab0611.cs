using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckFeedback0611(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        var points=state.Map.GetAllMapPoints().ToArray();
        var last=points.Where(n=>n.PointType==MapPointType.RestSite).OrderByDescending(n=>n.coord.row).First();
        state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(last.coord);
        var before=JsonSerializer.Serialize(state.Rng.ToSerializable());
        for(var i=0;i<10;i++){SnapshotReader.Read(state,p);RouteAdapter.Economy(service);}
        Check(before==JsonSerializer.Serialize(state.Rng.ToSerializable()),"commerce and camp forecasts do not consume run RNG");
        Check(SnapshotReader.Read(state,p).Commerce!.ReachableShops==0,"last camp has no reachable shop, earlier shops not counted");
        var e=RouteAdapter.Economy(service);
        Check(e.CampHealing==(double)HealRestSiteOption.GetHealAmount(p)&&e.CanRest&&e.CanSmith,"route camp reads native healing and enabled actions");
        foreach(var potion in p.Potions.ToArray())await PotionCmd.Discard(potion);
        await PotionCmd.TryToProcure(ModelDb.Potion<WeakPotion>().ToMutable(),p);
        await PotionCmd.TryToProcure(ModelDb.Potion<DropletOfPrecognition>().ToMutable(),p);
        var original=p.Potions.ToArray();
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<PotionCourier>(),showTransition:false);
        await Delay(.5);
        var plan=EventAdapter.Read(service,NEventRoom.Instance!)!;
        Check(plan.Buttons.Single(b=>b.GetInstanceId().ToString()==plan.Evaluation.RecommendedKey).Option.TextKey.EndsWith(".RANSACK"),
            "native courier chooses ransack with full combat slots and no reachable shop");
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"courier ransack exits",60);
        Check(p.Potions.SequenceEqual(original),"no-shop courier preserves actual combat potions");
        auto.Pause("courier guard");NMapScreen.Instance!.Close(false);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<PotionCourier>(),showTransition:false);
        await Delay(.5);plan=EventAdapter.Read(service,NEventRoom.Instance!)!;
        // Force the other native option to verify execution guard, independently of scoring.
        var forced=(Task)GameActions.Invoke(plan.Model,"GrabPotions")!;
        auto.Enable(true);await Until(()=>forced.IsCompleted&&Screen() is NMapScreen,"forced foul reward exits without discarding",60);await forced;
        Check(p.Potions.SequenceEqual(original),"reward execution also refuses no-shop Foul replacement");
        auto.Pause("near shop");NMapScreen.Instance!.Close(false);
        var shop=points.First(n=>n.PointType==MapPointType.Shop&&n.parents.Count>0);
        state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(shop.parents.First().coord);
        Check(SnapshotReader.Read(state,p).Commerce is {ReachableShops:>0,NearestShopDistance:1},"actual adjacent shop discovered");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<PotionCourier>(),showTransition:false);
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"near-shop courier finishes",60);
        Check(p.Potions.Count()==2&&p.Potions.All(x=>x is FoulPotion),"nearby shop still permits native economic replacement");
        auto.Pause("smith");NMapScreen.Instance!.Close(false);
        var stoke=state.CreateCard<Stoke>(p);var embrace=state.CreateCard<DarkEmbrace>(p);
        await CardPileCmd.Add(stoke,PileType.Deck);await CardPileCmd.Add(embrace,PileType.Deck);
        async Task Camp(int hp)
        {
            await CreatureCmd.SetCurrentHp(p.Creature,hp);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            auto.Enable(true);await Until(()=>Screen() is NMapScreen,"core upgrade camp exits",60);
            auto.Pause("camp boundary");NMapScreen.Instance!.Close(false);
        }
        await Camp(60);
        Check(embrace.CurrentUpgradeLevel==1&&embrace.EnergyCost.GetWithModifiers(CostModifiers.Local)==1,"first camp upgrades actual Dark Embrace to one energy");
        await Camp(60);
        Check(stoke.CurrentUpgradeLevel==1,"second camp upgrades actual Stoke");
        var hp=9;await Camp(hp);
        Check(p.Creature.CurrentHp>hp,"critical HP still rests after engine upgrades");
    }
}
