using System.Text.Json;
using SpireStrategist.Core;

static class ItemModelTests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string name,Action action)=>tests.Add(("Item models: "+name,action));
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        CardSnapshot C(string name)=>HeroPolicy.Card(name)??SilentPolicy.Card(name)??DefectPolicy.Card(name)??new(){ClassName=name,Name=name,Type="Attack",Rarity="Basic",Cost=1};
        RunSnapshot S(string hero,params string[] cards)=>new(){Character=hero,Hp=50,MaxHp=75,Act=2,Gold=13,Deck=cards.Select(C).ToArray(),PotionSlots=[null,null]};
        double R(string id,RunSnapshot s)=>RelicValue.Evaluate(id,s).Value;
        double P(string id,RunSnapshot s)=>PotionPolicy.Evaluate(s,new(id,"Rare",true))!.Value;
        foreach(var hero in new[]{"Ironclad","Silent","Defect","Regent","Necrobinder"})
        Test(hero+" shows unaffordable card scores without changing purchase plan",()=>{
            var name=hero switch{"Ironclad"=>"Offering","Silent"=>"BladeDance","Defect"=>"Hologram","Regent"=>"Furnace",_=>"Bodyguard"};
            var s=S(hero,"Strike"+hero,"Defend"+hero);var stock=new[]{new ShopCard("card",C(name),75)};
            var plan=ShopPolicy.Evaluate(s,stock,150,13);var review=ShopReview.IncludeCards(s,stock,plan);
            Check(review.Recommendation==null&&s.Gold==13,"cannot buy");var item=review.Choices.Single(c=>c.Key=="card");
            Check(item.IntrinsicValue is {} v&&double.IsFinite(v)&&!item.Affordable&&!item.Eligible,"score survives budget exclusion");
            var rich=s with{Gold=300};var normal=ShopPolicy.Evaluate(rich,stock,75,300);
            Check(ShopReview.IncludeCards(rich,stock,normal).Recommendation?.Key==normal.Recommendation?.Key,"display enrichment must not change executable decision");
        });
        Test("unknown and multiplayer cards are displayed without invented scores",()=>{
            var s=S("Regent");var cards=new[]{new ShopCard("unknown",C("ModdedUnknown"),50),new ShopCard("mp",C("Constellation"),70)};
            var plan=ShopReview.IncludeCards(s,cards,new(null,[],0,[]));
            Check(plan.Choices.All(c=>!c.Modeled&&c.IntrinsicValue==null&&!c.Eligible),"unmodeled is not zero strength");
        });
        Test("all forty native role relics and fifteen role potions are explicitly modeled",()=>{
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/native-items.json")));
            var roles=new[]{"Ironclad","Silent","Defect","Regent","Necrobinder"};var relicCount=0;var potionCount=0;
            foreach(var item in json.RootElement.GetProperty("relics").EnumerateArray())
            {
                var hero=roles.FirstOrDefault(h=>item.GetProperty("pool").GetString()==h+"RelicPool");if(hero==null)continue;
                var id=item.GetProperty("id").GetString()!;var model=CharacterItems.Relic(id,S(hero,"Strike"+hero,"Defend"+hero));
                Check(model is{Supported:true}&&double.IsFinite(model.Value),id);relicCount++;
            }
            foreach(var item in json.RootElement.GetProperty("potions").EnumerateArray())
            {
                var hero=roles.FirstOrDefault(h=>item.GetProperty("pool").GetString()==h+"PotionPool");if(hero==null)continue;
                var name=item.GetProperty("className").GetString()!;var model=CharacterItems.Potion(S(hero),new(name,item.GetProperty("rarity").GetString()!,true));
                Check(model!=null&&double.IsFinite(model.Value),name);potionCount++;
            }
            Check(relicCount==40&&potionCount==15,$"coverage {relicCount}/{potionCount}");
        });
        Test("same character items distinguish actual builds",()=>{
            var shivs=S("Silent","BladeDance","Accuracy","CloakAndDagger");var poison=S("Silent","DeadlyPoison","Catalyst","BouncingFlask");
            Check(R("HELICAL_DART",shivs)>R("HELICAL_DART",poison),"shiv relic needs shivs and block");
            Check(R("SNECKO_SKULL",poison)>R("SNECKO_SKULL",shivs),"poison relic needs poison");
            var sword=S("Regent","Furnace","SwordSage");var star=S("Regent","FallingStar","Venerate","BlackHole");
            Check(P("KingsCourage",sword)>P("KingsCourage",star),"forge potion prefers blade outlets");
            Check(P("StarPotion",star)>P("StarPotion",sword),"stars need consumption");
            Check(R("BONE_FLUTE",S("Necrobinder","Unleash","Bodyguard"))>R("BONE_FLUTE",S("Necrobinder","ReaperForm","SoulStorm")),"player or soul damage is not pet attack");
            Check(R("DEMON_TONGUE",S("Ironclad","Offering","Bloodletting"))>R("DEMON_TONGUE",S("Ironclad","StrikeIronclad")),"self damage support");
        });
        Test("rare poorly fitted potion does not displace a useful defensive potion",()=>{
            var s=S("Defect","Claw","AllForOne","Reprogram");
            Check(PotionPolicy.Replacement(s,[new("BlockPotion","Common",true)],new("EssenceOfDarkness","Rare",true))==-1,"rarity alone must not override poor fit");
            Check(PotionPolicy.Rank(s,new("FairyInABottle","Rare",true))==500,"revival stays highest");
            Check(PotionPolicy.Rank(s,new("EntropicBrew","Rare",true))==500,"refill stays highest");
        });
        Test("ancients respect actual targets and resource restrictions",()=>{
            var blocks=S("Necrobinder","Bodyguard","PullAggro","DefendNecrobinder");
            Check(R("PAELS_CLAW",blocks)==R("PAELS_CLAW",S("Necrobinder","DefendNecrobinder")),"only Defend is enchanted");
            Check(R("PAELS_CLAW",S("Necrobinder","UltimateDefend"))>0,"native Ultimate Defend also carries the Defend tag");
            Check(R("NUTRITIOUS_SOUP",S("Defect","AdaptiveStrike"))==0,"native soup only enchants basic Strike");
            Check(R("FIDDLE",S("Necrobinder","CaptureSpirit","Reap","SoulStorm"))<R("FIDDLE",S("Necrobinder","DefendNecrobinder")),"soul drawing conflicts with Fiddle");
            Check(R("VELVET_CHOKER",S("Silent","BladeDance","BladeDance","Acrobatics"))<R("VELVET_CHOKER",S("Silent","Dash")),"six-card cap constrains knife cycling");
            Check(R("DUSTY_TOME",S("Defect","Darkness","Coolheaded","MultiCast"))>R("DUSTY_TOME",S("Defect","Claw","Reprogram","AllForOne")),"actual ancient power must fit build");
            foreach(var id in new[]{"BIIIG_HUG","PRESERVED_FOG"})Check(RelicValue.Evaluate(id,blocks,true).Selection=="remove","selection contract retained");
        });
        Test("shop screenshot shared relics have mechanism assessments",()=>{
            foreach(var id in new[]{"LIZARD_TAIL","JOSS_PAPER","ORRERY"})Check(RelicValue.Shop(id,S("Regent","Furnace")) is{Supported:true},id);
            Check(R("JOSS_PAPER",S("Silent","BladeDance","BladeDance"))>R("JOSS_PAPER",S("Silent","Dash")),"exhaust frequency not role name");
        });
        Test("new item explanations and unaffordable score labels translate to English",()=>{
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/native-items.json")));
            var s=S("Regent","Furnace","FallingStar");
            foreach(var item in json.RootElement.GetProperty("relics").EnumerateArray())
            {
                var id=item.GetProperty("id").GetString()!;var model=CharacterItems.Relic(id,s)??AncientItems.Evaluate(id,s);
                if(model!=null)Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(model.Note),"[\\u4e00-\\u9fff]"),id);
            }
            foreach(var item in json.RootElement.GetProperty("potions").EnumerateArray())
            {
                var name=item.GetProperty("className").GetString()!;var model=CharacterItems.Potion(s,new(name,"Common",true));
                if(model!=null)Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(model.Reason),"[\\u4e00-\\u9fff]"),name);
            }
            Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English("卡牌边际评分：2.5 · 买不起"),"[\\u4e00-\\u9fff]"),"score label");
            foreach(var hero in new[]{"Regent","Necrobinder"})Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(HeroPolicy.Label(hero)+" · 建议与接管"),"[\\u4e00-\\u9fff]"),hero+" status title");
            foreach(var line in new[]{"Reave：6.0 / 48 金币 · 买不起","无色牌在亡灵契约师构筑中的净收益先验（已扣抽牌与启动成本）","亡灵契约师策略：买牌与删牌直接比较，普通遗物沿用预算购买。"})
                Check(!System.Text.RegularExpressions.Regex.IsMatch(Localization.English(line),"[\\u4e00-\\u9fff]"),line);
        });
    }
}
