using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckMultiplayer0645(RunState state,AdvisorService service)
    {
        CheckHandshake0645();
        Check(SnapshotReader.IsMultiplayer(1,NetGameType.Host)&&SnapshotReader.IsMultiplayer(1,NetGameType.Client)
            &&!SnapshotReader.IsMultiplayer(1,NetGameType.Singleplayer),"network host/client roles remain read-only with one remaining player");
        var local=service.Player;
        var save=local.ToSerializable();save.NetId=local.NetId+1;
        var other=Player.FromSerializable(save);
        state.AddPlayerDebug(other,0);
        try
        {
            Check(state.Players[0]!=local&&AdvisorService.ResolveLocalPlayer(state)==local,"local identity wins over host/first-player order");
            var players=(List<Player>)GameActions.Field(state,"_players")!;
            players.Reverse();
            Check(state.Players[0]==local&&AdvisorService.ResolveLocalPlayer(state)==local,"host identity remains local when first in party");
            players.Reverse();
            var originalId=MegaCrit.Sts2.Core.Context.LocalContext.NetId;
            try
            {
                MegaCrit.Sts2.Core.Context.LocalContext.NetId=other.NetId;
                Check(AdvisorService.ResolveLocalPlayer(state)==other,"second installed client independently resolves its own player");
                MegaCrit.Sts2.Core.Context.LocalContext.NetId=ulong.MaxValue;
                Check(AdvisorService.ResolveLocalPlayer(state)==null,"missing local identity never falls back to host");
            }
            finally{MegaCrit.Sts2.Core.Context.LocalContext.NetId=originalId;}
            var snapshot=SnapshotReader.Read(state,local);
            Check(snapshot.PlayerCount==2&&!snapshot.CombatSolverAvailable,"multiplayer snapshot disables solver-based bonuses");
            var card=ModelDb.Card<Blaze>().ToMutable();card.Owner=local;
            var ignored=SnapshotReader.ReadCard(card);
            Check(ignored.MultiplayerOnly&&MultiplayerAdvice.Ignore(ignored),"native cooperative constraint captured");
            var known=DefectPolicy.Card("Glacier")!;
            var evaluation=new Policy().Evaluate(snapshot,[new("coop",ignored.Name,"card",Card:ignored),new("solo",known.Name,"card",Card:known),new("skip","skip","skip")]);
            Check(evaluation.RecommendedKey is "solo" or "skip","native multiplayer snapshot produces supported personal advice");
            service.Panel.Show(snapshot,evaluation,"multiplayer-fixture");
            Check(((Button)GameActions.Field(service.Panel,"autoButton")!).Disabled,"takeover button disabled");
            var before=RewardForesightReader.Fingerprint(state,local);
            var otherBefore=RewardForesightReader.Fingerprint(state,other);
            service.Autopilot.Enable(true);service.Autopilot.SetFullAutomatic(true);service.Autopilot.Tick();
            ClimbController.SetEnabled(true);ClimbController.Concede(service,1);
            Check(!service.Autopilot.Enabled&&!service.Autopilot.FullAutomatic&&!ClimbController.Enabled,"takeover loop and concession entrypoints reject multiplayer");
            Check(before==RewardForesightReader.Fingerprint(state,local)&&otherBefore==RewardForesightReader.Fingerprint(state,other),"rejected multiplayer automation preserves both players and RNG");
            service.RestEnabled();service.RestStarted(other.NetId);
            Check(!service.RestBusy,"remote camp selection does not mark local camp busy");
            service.RemoteAwareRestFinished(null!,true,other.NetId);
            Check(!service.RestBusy,"remote camp completion does not commit a local choice");
            service.RestStarted(local.NetId);
            Check(service.RestBusy,"local camp selection updates own status");
            service.RestEnabled();
            Check(service.WithForesight(snapshot).Foresight is {Branches.Length:0},"unsupported multiplayer reward forecast cannot invent future cards");
            await Shot0620("0645-multiplayer-advice");
        }
        finally
        {
            ((List<Player>)GameActions.Field(state,"_players")!).Remove(other);
        }
    }

    private sealed class HandshakeProbe0645(NetGameType type):IHandshakeHandler
    {
        public NetGameType Type=>type;
        public bool Success;public bool Failed;
        public void SendHandshakeMessage(ulong peerId,PacketWriter writer){}
        public void HandshakeFailed(ulong peerId,NetErrorInfo info)=>Failed=true;
        public void HandshakeSucceeded(ulong peerId,PeerVersionInfo versionInfo)=>Success=true;
    }
    private static void CheckHandshake0645()
    {
        var names=ModManager.GetGameplayRelevantModNameList()??[];
        var optional=ModManager.GetNonGameplayRelevantModNameList()??[];
        Check(!names.Any(x=>x.StartsWith("SpireStrategist-"))&&optional.Any(x=>x.StartsWith("SpireStrategist-")),"loaded manifest registers assistant as client-optional");
        foreach(var (hostHas,clientHas) in new[]{(true,false),(false,true),(true,true)})
        foreach(var asHost in new[]{true,false})
        {
            PeerVersionInfo Version(bool installed)
            {
                var v=PeerVersionInfo.LocalDefault();
                // Hold unrelated mods constant: only the assistant differs here.
                v.gameplayAffectingMods=[];
                v.otherMods=installed?optional.Where(x=>x.StartsWith("SpireStrategist-")).ToList():null;
                return v;
            }
            var handler=new HandshakeProbe0645(asHost?NetGameType.Host:NetGameType.Client);
            var manager=new HandshakeManager(handler,Version(asHost?hostHas:clientHas),new PacketWriter());
            var writer=new PacketWriter();
            manager.BeginHandshakeFor(42);
            HandshakeManager.WriteHandshakeMessage(42,Version(asHost?clientHas:hostHas),writer);
            var reader=new PacketReader();reader.Reset(writer.Buffer[..writer.BytePosition]);
            manager.HandshakeMessageReceived(42,reader);
            Check(handler.Success&&!handler.Failed,$"native handshake hostMod={hostHas} clientMod={clientHas} receiver={(asHost?"host":"client")}");
        }
    }
}
