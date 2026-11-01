namespace SpireStrategist.Core;

// Counterfactual construction only. Future cards never enter the live deck or readiness.
public static class DefectBuildForecast
{
    public sealed record Investment(double Value,string Evidence);
    public static Investment Evaluate(RunSnapshot s,CardSnapshot candidate)
    {
        if(s.Character!="Defect"||s.Foresight is not {SourceUnchanged:true,Branches.Length:>0} forecast)return new(0,"");
        if(s.Deck.Any(c=>c.ClassName==candidate.ClassName)&&candidate.ClassName!="Claw")return new(0,"");
        bool Allowed(CardSnapshot c)=> !(DefectDamagePlan.PhysicalCommitted(s)&&Orb(c.ClassName))
            &&!(DefectDamagePlan.OrbCommitted(s)&&Physical(c.ClassName))
            &&!(DefectDamagePlan.DarkCommitted(s)&&c.ClassName is "Storm" or "Thunder" or "Voltaic");
        if(!Allowed(candidate))return new(0,"");
        var now=Value(s.Deck.Append(candidate))-Value(s.Deck);
        var best=0d;var evidence="";
        foreach(var branch in forecast.Branches.Take(8))
        {
            var pages=branch.Rewards.Take(3).ToArray();
            void Search(int i,List<CardSnapshot> selected,List<string> steps,double reliability,int gold)
            {
                if(selected.Count>0)
                {
                    var without=Value(s.Deck.Concat(selected));
                    var with=Value(s.Deck.Append(candidate).Concat(selected));
                    var gain=Math.Max(0,with-without-now)*reliability;
                    if(gain>best){best=gain;evidence=string.Join(" → ",steps);}
                }
                if(i>=pages.Length)return;
                var page=pages[i];
                Search(i+1,selected,steps,reliability,gold);
                for(var k=0;k<Math.Min(8,page.Cards.Length);k++)
                {
                    var c=page.Cards[k];if(!Allowed(c))continue;
                    var price=page.Room=="Shop"?page.Prices is {} prices&&k<prices.Length?prices[k]:int.MaxValue:0;
                    if(price<0||price>gold)continue;
                    var discount=Math.Clamp(page.Reliability,0,1)/(1+.08*Math.Max(0,page.Distance-1));
                    selected.Add(c);steps.Add(page.Room+" "+page.Node+"："+c.Name+(price>0?$" ({price}G)":""));
                    // One selection per page; alternative route branches never merge.
                    Search(i+1,selected,steps,Math.Min(reliability,discount),gold-price);
                    selected.RemoveAt(selected.Count-1);steps.RemoveAt(steps.Count-1);
                }
            }
            Search(0,[],[],1,Math.Max(0,s.Gold));
        }
        return new(Math.Round(Math.Min(12,best),3),evidence);
    }
    private static bool Physical(string n)=>n is "AdaptiveStrike" or "Claw" or "AllForOne" or "Scrape" or "BulkUp" or "Feral" or "HelixDrill";
    private static bool Orb(string n)=>n is "Darkness" or "ConsumingShadow" or "MultiCast" or "Quadcast" or "Shatter" or "Storm" or "Thunder" or "Voltaic" or "Spinner" or "Defragment";
    private static double Value(IEnumerable<CardSnapshot> cards)
    {
        var deck=cards.ToArray();int N(params string[] names)=>deck.Count(c=>names.Contains(c.ClassName));
        var focus=Math.Min(2,N("Defragment","BiasedCognition"));
        var retrieval=Math.Min(2,N("AllForOne","Scrape","Feral","Hologram"));
        var claws=Math.Min(5,N("Claw"));
        var physical=(N("AdaptiveStrike")>0?8+10*retrieval:0);
        if(claws>0)physical=Math.Max(physical,3*claws+(claws>=2?8:0)+4*retrieval);
        if(physical>0)physical+=Math.Min(2,N("BulkUp"))*4;
        var dark=N("Darkness","ConsumingShadow")>0?8d:0;
        if(dark>0)dark+=Math.Min(1,N("MultiCast","Quadcast","Shatter"))*18+Math.Min(1,N("Loop"))*5+focus*4;
        var producers=N("Storm","Thunder");
        var lightning=producers>0?6d:0;
        if(lightning>0)lightning+=Math.Min(1,N("Voltaic"))*18+focus*4
            +(N("Storm")>0?Math.Min(3,deck.Count(c=>c.Type=="Power"&&c.ClassName!="Storm"))*3:0);
        var frost=N("Spinner")>0?6+focus*12:0;
        var sources=Math.Min(3,deck.Count(c=>DefectPolicy.Tag(c,"status_source")));
        var status=N("FlakCannon")>0?4+sources*6:0;
        // Damage plans compete; unrelated archetypes cannot inflate a single plan.
        return Math.Max(Math.Max(physical,dark),Math.Max(lightning,frost))+status;
    }
}
