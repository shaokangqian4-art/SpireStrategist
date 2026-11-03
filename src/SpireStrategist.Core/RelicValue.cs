namespace SpireStrategist.Core;

public sealed record RelicAssessment(double Value,string Selection="",string Note="人工机制估值，未完成训练校准。",bool Supported=true);
public static class RelicValue
{
    // Nullable shop assessment: do not turn the generic fallback into a strength recommendation.
    public static RelicAssessment? Shop(string id,RunSnapshot s)
    {
        if(CharacterItems.Relic(id,s) is {} native)return native;
        if(AncientItems.Evaluate(id,s) is {} ancient)return ancient;
        if(HeroRelics.Evaluate(id,s) is {} hero)return hero;
        if(SilentRelics.Evaluate(id,s) is {} silent)return silent;
        if(DefectRelics.Evaluate(id,s) is {} defect)return defect;
        id=id.Split('.').Last();var v=new StrategicValue(s);
        double? common=id switch{
            "BAG_OF_PREPARATION"=>10,"ICE_CREAM"=>12+Math.Min(8,s.Deck.Count(c=>c.Cost>=2)),
            "ANCHOR"=>5*v.Horizon,"BLOOD_VIAL"=>3*v.Horizon,"HAPPY_FLOWER"=>7*v.Horizon,
            "VAJRA"=>4+v.Multi*2,"ODDLY_SMOOTH_STONE"=>4+Math.Min(6,v.Block),
            "MUMMIFIED_HAND"=>4+v.Powers*2,"KUNAI" or "SHURIKEN"=>s.Deck.Count(c=>c.Type=="Attack"&&c.Cost<=1)>=5?14:4,
            "PEN_NIB"=>6+Math.Min(6,v.Attack),"ORICHALCUM"=>v.Block<3?9:3,
            "STRIKE_DUMMY"=>s.Deck.Count(c=>c.ClassName.Contains("Strike"))*1.5,
            _=>null};
        if(common.HasValue)return new(common.Value);
        var known=Evaluate(id,s);
        return known.Note=="普通遗物按通用先验估值，具体联动未完全建模。"?null:known;
    }
    public static RelicAssessment Evaluate(string id,RunSnapshot s,bool ancient=false)
    {
        if(CharacterItems.Relic(id,s) is {} native)return native;
        if(AncientItems.Evaluate(id,s) is {} specialized)return specialized;
        if(HeroRelics.Evaluate(id,s) is {} hero)return hero;
        if(SilentRelics.Evaluate(id,s) is {} silent)return silent;
        if(DefectRelics.Evaluate(id,s) is {} defect)return defect;
        id=id.Replace("RELIC.","");var v=new StrategicValue(s);
        double E=v.Energy,H=v.Horizon,D=v.Draw,X=v.Exhaust,P=v.Powers,B=v.Basics;
        return id switch
        {
            "FAKE_ANCHOR"=>new(3*H),"FAKE_BLOOD_VIAL"=>new(2.5*H),"FAKE_HAPPY_FLOWER"=>new(4*H),
            "FAKE_LEES_WAFFLE"=>new(v.Heal(s.MaxHp*.1)),"FAKE_MANGO"=>new(v.MaxHp(3)),
            "FAKE_ORICHALCUM"=>new((v.Block<3?4:2)*H),"FAKE_VENERABLE_TEA_SET"=>new(4*H),
            "FAKE_STRIKE_DUMMY"=>new(s.Deck.Count(c=>c.ClassName.Contains("Strike"))*.5*H),
            "FAKE_SNECKO_EYE"=>new(s.Deck.Sum(c=>c.CostsX?0:c.Cost-1.5)*1.4-(v.Has("RUNIC_PYRAMID")?20:0)),
            "ASTROLABE"=>new(v.TransformBest(3)+v.UpgradeBest(3)*.5,"transform"),
            "BLACK_STAR"=>new(9*H,"", "收益取决于后续实际精英数量；长地图提高机会，不保证每个精英可打。"),
            "CALLING_BELL"=>new(30-v.Curse("CurseOfTheBell")),
            "EMPTY_CAGE"=>new(v.RemoveBest(2),"remove"),
            "PANDORAS_BOX"=>new(B*4-(v.Block<3?7:0),"","变化非必然正收益，未预测到具体替换时扣除攻防失衡风险。"),
            "RUNIC_PYRAMID"=>new(15+X*2-Math.Max(0,s.Deck.Count(c=>c.Cost>=2)-3)*2),
            "SNECKO_EYE"=>new(12+s.Deck.Sum(c=>c.CostsX?0:c.Cost-1.5)*1.4-(v.Has("RUNIC_PYRAMID")?20:0)),
            "ECTOPLASM"=>new(E-10*H),"SOZU"=>new(E-4-Math.Min(6,s.PotionSlots.Length*1.5)),
            "PHILOSOPHERS_STONE"=>new(E-(v.Block<3?12:6)),
            "VELVET_CHOKER"=>new(E-Math.Min(30,D*3+X*2+s.Deck.Count(c=>c.Cost==0)*2)),
            "DUSTY_TOME"=>new(8+X*2+v.Profiles.Sum(p=>p.ExhaustPayoff)*3,"","按战士技能消耗引擎估值，长战耗尽技能仍有风险。"),
            "CURSED_PEARL"=>new(v.Gold(333)-v.Curse("Greed")),
            "DOWSING_ROD"=>new(3*H),"HEFTY_TABLET"=>new(9-v.Curse()),
            "LARGE_CAPSULE"=>new(20-10),"LEAFY_POULTICE"=>new(v.TransformBest(2)-v.MaxHp(12)),
            "NEOWS_BONES"=>new(14-v.Curse()-3),"NEOWS_SACRIFICE"=>new(v.Potion(1)+4-v.Curse("Guilt")),
            "PRECARIOUS_SHEARS"=>new(v.RemoveBest(2)-v.HpCost(16),"remove"),
            "SILKEN_TRESS"=>new(7-v.Gold(s.Gold)),"SILVER_CRUCIBLE"=>new(10-6),
            "ARCANE_SCROLL"=>new(5),"BOOMING_CONCH"=>new(9*H),"FISHING_ROD"=>new(v.RandomUpgrade((int)Math.Clamp(v.Remaining/8,1,6))),
            "GOLDEN_PEARL"=>new(v.Gold(150)),"KALEIDOSCOPE"=>new(-8,"","跨角色卡未完整适配，保守降低收益。"),
            "LEAD_PAPERWEIGHT"=>new(6),"LOST_COFFER"=>new(5+v.Potion(1)),"MASSIVE_SCROLL"=>new(-30),
            "NEOWS_TORMENT"=>new(7),"NEW_LEAF"=>new(v.TransformBest(1),"transform"),
            "PHIAL_HOLSTER"=>new(7+v.Potion(2)),"PRECISE_SCISSORS"=>new(v.RemoveBest(1),"remove"),
            "SCROLL_BOXES"=>new(5),"WINGED_BOOTS"=>new(13),"LAVA_ROCK"=>new(s.Act==1?12:0),
            "NEOWS_TALISMAN"=>new(4),"NUTRITIOUS_OYSTER"=>new(v.MaxHp(11)),
            "POMANDER"=>new(v.UpgradeBest(1),"upgrade"),"SMALL_CAPSULE"=>new(10),"STONE_HUMIDIFIER"=>new(4*H),
            "BLESSED_ANTLER"=>new(E-8+Math.Min(6,X*2)),"BRILLIANT_SCARF"=>new(4+Math.Min(12,D*2+X)),
            "DELICATE_FROND"=>new(s.PotionSlots.Length*5*(v.Has("SOZU")?0:1),"","不设五格药水栏的硬门槛；少栏位仍有每战补药价值。"),
            "DIAMOND_DIADEM"=>new(16+(v.Block<3?5:0)),"FUR_COAT"=>new(13*H),
            "GLITTER"=>new(7*H),"JEWELRY_BOX"=>new(ApotheosisPolicy.Value(s),Note:ApotheosisPolicy.Reason(s)),
            "LOOMING_FRUIT"=>new(v.MaxHp(31)),"SIGNET_RING"=>new(v.Gold(888)*Math.Min(1,H)),
            "BEAUTIFUL_BRACELET"=>new(12+Math.Max(0,3-D)*2),
            "ELECTRIC_SHRYMP"=>new(v.BestEnchant("IMBUED"),"enchant:IMBUED","不限 X 费技能；还需检查自动施放的时机及目标。"),
            "GLASS_EYE"=>new(18*H),"ALCHEMICAL_COFFER"=>new(22),"DRIFTWOOD"=>new(7*H),
            "RADIANT_PEARL"=>new(13),"SAND_CASTLE"=>new(v.RandomUpgrade(6)),
            "TOUCH_OF_OROBAS"=>new(v.Has("BURNING_BLOOD")?18*H:0),"ARCHAIC_TOOTH"=>IroncladSynergies.ArchaicTooth(s),
            "SEA_GLASS"=>new(-5),"PRISMATIC_GEM"=>new(E-10),
            "PAELS_FLESH"=>new(E*.45),"PAELS_HORN"=>new(9-Math.Max(0,D-4)),"PAELS_TEARS"=>new(7),
            "PAELS_WING"=>new(10*H),"PAELS_EYE"=>new(9+Math.Min(5,X)),"PAELS_BLOOD"=>new(12+Math.Max(0,3-D)*2),
            "PAELS_CLAW"=>new(s.Deck.Count(c=>c.ClassName=="DefendIronclad")*(2+Math.Min(2,X))),
            "PAELS_TOOTH"=>PaelsToothPolicy.Evaluate(s),
            "PAELS_LEGION"=>new(8+Math.Min(10,v.Block*1.5)),"PAELS_GROWTH"=>new(v.BestEnchant("CLONE"),"enchant:CLONE"),
            "CLAWS"=>new(Math.Min(6,s.Deck.Count(c=>c.ClassName=="StrikeIronclad"))*3,"transform"),
            "CROSSBOW"=>new(12),"IRON_CLUB"=>new(9+Math.Min(7,D)),"MEAT_CLEAVER"=>new(Math.Min(20,B*3)*Math.Min(1,H)),
            "SAI"=>new(16+(v.Block<3?5:0)),"SPIKED_GAUNTLETS"=>new(E-P*12),
            "TANXS_WHISTLE"=>new(10),"THROWING_AXE"=>new(12),"WAR_HAMMER"=>new(v.RandomUpgrade(4)*Math.Min(2,H)),
            "TRI_BOOMERANG"=>new(v.BestEnchant("INSTINCT",1,3),"enchant:INSTINCT"),
            "VERY_HOT_COCOA"=>new(12+Math.Min(8,s.Deck.Count(c=>c.Cost>=2))),
            "YUMMY_COOKIE"=>new(v.UpgradeBest(4),"upgrade"),
            "BIIIG_HUG"=>new(v.RemoveBest(4)-(s.Deck.Length<16?16:7)+Math.Min(5,X),"remove"),
            "STORYBOOK"=>StorybookPolicy.Evaluate(s),"TOASTY_MITTENS"=>new(8+v.Multi*2+Math.Min(6,X)),
            "GOLDEN_COMPASS"=>new(7,"","替换地图后重新读真实节点，不按旧地图兑现收益。"),
            "PUMPKIN_CANDLE"=>new(E*.65),"TOY_BOX"=>new(18),
            "SEAL_OF_GOLD"=>new(E*Math.Clamp(s.Gold/180d,0,1)-v.Gold(Math.Min(s.Gold,60))),
            "NUTRITIOUS_SOUP"=>new(s.Deck.Count(c=>c.ClassName=="StrikeIronclad")*3-(X>3?3:0)),
            "BLOOD_SOAKED_ROSE"=>new(E-v.EnthralledCost(),"","执迷不能永久删除，留在手牌时阻止正常打牌；小牌组与高过牌反复支付解锁费用。仅已核验的自动消耗降低负担。"),"WHISPERING_EARRING"=>new(E-17),
            "FIDDLE"=>new(16-D*6),"PRESERVED_FOG"=>new(v.RemoveBest(3)-16,"remove"),
            "SERE_TALON"=>new(15-v.MaxHp(9)),"DISTINGUISHED_CAPE"=>new(20-2*v.Curse()),
            "CHOICES_PARADOX"=>new(10),"MUSIC_BOX"=>new(8+Math.Min(8,v.Attack)),
            "LORDS_PARASOL"=>new(12*Math.Min(2,H)-6,"","整店物品同时有收益与强制加牌负担；不是无条件差选项。"),
            "JEWELED_MASK"=>new(P>0?12+Math.Min(8,P*2):0),
            "DAUGHTER_OF_THE_WIND"=>new(7+Math.Min(7,s.Deck.Count(c=>c.Type=="Attack"))),
            "MR_STRUGGLES"=>new(7),"BING_BONG"=>new(-8+Math.Min(10,s.Deck.Count(c=>c.ClassName=="PerfectedStrike")*3)),
            "FORGOTTEN_SOUL"=>new(3+Math.Min(12,X*3)),"CHOSEN_CHEESE"=>new(s.Character=="Defect"?DefectStrategy.CheeseValue(s):8*H),
            _=>new(8,"",ancient?"先古遗物未适配。":"普通遗物按通用先验估值，具体联动未完全建模。",!ancient)
        };
    }
}
