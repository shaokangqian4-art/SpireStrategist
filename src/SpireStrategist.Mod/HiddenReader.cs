using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

// Never call live RNG.Next*, PullNextEncounter, or reward generation here.
// Previews consume private copies; exact state (including counter) is checked afterwards.
internal static class HiddenReader
{
    internal static RngSnapshot ReadStream(string name, Rng source)
    {
        var saved = source.ToSerializable();
        var copy = new Rng(saved);
        var preview = Enumerable.Range(0, 4).Select(_ => copy.NextInt(1_000_000)).ToArray();
        if (!saved.Equals(source.ToSerializable())) throw new InvalidOperationException("读取期间随机流发生变化：" + name);
        return new(name, saved.counter, saved.state0, saved.state1, saved.state2, saved.state3, preview);
    }

    internal static HiddenSnapshot Read(IRunState run, Player player)
    {
        try
        {
            var streams = typeof(RunRngSet).GetProperties().Where(p => p.PropertyType == typeof(Rng))
                .Select(p => (Name: "run." + p.Name, Source: (Rng)p.GetValue(run.Rng)!))
                .Concat(typeof(PlayerRngSet).GetProperties().Where(p => p.PropertyType == typeof(Rng))
                    .Select(p => (Name: "player." + p.Name, Source: (Rng)p.GetValue(player.PlayerRng)!))).ToArray();
            var before = streams.Select(s => s.Source.ToSerializable()).ToArray();
            var rng = streams.Select(s => ReadStream(s.Name, s.Source)).ToArray();
            var acts = run.Acts.Select((a, i) =>
            {
                var rooms = a.ToSave().SerializableRooms;
                return new ActForecast(i + 1, rooms.NormalEncounterIds?.Select(id => id.ToString()).ToArray() ?? [], rooms.NormalEncountersVisited,
                    rooms.EliteEncounterIds?.Select(id => id.ToString()).ToArray() ?? [], rooms.EliteEncountersVisited,
                    rooms.EventIds?.Select(id => id.ToString()).ToArray() ?? [], rooms.EventsVisited,
                    rooms.BossId?.ToString(), rooms.SecondBossId?.ToString(), rooms.AncientId?.ToString());
            }).ToArray();
            var draw = player.PlayerCombatState == null ? [] : PileType.Draw.GetPile(player).Cards.Select(c => c.Id.ToString()).ToArray();
            var unchanged = streams.Select((s, i) => before[i].Equals(s.Source.ToSerializable())).All(x => x);
            if (!unchanged) throw new InvalidOperationException("读取期间随机流发生变化；本次预测作废。");
            return new(run.Rng.StringSeed, rng, acts, draw, true);
        }
        catch (Exception ex) { return new(run.Rng.StringSeed, [], [], [], false, ex.Message); }
    }
}
