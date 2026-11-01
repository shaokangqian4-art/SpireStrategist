namespace SpireStrategist.Core;

// Counterfactual investment, not combat strength. Mutually exclusive pages/paths
// stay separate. Use the shared native RNG-copy reader and its validity gates.
public static class SilentForecast
{
    public sealed record Investment(double Value,string Evidence);
    public static Investment Evaluate(RunSnapshot s,CardSnapshot candidate)
    {
        if(s.Character!="Silent"||s.IsMultiplayer||s.Foresight is not {SourceUnchanged:true,Branches.Length:>0} f)return new(0,"");
        var a=SilentPolicy.Analyze(s);
        bool Allowed(CardSnapshot c)=>!MultiplayerAdvice.Ignore(c)&&!CardCatalog.HasUnmodelledModification(c)&&SilentPolicy.Profiles.ContainsKey(c.ClassName)
            &&!SilentMechanics.OffPlan(a,c);
        if(!Allowed(candidate)||s.Deck.Any(c=>c.ClassName==candidate.ClassName))return new(0,"");
        var now=Value(s.Deck.Append(candidate))-Value(s.Deck);double best=0;var evidence="";
        foreach(var branch in f.Branches.Take(8))
        {
            var pages=branch.Rewards.Take(3).ToArray();
            void Search(int i,List<CardSnapshot> selected,List<string> steps,double reliability,int gold)
            {
                if(selected.Count>0)
                {
                    var gain=Math.Max(0,Value(s.Deck.Append(candidate).Concat(selected))-Value(s.Deck.Concat(selected))-now)*reliability;
                    if(gain>best){best=gain;evidence=string.Join(" → ",steps);}
                }
                if(i==pages.Length)return;
                var page=pages[i];Search(i+1,selected,steps,reliability,gold);
                for(var k=0;k<Math.Min(8,page.Cards.Length);k++)
                {
                    var c=page.Cards[k];if(!Allowed(c))continue;
                    var price=page.Room=="Shop"?page.Prices is {} prices&&k<prices.Length?prices[k]:int.MaxValue:0;
                    if(price<0||price>gold)continue;
                    var trust=Math.Clamp(page.Reliability,0,1)/(1+.08*Math.Max(0,page.Distance-1));
                    selected.Add(c);steps.Add($"{page.Room} {page.Node}：{c.Name}"+(price>0?$" ({price}G)":""));
                    Search(i+1,selected,steps,Math.Min(reliability,trust),gold-price);
                    selected.RemoveAt(selected.Count-1);steps.RemoveAt(steps.Count-1);
                }
            }
            Search(0,[],[],1,Math.Max(0,s.Gold));
        }
        return new(Math.Round(Math.Min(9,best),3),evidence);
    }
    private static double Value(IEnumerable<CardSnapshot> cards)
    {
        var d=cards.ToArray();int N(string tag)=>d.Count(c=>SilentPolicy.Tag(c,tag));
        bool H(string id)=>d.Any(c=>c.ClassName==id);
        var sources=Math.Min(3,N("shiv_source"));
        var knives=sources==0?0:3*sources+Math.Min(2,N("shiv_payoff"))*12;
        var seeds=Math.Min(3,d.Count(c=>SilentPolicy.Tag(c,"poison_seed")&&c.ClassName!="PoisonedStab"));
        var poison=seeds==0?0:seeds*4+(H("Accelerant")?16:0)+(H("Outbreak")?7:0)+(H("BubbleBubble")?4:0);
        var engine=Math.Min(3,N("discard"))*Math.Min(3,N("sly"))*1.2;
        var repeat=(H("SerpentForm")||H("Murder")||H("Speedster"))?Math.Min(4,N("draw")+N("shiv_source"))*3:0;
        return Math.Max(knives,poison)+Math.Min(6,engine)+repeat;
    }
}
