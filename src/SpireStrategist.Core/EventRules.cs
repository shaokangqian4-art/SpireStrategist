namespace SpireStrategist.Core;

public static class EventRules
{
    public static EventChoice Describe(string eventId,string key,string name,RunSnapshot s,IReadOnlyDictionary<string,double> vars)
    {
        var v=new StrategicValue(s);var o=key.Split('.').Last();var c=new EventChoice(key,name);
        double V(string k)=>vars.TryGetValue(k,out var n)?n:throw new InvalidOperationException("缺少事件变量 "+eventId+"."+k);
        EventChoice Enchant(string id,int amount=1,int count=1)=>c with{Benefit=v.BestEnchant(id,amount,count),Selection="enchant:"+id};
        EventChoice Combat(double reward)=>c with{Benefit=reward,Burden=(10+Math.Max(0,5-v.Attack)*4)*(1+2*(1-v.Ratio)),Notes=["事件战斗采用保守战损先验；尚未调用整场试战。"]};
        EventChoice Unknown()=>c with{Supported=false,Notes=["未核验的事件分支："+key]};
        // These are explicit option keys, never a keyword classifier over translated descriptions.
        return eventId switch
        {
            "ABYSSAL_BATHS"=>o switch{
                "ABSTAIN"=>c with{Heal=V("Heal")},"IMMERSE" or "LINGER"=>c with{MaxHp=V("MaxHp"),HpLoss=V("Damage"),Notes=["最大生命先增加再扣血；每次泡浴后按递增伤害重新判断。"]},"EXIT_BATHS"=>c,_=>Unknown()},
            "AMALGAMATOR"=>o switch{
                "COMBINE_STRIKES"=>c with{Benefit=10+Math.Max(0,5-v.Attack)*2+v.Multi,Selection="remove"},
                "COMBINE_DEFENDS"=>c with{Benefit=10+Math.Max(0,5-v.Block)*2,Selection="remove"},_=>Unknown()},
            "AROMA_OF_CHAOS"=>o switch{"LET_GO"=>c with{Transform=1,Selection="transform"},"MAINTAIN_CONTROL"=>c with{Upgrade=1,Selection="upgrade"},_=>Unknown()},
            "BATTLEWORN_DUMMY"=>o switch{"SETTING_1"=>c with{Potions=1},"SETTING_2"=>c with{Benefit=v.RandomUpgrade(2)*(v.Attack>=7?.9:.4)},"SETTING_3"=>c with{Benefit=v.Attack>=13?11:1,Notes=["限时伤害考验，未证明三回合击破时降低最高档奖励兑现率。"]},_=>Unknown()},
            "BRAIN_LEECH"=>o switch{"RIP"=>c with{HpLoss=V("RipHpLoss"),Benefit=7},"SHARE_KNOWLEDGE"=>c with{Benefit=5,Burden=3,Selection="add"},_=>Unknown()},
            "BUGSLAYER"=>o switch{"EXTERMINATION"=>c with{Benefit=4+v.Strength*3},"SQUASH"=>c with{Benefit=7-(s.Deck.Any(d=>d.ClassName is "Shockwave" or "Uppercut")?3:0)},_=>Unknown()},
            "BYRDONIS_NEST"=>o switch{"EAT"=>c with{MaxHp=V("MaxHp")},"TAKE"=>c with{Benefit=8,Burden=7,Notes=["孵化消耗营火机会，并在孵化前占用牌组。"]},_=>Unknown()},
            "COLORFUL_PHILOSOPHERS"=>o switch{"EQUALITY"=>c with{Benefit=-20},"DEFECT" or "IRONCLAD" or "NECROBINDER" or "REGENT" or "SILENT"=>c with{Benefit=o=="IRONCLAD"?8:0,Selection=o=="IRONCLAD"?"add":"skip-foreign",Notes=["跨角色奖励仅在原生允许跳过时放弃，不伪造跳过。"]},_=>Unknown()},
            "COLOSSAL_FLOWER"=>o switch{
                "EXTRACT_CURRENT_PRIZE_1"=>c with{Gold=35},"EXTRACT_CURRENT_PRIZE_2"=>c with{Gold=75},"EXTRACT_INSTEAD"=>c with{Gold=135},
                "REACH_DEEPER_1"=>c with{HpLoss=5,Gold=75},"REACH_DEEPER_2"=>c with{HpLoss=6,Gold=135},
                "POLLINOUS_CORE"=>c with{HpLoss=7,Benefit=9+Math.Max(0,3-v.Draw)*2},_=>Unknown()},
            "CRYSTAL_SPHERE"=>o switch{"PAYMENT_PLAN"=>c with{Benefit=14,Burden=v.Curse()},"UNCOVER_FUTURE"=>c with{Benefit=11,Gold=-V("UncoverFutureCost")},_=>Unknown()},
            "DENSE_VEGETATION"=>o switch{"REST"=>Combat(5) with{Heal=V("Heal")},"TRUDGE_ON"=>c with{Gold=V("Gold"),HpLoss=V("HpLoss")},"FIGHT"=>c,_=>Unknown()},
            "DOLL_ROOM"=>o switch{"RANDOM"=>c with{Benefit=3},"TAKE_SOME_TIME"=>c with{HpLoss=V("TakeTimeHpLoss"),Benefit=9},"EXAMINE"=>c with{HpLoss=V("ExamineHpLoss"),Benefit=12},_=>Unknown()},
            "DOORS_OF_LIGHT_AND_DARK"=>o switch{"DARK"=>c with{Remove=1,Selection="remove"},"LIGHT"=>c with{RandomUpgrade=(int)V("Cards")},_=>Unknown()},
            "DROWNING_BEACON"=>o switch{"BOTTLE"=>c with{Potions=1},"CLIMB"=>c with{MaxHp=-V("HpLoss"),Benefit=9*v.Horizon},_=>Unknown()},
            "ENDLESS_CONVEYOR"=>o switch{
                "OBSERVE_CHEF"=>c with{RandomUpgrade=1},"CAVIAR"=>c with{Gold=-V("Gold"),MaxHp=V("CaviarMaxHp")},
                "CLAM_ROLL"=>c with{Gold=-V("Gold"),Heal=V("ClamRollHeal")},"FRIED_EEL"=>c with{Gold=-V("Gold"),Benefit=2},
                "GOLDEN_FYSH"=>c with{Gold=V("GoldenFyshGold")},"JELLY_LIVER"=>c with{Gold=-V("Gold"),Transform=1,Selection="transform"},
                "SEAPUNK_SALAD"=>c with{Gold=-V("Gold"),Benefit=2+v.Multi*2},"SPICY_SNAPPY"=>c with{Gold=-V("Gold"),RandomUpgrade=1},
                "SUSPICIOUS_CONDIMENT"=>c with{Gold=-V("Gold"),Potions=1},"LEAVE" or "LOCKED"=>c,_=>Unknown()},
            "FIELD_OF_MAN_SIZED_HOLES"=>o switch{"ENTER_YOUR_HOLE"=>Enchant("PERFECT_FIT"),"RESIST"=>c with{Remove=2,Burden=18,Selection="remove",Notes=["常态限制出牌；不能仅把两次删牌减一次诅咒等同无负面删一张。"]},_=>Unknown()},
            "GRAVE_OF_THE_FORGOTTEN"=>o switch{"ACCEPT"=>c with{Benefit=RelicValue.Evaluate("FORGOTTEN_SOUL",s).Value},"CONFRONT"=>Enchant("SOULS_POWER") with{Burden=v.Curse()},_=>Unknown()},
            "HUNGRY_FOR_MUSHROOMS"=>o switch{"BIG_MUSHROOM"=>c with{MaxHp=20,Burden=20},"FRAGRANT_MUSHROOM"=>c with{HpLoss=15,RandomUpgrade=2},_=>Unknown()},
            "INFESTED_AUTOMATON"=>o switch{"STUDY"=>c with{Benefit=3+Math.Max(0,2-v.Powers)},"TOUCH_CORE"=>c with{Benefit=3},_=>Unknown()},
            "JUNGLE_MAZE_ADVENTURE"=>o switch{"JOIN_FORCES"=>c with{Gold=V("JoinForcesGold")},"SOLO_QUEST"=>c with{Gold=V("SoloGold"),HpLoss=V("SoloHp")},_=>Unknown()},
            "LOST_WISP"=>o switch{"CLAIM"=>c with{Benefit=v.Powers*4,Burden=v.Curse()},"SEARCH"=>c with{Gold=V("Gold")},_=>Unknown()},
            "LUMINOUS_CHOIR"=>o switch{"OFFER_TRIBUTE"=>c with{Gold=-V("Gold"),Benefit=10},"REACH_INTO_THE_FLESH"=>c with{Remove=2,Burden=10,Selection="remove"},_=>Unknown()},
            "MORPHIC_GROVE"=>o switch{"GROUP"=>c with{Gold=-s.Gold,Transform=2,Selection="transform"},"LONER"=>c with{MaxHp=V("MaxHp")},_=>Unknown()},
            "POTION_COURIER"=>o switch{"GRAB_POTIONS"=>CourierPolicy.Describe(c,s,(int)V("FoulPotions")),"RANSACK"=>c with{Potions=1,Benefit=2},_=>Unknown()},
            "PUNCH_OFF"=>o switch{"I_CAN_TAKE_THEM"=>Combat(20),"NAB"=>c with{Benefit=10,Burden=v.Curse()},"FIGHT"=>c,_=>Unknown()},
            "RANWID_THE_ELDER"=>o switch{"GOLD"=>c with{Gold=-V("Gold"),Benefit=10},"POTION"=>c with{Benefit=10,Burden=5},"RELIC"=>c with{Benefit=20,Burden=12},_=>Unknown()},
            "REFLECTIONS"=>o switch{"SHATTER"=>c with{Benefit=2,Burden=8+(s.Deck.Length<15?8:0)+s.Deck.Count(d=>d.Keywords.Contains("Innate"))*3},"TOUCH_A_MIRROR"=>c with{RandomUpgrade=4,Burden=s.Deck.Where(d=>d.UpgradeLevel>0).Take(2).Count()*6},_=>Unknown()},
            "RELIC_TRADER"=>o is "TOP" or "MIDDLE" or "BOTTOM"?c with{Supported=false,Notes=["需要读取交换两端的实际遗物。"]}:Unknown(),
            "ROOM_FULL_OF_CHEESE"=>o switch{"GORGE"=>c with{Benefit=3,Selection="add"},"SEARCH"=>c with{HpLoss=V("Damage"),Benefit=8*v.Horizon},_=>Unknown()},
            "ROUND_TEA_PARTY"=>o switch{"ENJOY_TEA"=>c with{Heal=s.MaxHp,Burden=10*v.Horizon},"PICK_FIGHT"=>c with{HpLoss=V("Damage"),Benefit=10},"CONTINUE_FIGHT"=>c,_=>Unknown()},
            "SAPPHIRE_SEED"=>o switch{"EAT"=>c with{Heal=V("Heal"),Upgrade=1,Selection="upgrade"},"PLANT"=>Enchant("SOWN"),_=>Unknown()},
            "SELF_HELP_BOOK"=>o switch{"READ_THE_BACK"=>Enchant("SHARP",2),"READ_PASSAGE"=>Enchant("NIMBLE",2),"READ_ENTIRE_BOOK"=>Enchant("SWIFT",2),"NO_OPTIONS"=>c,_=>Unknown()},
            "SLIPPERY_BRIDGE"=>o=="OVERCOME"?c with{Supported=false,Notes=["需要读取实际待移除卡牌。"]}:o.StartsWith("HOLD_ON")?c with{HpLoss=V("HpLoss"),Benefit=v.RemoveBest(1)*.5}:Unknown(),
            "SPIRALING_WHIRLPOOL"=>o switch{"DRINK"=>c with{Heal=V("Heal")},"OBSERVE"=>Enchant("SPIRAL"),"REACH_IN"=>c with{Benefit=10,Burden=16},_=>Unknown()},
            "SPIRIT_GRAFTER"=>o switch{"LET_IT_IN"=>c with{Heal=V("LetItInHealAmount"),Benefit=2},"REJECTION"=>c with{HpLoss=V("RejectionHpLoss"),Upgrade=1,Selection="upgrade"},_=>Unknown()},
            "STONE_OF_ALL_TIME"=>o switch{"LIFT"=>c with{MaxHp=V("DrinkMaxHpGain"),Burden=5},"PUSH"=>Enchant("VIGOROUS",(int)V("PushVigorousAmount")) with{HpLoss=V("PushHpLoss")},_=>Unknown()},
            "SUNKEN_STATUE"=>o switch{"DIVE_INTO_WATER"=>c with{Gold=V("Gold"),HpLoss=V("HpLoss")},
                "GRAB_SWORD"=>c with{Benefit=(.5+Math.Min(2,v.Multi*.35))*Math.Min(1,v.Horizon),
                    Notes=["石中剑须再击败五场精英才变成加力量的玉石剑；当前没有即时战力。未证明能完成五场前，只给折扣后的期权价值；扣血取钱仍保留低血风险。"]},_=>Unknown()},
            "SUNKEN_TREASURY"=>o switch{"FIRST_CHEST"=>c with{Gold=V("SmallChestGold")},"SECOND_CHEST"=>c with{Gold=V("LargeChestGold"),Burden=v.Curse("Greed")-Math.Min(4,v.Exhaust)},_=>Unknown()},
            "SYMBIOTE"=>o switch{"APPROACH"=>Enchant("CORRUPTED"),"KILL_WITH_FIRE"=>c with{Transform=1,Selection="transform"},_=>Unknown()},
            "TABLET_OF_TRUTH"=>o switch{"SMASH"=>c with{Heal=V("SmashHPGain")},"GIVE_UP"=>c,"DECIPHER_1" or "DECIPHER"=>c with{MaxHp=-V("DecipherMaxHpLoss"),RandomUpgrade=key.Contains("DECIPHER_4")?s.Deck.Length:1},_=>Unknown()},
            "TEA_MASTER"=>o switch{"BONE_TEA"=>c with{Gold=-V("BoneTeaCost"),Benefit=4},"EMBER_TEA"=>c with{Gold=-V("EmberTeaCost"),Benefit=7+Math.Min(5,v.Multi)},"TEA_OF_DISCOURTESY"=>c with{Burden=5},_=>Unknown()},
            "THE_FUTURE_OF_POTIONS"=>o=="POTION"?c with{Benefit=7,Burden=5,Selection="add"}:Unknown(),
            "THE_LANTERN_KEY"=>o switch{"KEEP_THE_KEY"=>Combat(12*v.Horizon),"RETURN_THE_KEY"=>c with{Gold=key.Contains(".INITIAL.")?V("Gold"):0},"FIGHT"=>c,_=>Unknown()},
            "THE_LEGENDS_WERE_TRUE"=>o switch{"NAB_THE_MAP"=>c with{Benefit=14,Burden=6},"SLOWLY_FIND_AN_EXIT"=>c with{HpLoss=V("Damage"),Potions=1},_=>Unknown()},
            "THIS_OR_THAT"=>o switch{"ORNATE"=>c with{Benefit=10,Burden=8},"PLAIN"=>c with{HpLoss=V("HpLoss"),Gold=V("Gold")},_=>Unknown()},
            "TINKER_TIME"=>o switch{
                "CHOOSE_CARD_TYPE"=>c,"ATTACK"=>c with{Benefit=6+v.Strength*2},"SKILL"=>c with{Benefit=6+Math.Max(0,4-v.Block)},"POWER"=>c with{Benefit=5},
                "CHAOS"=>c with{Benefit=3},"CHOKING"=>c with{Benefit=6+v.Draw},"CURIOUS"=>c with{Benefit=v.Powers*3},"ENERGIZED"=>c with{Benefit=v.Energy*.6},
                "EXPERTISE"=>c with{Benefit=7+v.Multi},"IMPROVEMENT"=>c with{Benefit=v.RandomUpgrade(2)*v.Horizon},"SAPPING"=>c with{Benefit=8},"VIOLENCE"=>c with{Benefit=8+v.Strength*2},"WISDOM"=>c with{Benefit=9+Math.Max(0,3-v.Draw)},_=>Unknown()},
            "TRASH_HEAP"=>o switch{"DIVE_IN"=>c with{HpLoss=V("HpLoss"),Benefit=8},"GRAB"=>c with{Gold=V("Gold"),Benefit=-2},_=>Unknown()},
            "TRIAL"=>o switch{
                "ACCEPT"=>c,"REJECT"=>c with{Benefit=-10},"DOUBLE_DOWN"=>c with{Lethal=true},
                "GUILTY" when key.Contains("MERCHANT")=>c with{Benefit=20,Burden=v.Curse()},"INNOCENT" when key.Contains("MERCHANT")=>c with{Upgrade=2,Burden=v.Curse(),Selection="upgrade"},
                "GUILTY" when key.Contains("NOBLE")=>c with{Heal=10},"INNOCENT" when key.Contains("NOBLE")=>c with{Gold=300,Burden=v.Curse()},
                "GUILTY" when key.Contains("NONDESCRIPT")=>c with{Benefit=10,Burden=v.Curse()},"INNOCENT" when key.Contains("NONDESCRIPT")=>c with{Transform=2,Burden=v.Curse(),Selection="transform"},_=>Unknown()},
            "UNREST_SITE"=>o switch{"KILL"=>c with{MaxHp=-V("MaxHpLoss"),Benefit=10,
                    Preference=-Math.Max(0,V("MaxHpLoss")/Math.Max(1,s.MaxHp)-.12)*60,
                    PreferenceReason="生命上限较低时，扣除超过12%上限的额外长期风险"},
                "REST"=>UnrestRest(c,s,V("Heal"),V("MaxHpLoss")),_=>Unknown()},
            "WAR_HISTORIAN_REPY"=>o switch{"UNLOCK_CAGE"=>c with{Benefit=10+Math.Min(8,v.Attack)},"UNLOCK_CHEST"=>c with{Benefit=20,Potions=2},_=>Unknown()},
            "WATERLOGGED_SCRIPTORIUM"=>o switch{"BLOODY_INK"=>c with{MaxHp=V("MaxHp")},"TENTACLE_QUILL"=>Enchant("STEADY") with{Gold=-V("Gold")},"PRICKLY_SPONGE"=>Enchant("STEADY",1,2) with{Gold=-V("PricklySpongeGold")},_=>Unknown()},
            "WELCOME_TO_WONGOS"=>o switch{"BARGAIN_BIN"=>c with{Gold=-V("BargainBinCost"),Benefit=9},"FEATURED_ITEM"=>c with{Gold=-V("FeaturedItemCost"),Benefit=12},"MYSTERY_BOX"=>c with{Gold=-V("MysteryBoxCost"),Benefit=30*Math.Min(1,v.Remaining/10)},"LEAVE"=>c with{Burden=s.Deck.Any(d=>d.UpgradeLevel>0)?6:0},_=>Unknown()},
            "WELLSPRING"=>o switch{"BATHE"=>WellspringBathe(c,s),"BOTTLE"=>c with{Potions=1},_=>Unknown()},
            "WHISPERING_HOLLOW"=>o switch{"GOLD"=>c with{Gold=-V("Gold"),Potions=2},"HUG"=>c with{HpLoss=V("HpLoss"),Transform=1,Selection="transform"},_=>Unknown()},
            "WOOD_CARVINGS"=>o switch{"BIRD"=>c with{Benefit=6+v.Strength*2,Selection="transform"},"TORUS"=>c with{Benefit=5+Math.Max(0,4-v.Block),Selection="transform"},"SNAKE"=>Enchant("SLITHER"),_=>Unknown()},
            "ZEN_WEAVER"=>o switch{"ARACHNID_ACUPUNCTURE"=>c with{Gold=-V("ArachnidAcupunctureCost"),Remove=2,Selection="remove"},"EMOTIONAL_AWARENESS"=>c with{Gold=-V("EmotionalAwarenessCost"),Remove=1,Selection="remove"},"BREATHING_TECHNIQUES"=>c with{Gold=-V("BreathingTechniquesCost"),Benefit=s.Deck.Count(d=>d.Cost>=2)*2-4},_=>Unknown()},
            _=>Unknown()
        };
    }
    private static EventChoice WellspringBathe(EventChoice c,RunSnapshot s)
    {
        var v=new StrategicValue(s);
        // Guilty expires after five completed combats. Removing a basic is permanent.
        // These are explicit priors, not measured combat damage or win probabilities.
        var shortHorizon=s.Map is {Rows:>0} && v.Remaining<=5;
        var burden=(shortHorizon?5:2)+5*Math.Clamp((.4-v.Ratio)/.4,0,1);
        return c with{Remove=1,Burden=burden,Selection="remove",
            Notes=["愧疚在五场战斗后自动移除；普通血量、长路线优先兑现永久删牌收益。",
                $"临时占抽牌估值 {burden:F2}；低于40%血量时提高近期负担，剩余不超过五层时不预支到期收益。此为待校准策略，非战损预测。"]};
    }
    private static EventChoice UnrestRest(EventChoice c,RunSnapshot s,double heal,double maxLoss)
    {
        var v=new StrategicValue(s);
        var actual=Math.Clamp(heal,0,Math.Max(0,s.MaxHp-s.Hp));
        var nextDanger=s.NextNodeTypes.Any(n=>n is "Elite" or "Boss");
        var recent=s.RecentBattles.Where(b=>b.Act==s.Act).Select(b=>b.DamageTaken).DefaultIfEmpty(0).Max();
        var urgent=s.Hp<=Math.Max(12,s.MaxHp*.25)||s.MaxHp-maxLoss<40||
            nextDanger&&recent>0&&s.Hp<=recent*1.25;
        var insufficient=actual<.65*s.MaxHp&&!urgent;
        return c with{Heal=actual,Burden=10,Preference=insufficient?-v.Heal(actual):0,
            PreferenceReason="无休之处：非紧急且恢复不足最大生命65%，降低拿保留诅咒回血的偏好",
            Notes=[$"实际恢复 {actual:F0}/{s.MaxHp}（{actual/Math.Max(1,s.MaxHp):P0}）；紧急保命例外={urgent}。",
                "65%是用户指定的策略门槛，不是原生机制或已验证最优阈值。睡眠不佳会保留占手；后续火堆不是保证无伤抵达。"]};
    }
}
