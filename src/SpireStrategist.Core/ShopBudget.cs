namespace SpireStrategist.Core;

public static class ShopBudget
{
    public static int Reserve(RunSnapshot s,RouteNode[] nodes,string current,int normal=100)
    {
        // Only release cash near the final act's boss and with no reachable later shop.
        // Actual rows adapt to extended maps; act three is not hard-coded.
        if(s.Map is not {} map||s.Act!=map.TotalActs||map.BossRow-map.CurrentRow>4)return normal;
        var graph=nodes.ToDictionary(n=>n.Id);
        if(!graph.TryGetValue(current,out var root))return normal;
        var seen=new HashSet<string>{current};var queue=new Queue<string>(root.Children);
        var reachesBoss=false;
        while(queue.TryDequeue(out var id))
        {
            if(!seen.Add(id)||!graph.TryGetValue(id,out var n))continue;
            if(n.Kind=="Shop")return normal;
            if(n.Kind=="Boss")reachesBoss=true;
            foreach(var c in n.Children)queue.Enqueue(c);
        }
        return reachesBoss?0:normal;
    }
}
