namespace SpireStrategist.Core;

// Native role pools: 8 per character. Shared Neow preferences remain above these
// conditional values. Ancient starter replacements use the actual role/card.
public static class HeroRelics
{
    public static RelicAssessment? Evaluate(string id,RunSnapshot s)
    {
        if(!HeroPolicy.Supports(s.Character))return null;id=id.Split('.').Last();
        var a=HeroPolicy.Analyze(s);var v=new StrategicValue(s);bool H(string n)=>v.Has(n);
        int N(string tag)=>s.Deck.Count(c=>HeroPolicy.Tag(c,tag));var playerAttacks=s.Deck.Count(c=>c.Type=="Attack"&&!HeroPolicy.Tag(c,"osty_attack")&&!HeroStrategy.Basic(c));
        var attacks=Math.Min(6,playerAttacks*.6+a.OstyAttacks*.5+a.Draw*.2);
        double? value=id switch{
            "DIVINE_RIGHT"=>8,"DIVINE_DESTINY"=>17,"BOUND_PHYLACTERY"=>9,"PHYLACTERY_UNBOUND"=>19,
            "FENCING_MANUAL"=>7+(a.Primary=="铸剑"?6:0),
            "GALACTIC_DUST"=>3+Math.Min(11,Math.Min(a.Stars,a.StarDemand)),
            "LUNAR_PASTRY"=>8+Math.Min(8,a.StarDemand),
            "MINI_REGENT"=>a.StarDemand<=0?2:4+Math.Min(12,Math.Min(a.Stars+HeroPolicy.OpeningStars(s)*.5,a.StarDemand)*.5+playerAttacks),
            "ORANGE_DOUGH"=>7+Math.Min(5,N("generation_payoff")+N("generation_engine")),
            "REGALITE"=>3+Math.Min(7,a.Generation*1.5+(a.Forge>0?1:0)),
            "VITRUVIAN_MINION"=>s.Deck.Any(c=>c.ClassName is "Begone" or "Charge" or "Guards")?14:2,
            "BIG_HAT"=>7+Math.Min(6,N("ethereal_payoff")*2),
            "BONE_FLUTE"=>4+Math.Min(12,a.OstyAttacks*2),
            "BOOK_REPAIR_KNIFE"=>a.Doom<10?2:7+Math.Min(7,a.Doom*.15)*Math.Clamp(v.Horizon,.5,1.5),
            "BOOKMARK"=>3+Math.Min(11,s.Deck.Count(c=>c.Keywords.Contains("Retain")&&c.Cost>=1)*2),
            "FUNERARY_MASK"=>8+Math.Min(7,N("soul_payoff")*2.5),
            "IVORY_TILE"=>3+Math.Min(12,s.Deck.Count(c=>c.Cost>=3&&!c.CostsX)*2.5),
            "UNDYING_SIGIL"=>a.Doom>=15?10:2,
            "TOUCH_OF_OROBAS"=>s.Character=="Regent"?H("DIVINE_RIGHT")?15:0:H("BOUND_PHYLACTERY")?17:0,
            "DUSTY_TOME"=>Math.Max(0,v.Add(HeroPolicy.Card(s.Character=="Regent"?"TheSealedThrone":"ForbiddenGrimoire",true)!))*2,
            "ARCHAIC_TOOTH"=>s.Deck.Any(c=>c.ClassName==(s.Character=="Regent"?"FallingStar":"Unleash"))?
                Math.Max(0,v.Add(HeroPolicy.Card(s.Character=="Regent"?"MeteorShower":"Protector")!))*2+5:0,
            "CLAWS"=>s.Deck.Count(c=>c.ClassName=="Strike"+s.Character)*3,
            "NUTRITIOUS_SOUP"=>s.Deck.Count(c=>c.ClassName=="Strike"+s.Character)*2,
            "PAELS_CLAW"=>Math.Min(18,s.Deck.Count(c=>c.Variables.GetValueOrDefault("Block")>0&&!c.Modified)*2)-Math.Max(0,3-a.Defense)*2,
            "TOASTY_MITTENS"=>6+Math.Min(9,v.Basics*1.2)+(playerAttacks>=3?5:0)-(s.Deck.Length<12?5:0),
            "GIRYA"=>(3+Math.Min(8,playerAttacks+N("multihit")*1.5))*Math.Clamp(v.Horizon,.5,1.5),
            "SHURIKEN"=>3+Math.Min(12,playerAttacks*1.5+N("multihit")),"KUNAI"=>3+attacks*1.5,
            "ORNAMENTAL_FAN"=>4+attacks,"NUNCHAKU"=>5+attacks,"LETTER_OPENER"=>4+Math.Min(9,a.Draw+a.Souls),
            "CHEMICAL_X"=>s.Deck.Any(c=>c.CostsX)?15:1,
            "GHOST_SEED"=>Math.Min(22,v.Basics*2.5),
            "RUNIC_PYRAMID"=>14-Math.Max(0,v.Basics-4)+Math.Min(7,N("cleanup")*2),
            "SNECKO_EYE"=>6+s.Deck.Where(c=>!c.CostsX).Sum(c=>c.Cost-1.5)*1.4-a.Souls*2-(H("RUNIC_PYRAMID")?20:0),
            "VELVET_CHOKER"=>v.Energy-Math.Min(35,a.Draw*1.4+a.Souls*4+a.Generation*2),
            "SPIKED_GAUNTLETS"=>v.Energy-s.Deck.Count(c=>c.Type=="Power")*6,
            _=>null};
        return value.HasValue?new(value.Value,"","原生角色机制与当前构筑的条件估值；非实战胜率。"):null;
    }
    public static IEnumerable<ScorePart> CardAdjustments(RunSnapshot s,CardSnapshot c)
    {
        bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);bool T(string t)=>HeroPolicy.Tag(c,t);double v=0;
        if(T("osty_attack")&&R("BONE_FLUTE"))v+=1.4;
        if(T("soul_payoff")&&R("FUNERARY_MASK"))v+=1.5;
        if(T("ethereal_payoff")&&R("BIG_HAT"))v+=1;
        if(T("doom_source")&&(R("BOOK_REPAIR_KNIFE")||R("UNDYING_SIGIL")))v+=1.3;
        if(c.Cost>=3&&!c.CostsX&&R("IVORY_TILE"))v+=1.5;
        if(T("sword_payoff")&&R("FENCING_MANUAL"))v+=1.3;
        if((T("star_finisher")||T("star_engine"))&&R("LUNAR_PASTRY"))v+=1.3;
        if(T("generation")&&R("REGALITE"))v+=1;
        if(c.ClassName is "Begone" or "Charge" or "Guards"&&R("VITRUVIAN_MINION"))v+=2;
        if(c.CostsX&&R("CHEMICAL_X"))v+=2;
        if(c.Type=="Power"&&R("MUMMIFIED_HAND"))v+=1;
        if(v!=0)yield return new("已有遗物与角色资源、输出机制联动",v);
    }
}
