using System.Text.Json;

namespace SpireStrategist.Core;

public sealed record CardDefinition(string Pool, CardSnapshot Basic, CardSnapshot Upgraded);

// Generated from actual v0.111.0 model instances; strategic features below are estimates.
public static class CardRegistry
{
    public static IReadOnlyDictionary<string, CardDefinition> All { get; } = Load();
    private static IReadOnlyDictionary<string, CardDefinition> Load()
    {
        using var stream = typeof(CardRegistry).Assembly.GetManifestResourceStream("SpireStrategist.Cards.json")
            ?? throw new InvalidOperationException("Missing embedded card registry.");
        using var legacy=typeof(CardRegistry).Assembly.GetManifestResourceStream("SpireStrategist.EventCards.json")
            ?? throw new InvalidOperationException("Missing event card registry.");
        var json=new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<CardDefinition[]>(stream,json)!.Concat(JsonSerializer.Deserialize<CardDefinition[]>(legacy,json)!)
            .ToDictionary(c => c.Basic.ClassName, StringComparer.Ordinal);
    }
    private static readonly HashSet<string> DrawCards = new(StringComparer.Ordinal)
    { "BattleTrance", "BurningPact", "DrumOfBattle", "Finesse", "FlashOfSteel", "Impatience", "HuddleUp", "MasterOfStrategy", "Offering", "PommelStrike", "Restlessness", "ShrugItOff", "ThinkingAhead" };
    private static readonly HashSet<string> ImmediateEnergy = new(StringComparer.Ordinal)
    { "Bloodletting", "Offering", "Production", "Restlessness", "ForgottenRitual" };
    public static double Var(CardSnapshot c, string key) => (double)c.Variables.GetValueOrDefault(key);
    public static CardProfile Estimate(CardSnapshot card, CardDefinition definition)
    {
        // Old logs may omit variables; use the correct upgraded definition as a fallback.
        var c = card.Variables.Count > 0 ? card : card.UpgradeLevel > 0 ? definition.Upgraded : definition.Basic;
        var damage = Math.Max(Var(c, "Damage"), Var(c, "CalculatedDamage"));
        var hits = Math.Max(1, Var(c, "Repeat"));
        var block = Math.Max(Var(c, "Block"), Var(c, "CalculatedBlock"));
        var strength = Var(c, "StrengthPower") / 2;
        var draw = DrawCards.Contains(c.ClassName) ? Var(c, "Cards") / 2 : 0;
        var energy = ImmediateEnergy.Contains(c.ClassName) ? Var(c, "Energy") * .75 : 0;
        return new CardProfile(0, Attack: Math.Min(3, damage * hits / 10), Block: Math.Min(3, block / 10),
            Draw: draw, Energy: energy, Strength: strength, Scaling: strength * .5,
            StrengthPayoff: Math.Min(2, (hits - 1) * .5),
            ExhaustSource: c.Keywords.Contains("Exhaust") ? .2 : 0, SelfDamage: Var(c, "HpLoss") / 6,
            Upgrade: UpgradeEstimate(definition),
            Note: "基础数据已登记；通用近似评价，条件触发及特殊机制尚未全部建模。");
    }
    private static double UpgradeEstimate(CardDefinition d)
    {
        double Delta(string key) => Math.Max(0, Var(d.Upgraded, key) - Var(d.Basic, key));
        return Math.Clamp(3 + Math.Max(0, d.Basic.Cost - d.Upgraded.Cost) * 8 + Delta("Damage") * .8
            + Delta("Block") * .7 + (DrawCards.Contains(d.Basic.ClassName) ? Delta("Cards") * 5 : 0)
            + Delta("StrengthPower") * 3, 3, 18);
    }
}
