namespace SpireStrategist.Core;

// Bounded counterfactuals: one choice per page, one path, cumulative shop budget.
// Native snapshots must verify no RNG mutation; never grant future battle strength.
public static class HeroForecast
{
    public sealed record Investment(double Value,string Evidence);
    public static Investment Evaluate(RunSnapshot s,CardSnapshot candidate)
    {
        if(!HeroPolicy.Supports(s.Character)||s.IsMultiplayer||s.Foresight is not {SourceUnchanged:true,Branches.Length:>0} f)return new(0,"");
        bool Allowed(CardSnapshot c)=>!MultiplayerAdvice.Ignore(c)&&!CardCatalog.HasUnmodelledModification(c)&&HeroPolicy.Profiles.ContainsKey(c.ClassName);
        if(!Allowed(candidate)||s.Deck.Any(c=>c.ClassName==candidate.ClassName))return new(0,"");
        double Value(IEnumerable<CardSnapshot> cards)
        {
            var a=HeroPolicy.Analyze(s with{Deck=cards.ToArray(),Foresight=null});
            return a.DamageEngines*10+Math.Min(5,a.Immediate)+Math.Min(2,a.Defense);
        }
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
                if(i==pages.Length)return;var page=pages[i];Search(i+1,selected,steps,reliability,gold);
                for(var k=0;k<Math.Min(8,page.Cards.Length);k++)
                {
                    var c=page.Cards[k];if(!Allowed(c))continue;
                    var price=page.Room=="Shop"?page.Prices is {} p&&k<p.Length?p[k]:int.MaxValue:0;
                    if(price<0||price>gold)continue;
                    var trust=Math.Clamp(page.Reliability,0,1)/(1+.08*Math.Max(0,page.Distance-1));
                    selected.Add(c);steps.Add($"{page.Room} {page.Node}：{c.Name}"+(price>0?$" ({price}G)":""));
                    Search(i+1,selected,steps,Math.Min(reliability,trust),gold-price);selected.RemoveAt(selected.Count-1);steps.RemoveAt(steps.Count-1);
                }
            }
            Search(0,[],[],1,Math.Max(0,s.Gold));
        }
        return new(Math.Round(Math.Min(7,best),3),evidence);
    }
}
