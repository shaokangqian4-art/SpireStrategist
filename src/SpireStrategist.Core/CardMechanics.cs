namespace SpireStrategist.Core;

// Mechanism-aware priors. These are marginal utility estimates, never win probabilities.
public static class CardMechanics
{
    public static int Copies(RunSnapshot s, CardSnapshot c) => s.Deck.Count(d => d.ClassName == c.ClassName);
    public static double ActiveExhaust(CardProfile p) => p.ExhaustSource >= .7 ? p.ExhaustSource : 0;
    public static double DrawLoad(CardSnapshot c, CardProfile p)
    {
        // Known unconditional free redraws replace their own slot. Conditions and energy
        // costs are kept separate; a generic Draw tag is NOT sufficient for exemption.
        if (c.Cost == 0 && c.ClassName is "ThinkingAhead" or "Finesse" or "FlashOfSteel" or "MasterOfStrategy" or "Scrawl") return 0;
        if (c.ClassName == "Cascade") return .35;
        if (c.ClassName == "Offering") return .2;
        if (c.ClassName == "BattleTrance") return .35;
        // One-shot transition damage leaves later cycles, but still costs the first draw.
        if (c.ClassName == "MoltenFist" && c.Keywords.Contains("Exhaust")) return .65;
        // A paid draw-one replaces the card in hand, but consumes scarce turn energy.
        if(c.Cost>0&&p.Draw>0&&p.Draw<=.5)return .85;
        if(c.Type=="Power"||c.Keywords.Contains("Exhaust"))return .8;
        return p.Draw > 0 ? .55 : 1;
    }
    public static double EnergyDemand(RunSnapshot s)
    {
        var profiles = s.Deck.Select(CardCatalog.Find).OfType<CardProfile>().ToArray();
        var costly = s.Deck.Count(c => c.CostsX || c.Cost >= 2);
        var draw = profiles.Sum(p => p.Draw) * 15 / Math.Max(10, s.Deck.Length);
        var supply = Math.Max(0, s.Energy - 3) + profiles.Sum(p => p.Energy) * .45;
        // Draw alone is not proof that more energy can be spent. Keep its contribution
        // modest until paired fights support a larger increase; expensive cards remain
        // the main demand signal. Frozen before the predeclared seed 303 holdout.
        return Math.Clamp((.35 + costly / 3d + draw * .10) / (1 + supply * .35), .25, 2.2);
    }
    public static double Upgrade(RunSnapshot s, CardSnapshot c)
    {
        if(ApotheosisPolicy.UpgradeOverride(s,c) is {} apotheosis)return apotheosis;
        if(HeroPolicy.Supports(s.Character))return HeroStrategy.Upgrade(s,c);
        if(s.Character=="Silent")return SilentStrategy.Upgrade(s,c);
        if(s.Character=="Defect")return DefectStrategy.Upgrade(s,c);
        var p = CardCatalog.Find(c);
        if (!c.CanUpgrade || p == null) return 0;
        var copies = Copies(s, c);
        var value = p.Upgrade;
        if (p.Draw > 0 || p.Energy > 0) value *= 1.15;
        if (c.Type == "Power" && copies > 1) value /= 1 + .3 * (copies - 1);
        var block = s.Deck.Select(CardCatalog.Find).OfType<CardProfile>().Sum(x => x.Block);
        if (c.ClassName is "BodySlam" or "Barricade" or "Unmovable" or "Entrench")
            value *= Math.Clamp(block / 4, .4, 1.3);
        if (c.ClassName == "Bloodletting") value *= Math.Clamp(EnergyDemand(s), .6, 1.3);
        if (c.ClassName == "ThinkingAhead") value = 10;
        if (c.ClassName == "Bash")
        {
            // +2 damage and one extra Vulnerable turn, not a new engine or cost reduction.
            var otherVulnerable=s.Deck.Count(d=>d.ClassName is "Uppercut" or "Shockwave" or "Thunderclap" or "Tremble" or "Taunt");
            var payoffs=s.Deck.Count(d=>d.ClassName is "MoltenFist" or "Dismantle" or "Colossus" or "Dominate" or "Cruelty");
            value=(4.5+Math.Min(2,payoffs*.75))/(1+.4*otherVulnerable);
        }
        if (c.ClassName == "BattleTrance")
            value=7/(1+.8*Math.Max(0,copies-1)); // One extra draw; duplicates share the no-draw lock.
        if(c.ClassName=="Inflame")
        {
            var multi=s.Deck.Select(CardCatalog.Find).OfType<CardProfile>().Sum(x=>x.StrengthPayoff);
            var alternative=s.Deck.Count(d=>d.ClassName is "Dominate" or "DemonForm");
            value=(5+Math.Min(3,multi))/(1+.5*alternative+.4*Math.Max(0,copies-1))*(s.Act<=1?1:s.Act==2?.85:.7);
        }
        if(c.ClassName=="Dominate")
            value=(8+Math.Min(5,IroncladSynergies.VulnerablePayoffs(s))+
                Math.Min(3,IroncladSynergies.VulnerableCoverage(s,true)*2))/(1+.25*Math.Max(0,copies-1));
        if(c.ClassName=="Juggernaut")value=2+Math.Min(12,IroncladSynergies.BlockTriggers(s)*3);
        if(c.ClassName=="CrimsonMantle")value=(s.Hp<.25*s.MaxHp?6:12)+(s.Deck.Any(d=>d.ClassName=="Juggernaut")?2:0);
        if(c.ClassName=="Break")value=14+Math.Min(6,IroncladSynergies.VulnerablePayoffs(s)*1.5);
        if(c.ClassName=="Conflagration")value=8+Math.Min(8,IroncladSynergies.SupportedStrength(s));
        if (c.ClassName == "FeelNoPain")
        {
            var exhaust=s.Deck.Select(CardCatalog.Find).OfType<CardProfile>().Sum(ActiveExhaust);
            var armor=s.Deck.Any(d=>d.ClassName is "Barricade" or "BodySlam" or "Juggernaut");
            value=(6+Math.Min(10,exhaust*3)+(armor&&exhaust>=1?2:0))/(1+.4*Math.Max(0,copies-1));
        }
        if (c.ClassName == "Stoke")
        {
            // Native upgrade upgrades EVERY generated card, not Stoke's energy cost.
            // Hand-wide replacement is repeatable; payoff grows with a working exhaust engine.
            var payoffs = s.Deck.Count(d => d.ClassName is "DarkEmbrace" or "DrumOfBattle"
                or "HowlFromBeyond" or "AshenStrike");
            value = (17 + Math.Min(8, payoffs * 4)) / (1 + .35 * Math.Max(0, copies - 1));
        }
        if (c.ClassName == "DarkEmbrace")
        {
            // Native 2 -> 1 energy: makes setting up the engine and exhausting on the
            // same turn easier. This is upgrade value, not another card-pick bonus.
            var exhaust = s.Deck.Select(CardCatalog.Find).OfType<CardProfile>().Sum(ActiveExhaust);
            value = (15 + Math.Min(5, exhaust * 2) + (s.Deck.Any(d => d.ClassName == "Stoke") ? 4 : 0))
                / (1 + .3 * Math.Max(0, copies - 1));
        }
        return UpgradeReview.Refine(s,c,value);
    }
    public static double HealthyHealingMultiplier(double ratio) => .65 + .35 * Math.Clamp((.65 - ratio) / .3, 0, 1);
    public static double RestHealingMultiplier(double ratio) => .4 + .6 * Math.Clamp((.65 - ratio) / .3, 0, 1);
    public static double RecentCombatHealingMultiplier(RunSnapshot s)
    {
        // Gross room damage, not net HP: healing cannot disguise a dangerous encounter.
        // This small confidence adjustment is NOT a forecast of the next boss's damage.
        var samples=s.RecentBattles.Where(b=>b.Act==s.Act&&b.MaxHp>0&&b.Room is "Elite" or "Boss").TakeLast(3).ToArray();
        if(samples.Length<3||s.Hp<.65*s.MaxHp||samples.Any(b=>b.PotionsUsed>0)||s.Map?.EnemyDamageScale>1)return 1;
        var worst=samples.Max(b=>b.DamageTaken/(double)b.MaxHp)*s.MaxHp;
        return worst<=.15*s.MaxHp && s.Hp>2*worst+.35*s.MaxHp?.85:1;
    }
}
