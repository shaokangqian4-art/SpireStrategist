namespace SpireStrategist.Core;

// Audited native Silent pool (8 entries), upgraded starter, and character-sensitive
// shared/Ancient effects. Other universal effects use the common RelicValue table.
public static class SilentRelics
{
    public static RelicAssessment? Evaluate(string id,RunSnapshot s)
    {
        if(s.Character!="Silent")return null;
        id=id.Split('.').Last();var a=SilentPolicy.Analyze(s);var v=new StrategicValue(s);
        bool R(string n)=>v.Has(n);var h=Math.Clamp(v.Horizon,.5,1.6);
        var knives=Math.Min(5,a.Shivs);var discard=Math.Min(5,a.Discard);
        var powers=s.Deck.Count(c=>c.Type=="Power");
        var blocks=s.Deck.Count(c=>c.Variables.GetValueOrDefault("Block")>0);
        var cheap=Math.Min(7,a.CheapPlays+a.Draw*.3);
        // Three-attack relics count attacks, not cheap skills/poison applications.
        var attacks=Math.Min(7,knives+s.Deck.Count(c=>c.Type=="Attack"&&!SilentStrategy.Basic(c)&&c.Cost<=1)*.5
            +Math.Min(1,a.Draw*.12));
        double? value=id switch{
            "RING_OF_THE_SNAKE"=>9,"RING_OF_THE_DRAKE"=>19,
            "TOUCH_OF_OROBAS"=>R("RING_OF_THE_SNAKE")?16:0,
            "HELICAL_DART"=>3+knives*Math.Min(3,blocks*.6),
            "NINJA_SCROLL"=>6+Math.Min(5,s.Deck.Count(c=>SilentPolicy.Tag(c,"shiv_payoff"))*2),
            "PAPER_KRANE"=>4+Math.Min(12,a.Weak*3),
            "SNECKO_SKULL"=>a.PoisonSeeds==0?2:5+Math.Min(12,a.PoisonSeeds*3+(s.Deck.Any(c=>c.ClassName=="Envenom")?knives:0)),
            "TINGSHA"=>3+discard*2.2,"TOUGH_BANDAGES"=>3+discard*3.2,
            "TWISTED_FUNNEL"=>6+(a.PrimaryDamage=="毒"?5:0),
            "KUNAI"=>3+attacks*1.7+Math.Min(3,blocks*.4),"SHURIKEN"=>a.PrimaryDamage=="毒"?3:3+attacks*2,
            "ORNAMENTAL_FAN"=>4+attacks*1.3,"NUNCHAKU"=>5+attacks,
            "KUSARIGAMA"=>3+attacks*1.5,"LETTER_OPENER"=>4+Math.Min(9,a.Draw+a.Discard),
            "POCKETWATCH"=>12-Math.Min(10,cheap),"PEN_NIB"=>a.PrimaryDamage=="毒"?3:6+Math.Min(5,a.Immediate),
            "GAMBLING_CHIP"=>11+Math.Min(5,a.Sly),"BAG_OF_PREPARATION"=>12,
            "MUMMIFIED_HAND"=>4+Math.Min(14,powers*2),"TOXIC_EGG"=>10*h,"FROZEN_EGG"=>(4+powers)*h,"MOLTEN_EGG"=>(3+Math.Min(4,a.Immediate*.4))*h,
            "CHEMICAL_X"=>s.Deck.Any(c=>c.CostsX)?14:1,
            "ICE_CREAM"=>10+Math.Min(8,a.Energy+s.Deck.Count(c=>c.Cost>=2&&!c.Keywords.Contains("Sly"))),
            "RUNIC_PYRAMID"=>13+Math.Min(8,discard*1.5)-Math.Max(0,v.Basics-5)
                +(s.Deck.Any(c=>c.ClassName=="BulletTime")?12:0)
                -(s.Deck.Any(c=>c.ClassName=="WellLaidPlans")?6:0),
            "SNECKO_EYE"=>6+s.Deck.Where(c=>!c.CostsX&&!c.Keywords.Contains("Sly")).Sum(c=>c.Cost-1.5)*1.4-knives*3-(R("RUNIC_PYRAMID")?20:0),
            "VELVET_CHOKER"=>v.Energy-Math.Min(35,cheap*3+a.Draw),
            "SPIKED_GAUNTLETS"=>v.Energy-powers*6,
            "GHOST_SEED"=>Math.Min(22,v.Basics*2.5),
            "DUSTY_TOME"=>Math.Max(0,v.Add(SilentPolicy.Card("WraithForm",true)!))*2,
            "ARCHAIC_TOOTH"=>s.Deck.Any(c=>c.ClassName=="Neutralize")?15+Math.Min(5,a.Weak):0,
            "PAELS_CLAW"=>Math.Min(18,s.Deck.Count(c=>c.Variables.GetValueOrDefault("Block")>0&&!c.Modified)*2)-Math.Max(0,3-a.Defense)*2,
            "CLAWS"=>s.Deck.Count(c=>c.ClassName=="StrikeSilent")*(a.PrimaryDamage=="毒"?2:3),
            "NUTRITIOUS_SOUP"=>s.Deck.Count(c=>c.ClassName=="StrikeSilent")*2,
            "TOASTY_MITTENS"=>7+Math.Min(8,v.Basics*1.2+s.Deck.Count(c=>c.Type=="Curse")*2)
                +Math.Min(9,knives*2)-(s.Deck.Length<12?6:0),
            "GIRYA"=>(3+Math.Min(10,knives*2))*h,
            "FIDDLE"=>v.Energy-Math.Min(28,a.Draw*2.5+discard*2),
            "BING_BONG"=>-8,"FORGOTTEN_SOUL"=>3,
            "CHOSEN_CHEESE"=>v.MaxHp(Math.Clamp(v.Remaining*.45,1,30)),
            _=>null};
        var selection=id=="CLAWS"?"transform":"";
        return value.HasValue?new(value.Value,selection,"猎人遗物：按已有小刀/毒、主动弃牌、过牌和防守能力估值；不是战斗模拟。"):null;
    }
    public static IEnumerable<ScorePart> CardAdjustments(RunSnapshot s,CardSnapshot c)
    {
        bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        bool T(string t)=>SilentPolicy.Tag(c,t);double value=0;
        if(T("discard"))value+=(R("TOUGH_BANDAGES")?2:0)+(R("TINGSHA")?1.4:0);
        if(T("shiv_source"))value+=Math.Min(3,new[]{"KUNAI","SHURIKEN","ORNAMENTAL_FAN","HELICAL_DART"}.Count(R)*.8);
        if(T("poison_seed")&&R("SNECKO_SKULL"))value+=1.2;
        if(T("weak")&&R("PAPER_KRANE"))value+=1;
        if(c.Type=="Power"&&R("MUMMIFIED_HAND"))value+=1;
        if(c.CostsX&&R("CHEMICAL_X"))value+=2;
        if(R("VELVET_CHOKER")&&(T("shiv_source")||T("draw")))value-=2;
        if(value!=0)yield return new("已有遗物与猎人卡牌机制联动",value);
    }
}
