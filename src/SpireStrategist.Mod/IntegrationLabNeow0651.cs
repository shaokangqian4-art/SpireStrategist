using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckNeow0651(RunState state,AdvisorService service)
    {
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);
        await Delay(.5);
        var model=(Neow)GameActions.Field(NEventRoom.Instance!,"_event")!;
        var s=SnapshotReader.Read(state,service.Player);
        var before=RewardForesightReader.Fingerprint(state,service.Player);
        var eventBefore=model.Rng.ToSerializable();
        var choices=model.AllPossibleOptions.ToDictionary(o=>o.Relic!.Id.Entry,
            o=>EventAdapter.Describe(model,o,s,new Dictionary<string,double>()));
        Check(choices["LAVA_ROCK"].Benefit==1,"native Neow lava uses shared low tier: "+s.Character);
        Check(choices["PRECISE_SCISSORS"].Benefit==32&&choices["SILVER_CRUCIBLE"].Benefit==28,
            "native Neow preserves user removal and second tier: "+s.Character);
        var predicted=RewardForesightReader.GenerateArcaneScroll(RewardForesightReader.Clone(state).Players.Single())!;
        Check(choices["ARCANE_SCROLL"].Benefit==NeowPolicy.ScrollScore(s,predicted)&&
            choices["ARCANE_SCROLL"].Notes!.Any(n=>n.Contains(predicted.Name)),
            "native scroll keeps shared prior plus actual card preview: "+s.Character);
        Check(!choices["KALEIDOSCOPE"].Enabled&&!choices["MASSIVE_SCROLL"].Enabled,"foreign and multiplayer exclusions retained");
        Check(before==RewardForesightReader.Fingerprint(state,service.Player)&&eventBefore.Equals(model.Rng.ToSerializable()),
            "all Neow scoring leaves real RNG and inventory unchanged");
    }
}
