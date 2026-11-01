namespace SpireStrategist.Core;

// Native 0.111.0 mechanisms; bounded priors, not simulated win probabilities.
public static class DefectRelics
{
    private static bool Has(RunSnapshot s,string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
    internal static bool EnginePurchase(RunSnapshot s,string id)
    {
        var powers=s.Deck.Count(c=>c.Type=="Power");
        var producers=s.Deck.Count(c=>new[]{"lightning","frost","frost2","frost3","dark","glass","lightning_engine"}.Any(t=>DefectPolicy.Tag(c,t)));
        return id.Split('.').Last() switch
        {
            "MUMMIFIED_HAND"=>powers>=3&&s.Deck.Any(c=>c.Type=="Power"&&c.Cost>=2),
            "DATA_DISK"=>producers>=3&&DefectComposition.Orbs(s)>=DefectComposition.Physical(s),
            "BAG_OF_PREPARATION"=>s.Deck.Length>=14&&s.Deck.Any(c=>c.ClassName is "Storm" or "EchoForm" or "AllForOne" or "Voltaic"),
            _=>false
        };
    }
    public static RelicAssessment? Evaluate(string id,RunSnapshot s)
    {
        if(s.Character!="Defect")return null;
        id=id.Split('.').Last();
        var f=DefectNeutral.Features(s);var v=new StrategicValue(s);
        var powers=s.Deck.Count(c=>c.Type=="Power");var orbs=Math.Min(5,DefectComposition.Orbs(s)+f["frost"]);
        var dark=s.Deck.Count(c=>c.ClassName is "Darkness" or "ConsumingShadow");
        var attacks=Math.Min(5,f["physical"]+s.Deck.Count(c=>c.Type=="Attack"&&c.Cost==0)*.35);
        var h=Math.Clamp(v.Horizon,.5,1.5);
        double RandomTypedUpgrade(string type)
        {
            var pool=s.Deck.Where(c=>c.Type==type&&c.CanUpgrade).ToArray();
            return pool.Length==0?0:pool.Average(v.Upgrade)*Math.Min(2,pool.Length);
        }
        double? value=id switch
        {
            "DATA_DISK"=>3+orbs*2,"GOLD_PLATED_CABLES"=>2+orbs*1.5+dark,
            "RUNIC_CAPACITOR"=>2+Math.Min(7,orbs)+dark*2-(s.Deck.Any(c=>c.ClassName=="Thunder")&&dark==0?3:0),
            "EMOTION_CHIP"=>2+orbs*1.2,"SYMBIOTIC_VIRUS"=>4+dark*2,
            "CRACKED_CORE"=>4,"INFUSED_CORE"=>12+Math.Min(6,s.Deck.Count(c=>c.ClassName is "Thunder" or "Storm" or "Voltaic")*2),
            "TOUCH_OF_OROBAS"=>Has(s,"CRACKED_CORE")?12+Math.Min(8,orbs*1.2):0,
            "ICE_CREAM"=>12+Math.Min(10,f["energy"]+f["expensive"]),
            "CHEMICAL_X"=>f["xCount"]>0?12+f["xCount"]*2:1,
            "MUMMIFIED_HAND"=>3+Math.Min(16,powers*2+f["expensive"]*.5),
            "PERMAFROST"=>powers>0?5+Math.Min(3,powers):1,
            "POWER_CELL"=>3+Math.Min(8,s.Deck.Count(c=>c.Cost==0&&!c.CostsX)*1.5),
            "METRONOME"=>2+orbs*1.5,"FRESNEL_LENS"=>8*h,
            "KUNAI"=>2+attacks*1.8+f["block"]*.4,"SHURIKEN"=>1+attacks*2,
            "ORNAMENTAL_FAN"=>2+attacks*1.4,"KUSARIGAMA"=>2+attacks*1.3,
            "NUNCHAKU"=>3+attacks,"PEN_NIB"=>2+attacks*1.2,
            "LETTER_OPENER"=>4+Math.Min(8,f["cycle"]+s.Deck.Count(c=>c.Type=="Skill")*.25),
            "POCKETWATCH"=>12-Math.Min(8,f["cycle"]+attacks),
            "UNCEASING_TOP"=>2+Math.Min(8,attacks+f["energy"])-Math.Min(6,s.Deck.Count(c=>c.Type is "Curse" or "Status")*2)-(Has(s,"RUNIC_PYRAMID")?5:0),
            "BAG_OF_PREPARATION"=>12,"GAMBLING_CHIP"=>10+Math.Min(5,f["junk"]*.5),
            "FROZEN_EGG"=>(5+Math.Min(6,powers))*h,"TOXIC_EGG"=>(7+f["cycle"])*h,"MOLTEN_EGG"=>(3+attacks)*h,
            "VENERABLE_TEA_SET"=>6+(Has(s,"ICE_CREAM")?4:0),
            "BOOK_OF_FIVE_RINGS"=>3*h,"RUNIC_PYRAMID"=>12+f["energy"]+f["goodSkill"]-Math.Max(0,f["expensive"]-2)*2-f["junk"]*.5,
            "VELVET_CHOKER"=>v.Energy-Math.Min(30,attacks*3+f["cycle"]*2),
            "SPIKED_GAUNTLETS"=>v.Energy-powers*5,
            "GHOST_SEED"=>Math.Min(22,s.Deck.Count(c=>c.ClassName is "StrikeDefect" or "DefendDefect")*2.5),
            // Gives a character-specific upgraded Ancient card, not Ironclad exhaust scaling.
            "DUSTY_TOME"=>8,
            "WAR_PAINT"=>RandomTypedUpgrade("Skill"),"WHETSTONE"=>RandomTypedUpgrade("Attack"),
            "AKABEKO"=>4+Math.Min(3,attacks*.6),
            "TOASTY_MITTENS"=>6+Math.Min(5,f["junk"]*.5)
                +(DefectComposition.Physical(s)>=2&&DefectComposition.Physical(s)>DefectComposition.Orbs(s)?12+attacks:0)
                -(s.Deck.Count(c=>c.Type is "Attack" or "Skill" or "Power")<8?8:0),
            _=>null
        };
        return value.HasValue?new(value.Value,"","机器人遗物机制估值；按现有构筑联动折扣，尚未进行完整战斗模拟。"):null;
    }
    public static IEnumerable<ScorePart> CardAdjustments(RunSnapshot s,CardSnapshot c)
    {
        bool R(string id)=>Has(s,id);
        bool T(params string[] tags)=>tags.Any(t=>DefectPolicy.Tag(c,t));
        var value=0d;
        if(R("DATA_DISK")&&T("frost","frost2","frost3","lightning","dark","glass"))value+=.8;
        if(R("INFUSED_CORE")&&(c.ClassName is "Thunder" or "Storm" or "Voltaic"))value+=1.2;
        if(R("GOLD_PLATED_CABLES")&&T("focus","dark","frost","frost2","frost3"))value+=.6;
        if(R("RUNIC_CAPACITOR")&&c.ClassName=="Darkness")value+=.7;
        if(R("MUMMIFIED_HAND")&&c.Type=="Power")value+=1.2;
        if(R("PERMAFROST")&&c.Type=="Power"&&s.Deck.Count(d=>d.Type=="Power")<3)value+=.4;
        if(R("CHEMICAL_X")&&c.CostsX&&c.ClassName!="Volley")value+=2;
        // Attack-trigger relics alone must not redirect an orb deck toward physical filler.
        if(DefectComposition.Physical(s)>=3&&c.Type=="Attack"&&c.Cost<=1)
            value+=Math.Min(1.5,new[]{"KUNAI","SHURIKEN","ORNAMENTAL_FAN","KUSARIGAMA","NUNCHAKU"}.Count(R)*.4);
        if(value!=0)yield return new("已持有遗物的实际机制联动",value);
    }
}
