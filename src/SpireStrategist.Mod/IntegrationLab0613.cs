using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckFeedback0613(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);
        await Delay(.5);
        var model=(Neow)GameActions.Field(NEventRoom.Instance!,"_event")!;
        var snapshot=SnapshotReader.Read(state,p);
        string RngState()=>JsonSerializer.Serialize(HiddenReader.Read(state,p).Streams);
        var before=RngState();
        var original=p.Deck.Cards.OfType<StrikeIronclad>().First();
        var single=TransformForecast.ReadNewLeaf(original,snapshot);
        var pair=TransformForecast.Leafy(model);
        for(var i=0;i<10;i++)
        {
            TransformForecast.ReadNewLeaf(original,snapshot);TransformForecast.Leafy(model);
            foreach(var option in model.AllPossibleOptions)EventAdapter.Describe(model,option,snapshot,new Dictionary<string,double>());
        }
        Check(RngState()==before,"all Neow option assessments and transform previews preserve every RNG stream");
        var descriptions=model.AllPossibleOptions.ToDictionary(o=>o.Relic!.Id.Entry,
            o=>EventAdapter.Describe(model,o,snapshot,new Dictionary<string,double>()));
        Check(descriptions["PRECISE_SCISSORS"].Benefit>descriptions["NUTRITIOUS_OYSTER"].Benefit,
            "native Neow adapter uses user priority");
        Check(!descriptions["KALEIDOSCOPE"].Enabled&&!descriptions["MASSIVE_SCROLL"].Enabled,
            "unsupported Neow rewards are excluded from native automation");
        Check(descriptions["NEW_LEAF"].Notes!.Any(x=>x.Contains(single.Result.Name))&&
            descriptions["LEAFY_POULTICE"].Notes!.Any(x=>pair.All(c=>x.Contains(c.Name))),
            "native event descriptions expose actual transformation predictions");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        // Leafy performs the exact native two-card transform, including max HP cost.
        var old=p.Deck.Cards.ToArray();var max=p.Creature.MaxHp;
        await RelicCmd.Obtain(ModelDb.Relic<LeafyPoultice>().ToMutable(),p);
        var added=p.Deck.Cards.Except(old).Select(SnapshotReader.ReadCard).ToArray();
        Check(added.Select(c=>c.Id).SequenceEqual(pair.Select(c=>c.Id))&&p.Creature.MaxHp==max-12,
            "Leafy predicted pair matches native strike-then-defend results and 12 max HP loss");
        // New Leaf uses a different RNG and a native selectable transform.
        snapshot=SnapshotReader.Read(state,p);
        var expected=p.Deck.Cards.Where(c=>c.IsTransformable).Select(c=>TransformForecast.ReadNewLeaf(c,snapshot))
            .OrderByDescending(x=>x.Value).First();
        old=p.Deck.Cards.ToArray();
        var leaf=ModelDb.Relic<NewLeaf>().ToMutable();
        auto.Enable(true);GameActions.Invoke(auto,"PrepareRelicSelection",leaf);
        var task=RelicCmd.Obtain(leaf,p);
        await Until(()=>task.IsCompleted,"New Leaf native selector completes",60);await task;auto.Pause("fixture boundary");
        Check(!p.Deck.Cards.Contains(expected.Original)&&p.Deck.Cards.Except(old).Single().Id.ToString()==expected.Result.Id,
            "New Leaf uses predicted target and produces predicted card through native selector");
        // Ensure actual native selector chooses one of each, not two strikes.
        // Native damage needs an event room's screen-shake target; a bare map has none.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Wellspring>(),showTransition:false);
        await Delay(.5);
        old=p.Deck.Cards.ToArray();var hp=p.Creature.CurrentHp;
        var shears=ModelDb.Relic<PrecariousShears>().ToMutable();
        auto.Enable(true);GameActions.Invoke(auto,"PrepareRelicSelection",shears);
        AccessTools.Field(typeof(PartialAutopilot),"eventSelection").SetValue(auto,"neow-remove-pair");
        task=RelicCmd.Obtain(shears,p);
        await Until(()=>task.IsCompleted,"Neow shears native selector completes",60);await task;auto.Pause("fixture done");
        var removed=old.Except(p.Deck.Cards).ToArray();
        Check(removed.Length==2&&removed.Any(c=>c is StrikeIronclad)&&removed.Any(c=>c is DefendIronclad)&&p.Creature.CurrentHp==hp-16,
            "native shears remove one strike and one defend and pay actual damage");
    }
}
