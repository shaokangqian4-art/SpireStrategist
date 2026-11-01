namespace SpireStrategist.Core;

public static class CampRoutePolicy
{
    public static Dictionary<string,double> Risks(RunSnapshot s,DecisionOption[] options,RouteNode[] graph,string current,RouteEconomy economy)
    {
        var enabled=options.Where(o=>o.Enabled).ToArray();
        // Special actions and multiple actions/upgrades need their own transition model.
        if(s.Relics.Contains("RELIC.MINIATURE_TENT")||enabled.Any(o=>o.Kind is not ("heal" or "smith")||o.SmithCount!=1)
            ||graph.FirstOrDefault(n=>n.Id==current) is not {Kind:"RestSite",Children.Length:>0})return [];
        var heal=enabled.FirstOrDefault(o=>o.Kind=="heal");
        economy=economy with{CampHealing=heal?.HealAmount??0,CanRest=heal!=null,CanSmith=enabled.Any(o=>o.Kind=="smith"),CurrentCampEntered=true};
        if(s.Character is "Defect" or "Silent" or "Regent" or "Necrobinder")
            graph=graph.Select(n=>n.Id!=current&&n.Kind=="RestSite"?n with{Children=[]}:n).ToArray();
        var risks=enabled.ToDictionary(o=>o.Kind,o=>RoutePolicy.Evaluate(s,graph,[current],economy,o.Kind).Options[0].RiskPenalty);
        if(risks.Count==0)return risks;
        var min=risks.Values.Min();
        return risks.ToDictionary(p=>p.Key,p=>Math.Max(0,p.Value-min));
    }
}
