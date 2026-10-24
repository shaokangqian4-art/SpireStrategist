using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckCamp0617(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        var points=state.Map.GetAllMapPoints().OrderBy(n=>n.coord.row).ToArray();
        var camp=points.First(n=>n.coord.row==2);
        var first=points.First(n=>n.coord.row==3);var second=points.First(n=>n.coord.row==4);
        camp.PointType=MapPointType.RestSite;first.PointType=second.PointType=MapPointType.Elite;
        foreach(var point in new[]{camp,first,second})foreach(var child in point.Children.ToArray())point.RemoveChildPoint(child);
        camp.AddChildPoint(first);first.AddChildPoint(second);
        state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(camp.coord);
        var uppercut=state.CreateCard<Uppercut>(p);await CardPileCmd.Add(uppercut,PileType.Deck);
        await CreatureCmd.SetCurrentHp(p.Creature,40);
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>service.Decision?.Kind=="rest"&&service.Decision.State.CampRouteRisk.Count==2,"camp forward context captured");
        var d=service.Decision!;
        var plain=new Policy().Evaluate(d.State with{CampRouteRisk=[]},d.Options);
        Check(d.Options.Single(o=>o.Key==plain.RecommendedKey).Kind=="smith","without future danger local score would smith");
        Check(d.Options.Single(o=>o.Key==d.Evaluation.RecommendedKey).Kind=="heal","native camp plans recovery before forced elite chain");
        var rng=JsonSerializer.Serialize(state.Rng.ToSerializable());
        for(var i=0;i<3;i++)CampRoutePolicy.Risks(SnapshotReader.Read(state,p),d.Options,RouteAdapter.Nodes(state),RouteAdapter.Key(camp),RouteAdapter.Economy(service));
        Check(JsonSerializer.Serialize(state.Rng.ToSerializable())==rng,"camp projections do not consume game RNG");
        var upgrades=p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"risk-aware rest completes",60);
        auto.Pause("camp fixture boundary");NMapScreen.Instance!.Close(false);
        Check(p.Creature.CurrentHp==64&&p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)==upgrades,"native heal exactly once, no accidental smith");
        await CreatureCmd.SetCurrentHp(p.Creature,80);
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>service.Decision?.Kind=="rest"&&service.Decision.State.Hp==80,"healthy camp context");
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"healthy smith completes",60);
        auto.Pause("camp fixture complete");
        Check(uppercut.CurrentUpgradeLevel==1,"healthy camp upgrades meaningful card before Bash");
        Check(p.Deck.Cards.Single(c=>c is Bash).CurrentUpgradeLevel==0,"Bash is not the default first upgrade");
    }
}
