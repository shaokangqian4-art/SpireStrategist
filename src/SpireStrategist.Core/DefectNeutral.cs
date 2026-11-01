using System.Text.Json;
namespace SpireStrategist.Core;

// Explicit, conservative mechanisms for neutral cards. No Ironclad strength/exhaust
// payoff is imported. Other neutral cards remain visibly unsupported.
public static class DefectNeutral
{
    private sealed record Model(double Base,double Upgrade,double Duplicate,Dictionary<string,double> Weights,string Note);
    private sealed record Data(Dictionary<string,Model> Cards);
    private static readonly Dictionary<string,Model> models=Load();
    private static Dictionary<string,Model> Load()
    {
        using var stream=typeof(DefectNeutral).Assembly.GetManifestResourceStream("SpireStrategist.DefectNeutral.json")!;
        return JsonSerializer.Deserialize<Data>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!.Cards;
    }
    public static bool Known(string name)=>name=="Apotheosis"||models.ContainsKey(name);
    public static double? Upgrade(CardSnapshot c)=>models.TryGetValue(c.ClassName,out var m)?Math.Min(m.Base<0?4:12,2+3*m.Upgrade):null;
    public static CardProfile? ProfileFor(CardSnapshot c)
    {
        if(c.ClassName=="Apotheosis")return new(0,Upgrade:20);
        if(!models.TryGetValue(c.ClassName,out var m))return null;
        var name=c.ClassName;
        var draw=name switch {"MasterOfStrategy"=>1.5,"Scrawl"=>1.5,"SecretTechnique" or "SecretWeapon"=>.7,"Finesse" or "FlashOfSteel" or "ThinkingAhead"=>.3,_=>0d};
        var attack=c.Type=="Attack"?Math.Min(2,(double)c.Variables.GetValueOrDefault("Damage")/10):0;
        var block=name is "TheGambit" or "PanicButton" or "Prolong"?0:Math.Min(2,(double)c.Variables.GetValueOrDefault("Block")/10);
        return new(m.Base,Attack:attack,Block:block,Draw:draw,Energy:name=="Production"?1:0,
            ExhaustSource:name=="Purity"?1:0,Upgrade:Upgrade(c)??0);
    }
    public static IReadOnlyDictionary<string,double> Features(RunSnapshot s)
    {
        if(HeroPolicy.Supports(s.Character))return HeroStrategy.NeutralFeatures(s);
        if(s.Character=="Silent")return SilentStrategy.NeutralFeatures(s);
        var deck=s.Deck;
        bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        int N(params string[] names)=>deck.Count(c=>names.Contains(c.ClassName));
        var act=s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act;
        return new Dictionary<string,double>
        {
            ["early"]=act==1?1:act==2?.4:0,["recall"]=N("AllForOne")>0?1:0,
            ["physical"]=Math.Min(5,DefectComposition.Physical(s)),
            ["rare"]=Math.Min(6,deck.Count(c=>c.Rarity=="Rare")),
            ["junk"]=Math.Min(8,deck.Count(c=>DefectStrategy.Basic(c)||c.Type is "Curse" or "Status")),
            ["defends"]=Math.Min(6,deck.Count(c=>c.ClassName is "DefendDefect" or "UltimateDefend")),
            ["draw"]=Math.Min(5,deck.Count(c=>DefectPolicy.Tag(c,"draw")||c.ClassName is "MasterOfStrategy" or "Scrawl")),
            ["cycle"]=Math.Min(5,N("AllForOne","Scrape","Hologram","Skim","Overclock","Coolheaded","Finesse","FlashOfSteel","Scrawl")),
            ["goodAttack"]=Math.Min(5,deck.Count(c=>c.Type=="Attack"&&c.Rarity!="Basic"&&c.Cost>=1)),
            ["goodSkill"]=Math.Min(4,N("DoubleEnergy","Hologram","Compact","AllForOne","Catastrophe")),
            ["goodTarget"]=Math.Min(7.5,deck.Count(c=>c.Rarity!="Basic"&&c.Type is "Skill" or "Power")),
            ["energy"]=Math.Min(4,N("DoubleEnergy","Turbo","Fusion","Supercritical","Production")+Math.Max(0,s.Energy-3)),
            ["expensive"]=Math.Min(5,deck.Count(c=>c.Rarity!="Basic"&&(c.Cost>=2||c.CostsX))),
            ["frost"]=Math.Min(4,deck.Count(c=>DefectPolicy.Tag(c,"frost")||DefectPolicy.Tag(c,"frost2")||DefectPolicy.Tag(c,"frost3"))),
            ["block"]=Math.Min(4,deck.Count(c=>c.Rarity!="Basic"&&c.Variables.GetValueOrDefault("Block")>0)),
            ["debuff"]=Math.Min(3,N("BeamCell","GoForTheEyes","Shockwave","DarkShackles")),
            ["attackRatio"]=deck.Count(c=>c.Type=="Attack")/Math.Max(1d,deck.Length),
            ["xCount"]=Math.Min(3,deck.Count(c=>c.CostsX)),["largeDeck"]=Math.Min(12,Math.Max(0,deck.Length-20)),
            ["ice"]=R("ICE_CREAM")?1:0,["pyramid"]=R("RUNIC_PYRAMID")?1:0,["sozu"]=R("SOZU")?1:0,
            ["ghost"]=R("GHOST_SEED")?1:0,["chemical"]=R("CHEMICAL_X")?1:0,
            ["strikeDummy"]=R("STRIKE_DUMMY")?1:0,["fasten"]=N("Fasten")>0?1:0
        };
    }
    public static OptionScore? Score(RunSnapshot s,DecisionOption o,CardSnapshot c)
    {
        if(c.ClassName=="Apotheosis")return ApotheosisPolicy.Score(s,o,c);
        if(!Known(c.ClassName))return null;
        if(CardCatalog.HasUnmodelledModification(c)||!string.IsNullOrEmpty(c.Enchantment)&&c.Enchantment!="ENCHANTMENT.SWIFT")
            return new(o.Key,o.Name,false,o.Enabled,null,[],["附魔／异变未建模，不能据此推荐。"]);
        var n=s.Deck.Count(d=>d.ClassName==c.ClassName);
        var model=models[c.ClassName];var features=Features(s);
        // Values already include the card's own draw slot, energy and conditional-use costs.
        var parts=new List<ScorePart>{new("无色牌在机器人构筑中的净收益先验（已扣抽牌与启动成本）",model.Base+(c.UpgradeLevel>0?model.Upgrade:0)),
            new("无色牌机制与当前牌组的配合",model.Weights.Sum(w=>w.Value*features[w.Key])),
            new("无色牌重复份数递减",-n*model.Duplicate-n*n*.5)};
        parts.AddRange(HeroPolicy.Supports(s.Character)?HeroRelics.CardAdjustments(s,c):s.Character=="Silent"?SilentRelics.CardAdjustments(s,c):DefectRelics.CardAdjustments(s,c));
        parts.AddRange(CopyEngine.Adjustments(s,c));
        if(CopyEngine.Swift(c))parts.Add(new("迅捷首次打出抽牌，普通牌不重复触发",Math.Min(4,c.EnchantmentAmount*1.2)));
        if(s.Character!="Defect")parts=parts.Select(p=>p with{Reason=p.Reason.Replace("机器人",HeroPolicy.Label(s.Character))}).ToList();
        return new(o.Key,o.Name,true,o.Enabled,Math.Round(parts.Sum(p=>p.Value),3),parts.ToArray(),
            ["无色牌使用"+HeroPolicy.Label(s.Character)+"机制特征；未完成完整战斗模拟。"]);
    }
}
