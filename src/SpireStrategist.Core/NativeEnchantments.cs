namespace SpireStrategist.Core;

// Native 0.111.0 only. Afflictions and unknown third-party effects remain guarded.
public static class NativeEnchantments
{
    public static readonly string[] Ids = "ADROIT CLONE CORRUPTED GLAM GOOPY IMBUED INKY INSTINCT MOMENTUM NIMBLE PERFECT_FIT ROYALLY_APPROVED SHARP SLITHER SLUMBERING_ESSENCE SOULS_POWER SOWN SPIRAL STEADY SWIFT TEZCATARAS_EMBER VIGOROUS".Split(' ');
    public static bool Known(string? id)=>id!=null&&id.StartsWith("ENCHANTMENT.")&&Ids.Contains(id[12..]);
    public static double Glam(CardSnapshot c)
    {
        var p=CardCatalog.Find(c);
        // One extra play per combat, not a permanent doubling of the card.
        return p==null?0:Math.Clamp(p.Attack+p.Block+p.Draw+p.Energy+(c.ClassName=="BladeDance"?2:0),.5,4);
    }
    public static double Royal(StrategicValue v,CardSnapshot c)
    {
        if(c.Type is not ("Attack" or "Skill"))return -10;
        if(c.Rarity=="Basic")return -8; // Crowds out a useful opening draw and future removal.
        var p=CardCatalog.Find(c)!;
        var innate=c.Keywords.Contains("Innate");var retain=c.Keywords.Contains("Retain")||v.State.Relics.Any(r=>r.EndsWith("RUNIC_PYRAMID"));
        var startup=p.Draw+p.Energy;
        var timing=c.CostsX||c.ClassName is "Voltaic" or "MultiCast" or "ParticleWall" or "KnifeTrap" or "GrandFinale" or "FiendFire";
        var value=Math.Clamp(v.Add(c),-4,8)*.35;
        if(!innate)value+=Math.Min(6,startup*1.8)+(c.ClassName=="Apotheosis"?7:0)+(timing?1:0)-1;
        if(!retain)value+=(timing?5:Math.Max(0,c.Cost-1)*1.2+1);
        // Both benefits already supplied: no upgrade in functionality.
        return innate&&retain?0:value;
    }
}
