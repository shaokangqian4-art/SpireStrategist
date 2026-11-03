namespace SpireStrategist.Core;

// Upgrade utility, not acquisition priors. Read the native before/after models;
// explicitly account for runtime IsUpgraded branches absent from DynamicVars.
public static class UpgradeReview
{
    public static CardSnapshot? Native(string name,bool upgraded)=>
        HeroPolicy.Card(name,upgraded)??SilentPolicy.Card(name,upgraded)??DefectPolicy.Card(name,upgraded)??
        (CardRegistry.All.TryGetValue(name,out var d)?upgraded?d.Upgraded:d.Basic:null);

    public static double Refine(RunSnapshot s,CardSnapshot c,double score)
    {
        var basic=Native(c.ClassName,false);var upgraded=Native(c.ClassName,true);
        if(basic==null||upgraded==null||!c.CanUpgrade||CardCatalog.HasUnmodelledModification(c))return score;
        if(c.ClassName is "ManifestAuthority" or "Quasar")
        {
            // Both upgrade generated colorless cards on every play. Quasar chooses
            // among three; authority also blocks. Star supply limits realizability.
            var support=s.Deck.Count(d=>HeroPolicy.Tag(d,"generation")||d.ClassName is "Arsenal" or "Supermassive");
            score=(c.ClassName=="ManifestAuthority"?14:15)+Math.Min(2,Math.Max(0,support-1));
            if(c.ClassName=="Quasar"&&HeroPolicy.Analyze(s).Stars<2)score*=.6;
            if(s.Deck.Any(d=>d.ClassName==c.ClassName&&d.UpgradeLevel>0))score*=.8;
        }
        if(c.ClassName=="Tyranny")
        {
            // Innate draw establishes the first turn; already-Innate cards and
            // upgraded duplicates reduce the benefit of another guaranteed copy.
            var innate=s.Deck.Count(d=>d.Keywords.Contains("Innate"));
            score=Math.Max(score,14-Math.Min(8,innate*2));
        }
        if(c.ClassName=="Orbit")score=Math.Max(score,16); // 2 -> 1 energy: cheaper energy engine setup.
        if(c.ClassName=="Guards")
        {
            // Every chosen hand card becomes MinionSacrifice+: 7 -> 10 block.
            // Potential hand targets, not the full deck, determine aggregate gain.
            var junk=s.Deck.Count(d=>d.Rarity=="Basic"||d.Type is "Curse" or "Status");
            score=Math.Max(score,6+3*Math.Min(3,junk*5d/Math.Max(1,s.Deck.Length)));
        }

        // Low-prior attacks with ONLY flat damage/forge increments remain filler
        // upgrades in later acts. Draw, debuff duration, energy, generated cards,
        // keywords and exceptional multihit/copy mechanics retain their models.
        var changed=basic.Variables.Keys.Union(upgraded.Variables.Keys)
            .Where(k=>basic.Variables.GetValueOrDefault(k)!=upgraded.Variables.GetValueOrDefault(k)).ToArray();
        var plain=c.Type=="Attack"&&basic.Cost==upgraded.Cost&&basic.StarCost==upgraded.StarCost&&
            basic.Keywords.Order().SequenceEqual(upgraded.Keywords.Order())&&changed.Length>0&&
            changed.All(k=>k is "Damage" or "Forge")&&basic.Variables.Keys.All(k=>k is "Damage" or "Forge")&&
            !basic.Keywords.Contains("Exhaust")&&(CardCatalog.Find(c)?.Base??10)<=5&&
            c.ClassName is not ("AdaptiveStrike" or "Anger" or "Claw" or "AllForOne")&&
            RegentAttackLinks.Hits(s,c)<=1&&!(CardCatalog.Find(c)?.Scaling>0);
        if(s.Act>=2&&plain)score=Math.Min(score,s.Act>=3?4:5);
        return score;
    }
}
