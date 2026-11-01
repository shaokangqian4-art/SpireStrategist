using System.Text.Json;
using System.Text.Encodings.Web;
using SpireStrategist.Core;

if(args.Length!=3)throw new ArgumentException("DiagnoseRun log-directory seed output.json");
var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
var offers=new List<object>();var decisions=new List<object>();var routes=new List<object>();
foreach(var file in Directory.GetFiles(args[0],"session-*.jsonl").Order())
{
    var records=File.ReadLines(file).Where(l=>l.Length>0).Select(l=>JsonDocument.Parse(l)).ToArray();
    try
    {
        var choices=records.Where(r=>r.RootElement.GetProperty("type").GetString()=="choice")
            .Select(r=>r.RootElement.GetProperty("payload")).ToArray();
        RunSnapshot? latest=null;
        foreach(var record in records)
        {
            var root=record.RootElement;var type=root.GetProperty("type").GetString();var payload=root.GetProperty("payload");
            if(type=="state")latest=payload.GetProperty("state").Deserialize<RunSnapshot>(json);
            if(type=="decision")
            {
                var state=payload.GetProperty("state").Deserialize<RunSnapshot>(json)!;
                var input=payload.GetProperty("options").Deserialize<DecisionOption[]>(json)!;
                var old=payload.GetProperty("evaluation").Deserialize<Evaluation>(json)!;
                var id=payload.GetProperty("id").GetString();
                var chosen=choices.LastOrDefault(c=>c.GetProperty("decisionId").GetString()==id);
                var key=chosen.ValueKind==JsonValueKind.Undefined?null:chosen.GetProperty("key").GetString();
                var wave=input.FirstOrDefault(o=>o.Card?.ClassName=="IronWave");
                if(wave!=null)offers.Add(new{file=Path.GetFileName(file),id,seed=state.Hidden?.Seed,state.Floor,old.PolicyVersion,recommended=old.RecommendedKey==wave.Key,chosen=key==wave.Key,choiceRecorded=key!=null,previousCopies=state.Deck.Count(c=>c.ClassName=="IronWave"),score=old.Options.First(o=>o.Key==wave.Key).Score});
                if(state.Hidden?.Seed==args[1])
                    decisions.Add(new{id,state.Floor,state.Hp,deck=state.Deck.Select(c=>c.Name).ToArray(),old,updated=new Policy().Evaluate(state,input),chosen=key});
            }
            if(type=="auto_route"&&latest?.Hidden?.Seed==args[1])
            {
                var route=payload.GetProperty("payload").Deserialize<RouteEvaluation>(json)!;
                // Old logs lack the graph; this is only normalization of the recorded path,
                // NOT a replay of all legal alternative paths under the new policy.
                routes.Add(new{latest.Floor,latest.Hp,route,recordedPathNormalization=route.Options.Select(o=>new{o.Node,o.Kind,oldScore=o.Score,normalizedOnly=o.Score/o.Path.Select((_,i)=>Math.Pow(.55,i)).Sum(),weakDeckPenaltyAtFirstNode=o.Kind=="Elite"?120*Math.Max(0,.6-route.DeckQuality):0})});
            }
        }
    }
    finally{foreach(var record in records)record.Dispose();}
}
File.WriteAllText(args[2],JsonSerializer.Serialize(new{seed=args[1],policy=Policy.Version,offers,decisions,routes,limits="Offer records are observations, not win-rate evidence. Old route logs lack graph; path normalization is not a full replay."},json));
Console.WriteLine($"Recorded {offers.Count} Iron Wave offers; target run has {decisions.Count} decisions and {routes.Count} routes.");
