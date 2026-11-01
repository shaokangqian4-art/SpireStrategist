namespace SpireStrategist.Core;

public sealed record PotionCandidate(string Name,string Rarity,bool Native=true);
public sealed record PotionAssessment(double Value,bool Defense,string Reason);
// Shared by all characters. Only strict improvements replace an occupied slot.
public static class PotionPolicy
{
    // Values express situational utility, not a simulated fight result. Native 0.111.0 effects.
    public static PotionAssessment? Evaluate(RunSnapshot s,PotionCandidate p)
    {
        if(!p.Native)return null;
        if(CharacterItems.Potion(s,p) is {} role)return role;
        var v=new StrategicValue(s);
        var boss=s.NextNodeTypes.Contains("Boss") || s.Map is {} m&&m.CurrentRow>=m.BossRow-4;
        var late=s.Map is {} map?s.Act==map.TotalActs&&map.CurrentRow>=map.BossRow-4:s.Act>=3;
        var attacks=s.Deck.Count(c=>c.Type=="Attack");
        var physical=s.Character=="Silent"?SilentPolicy.Analyze(s).PrimaryDamage!="毒":s.Character=="Necrobinder"?
            s.Deck.Count(c=>c.Type=="Attack"&&!HeroPolicy.Tag(c,"osty_attack")&&!HeroStrategy.Basic(c))>=3:
            s.Character!="Defect"?attacks>=3:DefectComposition.Physical(s)>=2;
        var orbs=s.Character=="Defect"&&s.Deck.Any(c=>new[]{"lightning","frost","frost2","frost3","dark","glass"}.Any(t=>DefectPolicy.Tag(c,t)));
        var blocks=s.Deck.Count(c=>c.Variables.GetValueOrDefault("Block")>0);
        var missing=Math.Max(0,s.MaxHp-s.Hp);
        var expensive=s.Deck.Count(c=>c.Cost>=2||c.CostsX);
        var upgraded=s.Deck.Count(c=>c.CanUpgrade);
        var statuses=s.Deck.Count(c=>c.Type is "Curse" or "Status"||DefectPolicy.Tag(c,"status_source"));
        var defense=p.Name is "BlockPotion" or "BloodPotion" or "DexterityPotion" or "SpeedPotion" or "WeakPotion"
            or "ShipInABottle" or "FyshOil" or "GhostInAJar" or "HeartOfIron" or "Fortifier" or "ShacklingPotion"
            or "BeetleJuice" or "LuckyTonic" or "RegenPotion" or "PotionOfBinding" or "Ambergris" or "FairyInABottle";
        double? value=p.Name switch
        {
            "EntropicBrew"=>45+Math.Min(3,s.PotionSlots.Length)*2,
            "FairyInABottle"=>48,
            "FoulPotion"=>CourierPolicy.MayReplace(s)?22*CourierPolicy.MerchantOpportunity(s):0,
            "BlockPotion"=>6,"BloodPotion"=>Math.Min(s.MaxHp*.2,missing)*.55+(missing==0?1:0),
            "DexterityPotion"=>blocks>=3?(boss?11:8):2,"SpeedPotion"=>blocks>=3?7:2,
            "WeakPotion"=>10,"PotionOfBinding"=>9+(physical?2:0),"BeetleJuice"=>18,
            "GhostInAJar"=>22,"LuckyTonic"=>18,"ShipInABottle"=>12,"HeartOfIron"=>13,
            "ShacklingPotion"=>13,"Fortifier"=>blocks>=3?12:2,"FyshOil"=>blocks>=3?8:physical?6:2,
            "RegenPotion"=>Math.Min(15,missing)*.65+1,"Ambergris"=>18+Math.Min(missing,s.MaxHp*.5)*.5,
            "FirePotion"=>late?2:6,"ExplosiveAmpoule"=>late?1:boss?3:7,
            "PotionShapedRock"=>late?1:4,"PoisonPotion"=>boss?5:7,"PotionOfDoom"=>late?2:6,
            "StrengthPotion"=>physical?(boss?12:9):2,"FlexPotion"=>physical?8:1,
            "VulnerablePotion"=>physical?9:2,"GigantificationPotion"=>physical?14:3,
            "LiquidBronze"=>boss?7:6,"PowderedDemise"=>boss?14:10,
            "MazalethsGift"=>physical?(boss?18:12):2,
            "FocusPotion"=>orbs?(boss?17:12):0,
            "PotionOfCapacity"=>orbs?(s.Deck.Any(c=>DefectPolicy.Tag(c,"dark"))?14:9):0,
            "EssenceOfDarkness"=>s.Character=="Defect"?s.Deck.Any(c=>c.ClassName is "MultiCast" or "Quadcast" or "Darkness")?18:10:0,
            "EnergyPotion"=>6+Math.Min(4,expensive),"RadiantTincture"=>10+Math.Min(5,expensive),
            "CureAll"=>12,"SwiftPotion"=>9,"Clarity"=>12,"BottledPotential"=>13,
            "DropletOfPrecognition"=>16,"LiquidMemories"=>16,"Duplicator"=>15,
            "GamblersBrew"=>8+Math.Min(5,statuses),"StableSerum"=>v.Has("RUNIC_PYRAMID")?0:7,
            "BlessingOfTheForge"=>ApotheosisPolicy.Finished(s)?1:Math.Min(12,upgraded*.8),
            "Ashwater"=>5+Math.Min(9,statuses*2+v.Exhaust),"GlowwaterPotion"=>12+Math.Min(6,statuses),
            "TouchOfInsanity"=>expensive>0?15:2,
            "SneckoOil"=>s.Deck.Count(c=>c.Cost<=1)>s.Deck.Length*.7?2:12,
            "DistilledChaos"=>10,"PowerPotion"=>boss?13:9,"AttackPotion"=>late?5:8,"SkillPotion"=>9,
            "ColorlessPotion"=>10,"CosmicConcoction"=>13,"OrobicAcid"=>14,
            "FruitJuice"=>7+Math.Min(5,v.Horizon*2),
            "SoldiersStew"=>Math.Min(20,s.Deck.Count(c=>c.ClassName.Contains("Strike"))*3),
            "CunningPotion"=>physical?10:6,"PotOfGhouls"=>12,
            "BoneBrew"=>s.Character=="Necrobinder"?12:0,
            "KingsCourage"=>s.Character=="Regent"?12:0,"StarPotion"=>s.Character=="Regent"?8:0,
            "DeprecatedPotion"=>0,_=>null
        };
        return value.HasValue?new(value.Value,defense,
            p.Name is "EntropicBrew" or "FairyInABottle"?"最高保留优先级：混沌补给或复活保障":
            p.Name=="FoulPotion"?"污浊药水仅按已知可达商店兑现机会估值":
            late&&p.Name is "ExplosiveAmpoule" or "FirePotion" or "PotionShapedRock"?"最终首领前固定少量伤害收益很低":
            value==0?"当前角色或牌组无法有效利用此药水":
            "按原生效果、牌组配合及当前阶段估值；不是战斗胜率"):null;
    }
    public static double PurchaseScore(RunSnapshot s,PotionCandidate p,int price)
    {
        if(price<0||s.Relics.Contains("RELIC.SOZU")||!s.PotionSlots.Any(x=>x==null))return double.NegativeInfinity;
        var model=Evaluate(s,p);if(model==null||model.Value<=0)return double.NegativeInfinity;
        return model.Value-price*.08*(s.Gold<150?1.3:1);
    }
    public static int Rank(RunSnapshot s,PotionCandidate p)
    {
        var model=Evaluate(s,p);if(model==null)return -1;
        if(p.Name=="FoulPotion")return CourierPolicy.MayReplace(s)?450:0;
        if(p.Name is "EntropicBrew" or "FairyInABottle")return 500;
        if(model.Value<=0)return 0;
        if(model.Value<4)return 10+(int)model.Value; // Poor fit cannot displace a useful potion solely by rarity.
        var tier=p.Rarity switch{"Common"=>100,"Uncommon"=>200,"Rare"=>300,"Event"=>300,"Token"=>100,_=>-1};
        if(tier<0)return -1;
        return tier+(model.Defense?30:0)+(int)Math.Clamp(model.Value,0,29);
    }
    public static int Replacement(RunSnapshot s,IReadOnlyList<PotionCandidate> belt,PotionCandidate incoming)
    {
        if(s.Relics.Contains("RELIC.SOZU"))return -1;
        var rank=Rank(s,incoming);if(rank<=0)return -1;
        var weakest=-1;
        for(var i=0;i<belt.Count;i++)
        {
            var current=Rank(s,belt[i]);if(current<0||current>=rank)continue;
            if(weakest<0||current<Rank(s,belt[weakest]))weakest=i;
        }
        return weakest;
    }
}
