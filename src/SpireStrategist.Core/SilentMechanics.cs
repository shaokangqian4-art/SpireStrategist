namespace SpireStrategist.Core;

public static class SilentMechanics
{
    public static bool Has(RunSnapshot s,string relic)=>s.Relics.Any(r=>r.Split('.').Last()==relic);
    // Pyramid prevents the end-turn flush; it does not grant the Retain keyword.
    public static bool RedundantRetainUpgrade(RunSnapshot s,CardSnapshot c)=>Has(s,"RUNIC_PYRAMID")&&c.ClassName=="CalculatedGamble";
    public static bool OffPlan(SilentPolicy.Composition a,CardSnapshot c)=>
        a.PrimaryDamage=="小刀"&&(SilentPolicy.Tag(c,"poison_seed")||SilentPolicy.Tag(c,"poison_payoff")||SilentPolicy.Tag(c,"poison_conditional")||SilentPolicy.Tag(c,"poison_block"))||
        a.PrimaryDamage=="毒"&&(SilentPolicy.Tag(c,"shiv_source")||SilentPolicy.Tag(c,"shiv_payoff"));
    // Native Silent discard and self-Exhaust are NOT removal of other cards.
    // Verified neutral/foreign-card exceptions; no credit for hypothetical future tools.
    public static bool CanCleanJunk(RunSnapshot s)=>Has(s,"TOASTY_MITTENS")||
        s.Deck.Any(c=>c.ClassName is "Purity" or "BurningPact" or "TrueGrit" or "FiendFire" or "SecondWind");
    public static double StrengthSupport(RunSnapshot s,double shivs)
        =>(Has(s,"VAJRA")?1:0)+(Has(s,"GIRYA")?Math.Clamp(s.GiryaLifts??0,0,3):0)
          +(Has(s,"TOASTY_MITTENS")?2:0)+(Has(s,"SHURIKEN")&&shivs>=1.5?1.5:0);
    public static bool UpgradedTrap(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName=="KnifeTrap"&&c.UpgradeLevel>0);
    public static bool RedundantShivUpgrade(RunSnapshot s,CardSnapshot c)=>UpgradedTrap(s)&&
        c.ClassName is "HiddenDaggers" or "StormOfSteel";
    public static string? UpgradeNote(RunSnapshot s,CardSnapshot c)=>RedundantShivUpgrade(s,c)?
        "刀刃陷阱＋会升级回放的小刀：降低仅将小刀变为小刀＋的升级收益，仍保留首次出刀伤害。":
        c.ClassName=="KnifeTrap"?"升级回放的小刀；优先第一张，已有升级版本时降低重复敲牌收益。":null;
    public static double LiftValue(RunSnapshot s)=>4+Math.Min(8,SilentPolicy.Analyze(s).Shivs*1.5)
        +Math.Min(3,s.Deck.Count(c=>c.Type=="Attack"&&EnchantmentValue.Hits(c)>1));
}
