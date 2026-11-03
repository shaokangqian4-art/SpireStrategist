using System.Text.Json;
using SpireStrategist.Core;

static class Regent0670Tests
{
    public static void Register(List<(string,Action)> tests)
    {
        void Test(string n,Action f)=>tests.Add(("Regent 0670: "+n,f));
        void Check(bool b,string why){if(!b)throw new Exception(why);}
        CardSnapshot C(string n,bool up=false)=>HeroPolicy.Card(n,up)!;
        RunSnapshot S(params string[] cards)=>new(){Character="Regent",Hp=60,MaxHp=75,Act=3,Energy=3,Relics=["RELIC.DIVINE_RIGHT"],Deck=cards.Select(n=>C(n)).ToArray()};
        double Score(RunSnapshot s,CardSnapshot c)=>new Policy().Evaluate(s,[new("c",c.Name,"card",Card:c),new("skip","skip","skip")]).Options[0].Score!.Value;
        var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        Test("real late filler declines, first shop Orbit becomes worth buying",()=>{
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/regent-0669-player-feedback.json")));
            foreach(var row in doc.RootElement.GetProperty("decisions").EnumerateArray()){
                var state=row.GetProperty("state").Deserialize<RunSnapshot>(json)!;
                var options=row.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
                var e=new Policy().Evaluate(state,options);
                if(state.Floor is 38 or 43)Check(options.Single(o=>o.Key==e.RecommendedKey).Kind=="skip","skip filler floor "+state.Floor);
                if(state.Floor==42)Check(options.Single(o=>o.Key==e.RecommendedKey).Card?.ClassName=="KnowThyPlace","preserve functional debuff");
            }
            var shop=doc.RootElement.GetProperty("shop");
            var s=shop.GetProperty("state").Deserialize<RunSnapshot>(json)!;
            var stock=shop.GetProperty("stock").Deserialize<ShopCard[]>(json)!;
            var plan=ShopPolicy.Evaluate(s,stock,200,s.Gold);
            Check(plan.Recommendation?.Key==stock.Single(c=>c.Card.ClassName=="Orbit").Key,"buy first Orbit rather than other cards or removal");
        });
        Test("Orbit is capacity limited, stars do not refund energy",()=>{
            var paid=S("StrikeRegent","GatherLight","GuidingStar","Glimmer");
            Check(RegentOrbit.Refund(paid,2,1)>0,"ordinary paid plays already refund");
            Check(RegentOrbit.Refund(paid,2,1)<RegentOrbit.Refund(paid,2,2),"independent instances");
            Check(RegentOrbit.Refund(S("AstralPulse","Stardust"),6,2)==0,"star X is not energy X");
            Check(RegentOrbit.Refund(paid,20,8)<=6,"no assumed infinite");
            var orbit=paid with{Deck=[..paid.Deck,C("Orbit")]};
            Check(HeroStrategy.Upgrade(orbit,C("Orbit"))>HeroStrategy.Upgrade(orbit,C("SolarStrike")),"Orbit cost reduction is a priority");
            Check(HeroPolicy.Analyze(orbit).DamageEngines==HeroPolicy.Analyze(paid).DamageEngines,"energy not an output engine");
            Check(Score(orbit,C("Orbit"))<Score(paid,C("Orbit")),"strong does not mean endless duplicates");
        });
        Test("reviewed priors preserve resources and supported exceptions",()=>{
            var s=S("GatherLight","Glow","GuidingStar","Glimmer","Furnace","Parry","SummonForth");
            Check(Score(s,C("Tyranny"))>Score(s,C("Prophesize")),"premium engine over expensive plain draw");
            Check(Score(s,C("BigBang"))>Score(s,C("Hegemony")),"strong support over filler");
            Check(Score(s,C("Devastate"))<0,"high star single hit not free damage");
            Check(Score(s,C("SevenStars"))<0,"unfunded seven stars not a core by rarity");
            Check(HeroStrategy.Remove(s with{Deck=[C("Venerate"),C("SevenStars")]},C("Venerate"))<0,"low acquisition prior is not removal of sole supply");
        });
        Test("multi hit enchants, relic triggers and vigor share payable hits",()=>{
            var s=S("CelestialMight","GatherLight","Glimmer","GuidingStar");
            var multi=C("CelestialMight");var single=C("KinglyPunch");
            foreach(var e in new[]{"SHARP","MOMENTUM"})Check(new StrategicValue(s).Enchant(multi,e,2)>new StrategicValue(s).Enchant(single,e,2),e+" multiplies actual hits");
            Check(RegentAttackLinks.Hits(S("StrikeRegent") with{Relics=[]},C("SevenStars"))==0,"unfunded star attack cannot promise seven hits");
            var cannon=s with{Relics=[..s.Relics,"RELIC.MINIATURE_CANNON"]};
            Check(Score(cannon,multi)==Score(s,multi),"cannon does not boost unupgraded card now");
            Check(Score(cannon,C("CelestialMight",true))>Score(s,C("CelestialMight",true)),"upgraded cannon hits");
            Check(HeroStrategy.Upgrade(cannon,multi)>HeroStrategy.Upgrade(s,multi),"cannon informs smith target");
            var lighter=s with{Relics=[..s.Relics,"RELIC.MYSTIC_LIGHTER"]};
            Check(Score(lighter,multi)==Score(s,multi),"lighter requires enchantment");
            var enchanted=multi with{Enchantment="ENCHANTMENT.SHARP",EnchantmentAmount=2,Modified=true};
            Check(Score(lighter,enchanted)>Score(s,enchanted),"lighter on enchanted multi hit");
            Check(new StrategicValue(lighter).Enchant(multi,"SHARP",2)>new StrategicValue(s).Enchant(multi,"SHARP",2),"lighter informs target choice");
            Check(RelicValue.Shop("MYSTIC_LIGHTER",s with{Deck=[enchanted]})!.Value>RelicValue.Shop("MYSTIC_LIGHTER",s)!.Value,"shop uses owned enchanted outlet");
            Check(RegentAttackLinks.Adjust(s,C("Terraforming")).Sum(p=>p.Value)>RegentAttackLinks.Adjust(S("KinglyPunch"),C("Terraforming")).Sum(p=>p.Value),"vigor only next multi attack");
        });
    }
}
