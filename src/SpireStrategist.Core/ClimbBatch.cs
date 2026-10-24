namespace SpireStrategist.Core;

// A run is counted only once and only if this batch started it and retained control.
// Interruptions and joining a player's existing run remain in the ledger, outside win rate.
public sealed record ClimbResult(string Id,string Seed,bool FullRun,string Outcome,string Reason);
public sealed class ClimbBatch
{
    public bool Enabled {get;private set;}
    public bool StopAfterRun {get;private set;}
    public ClimbResult? Current {get;private set;}
    public List<ClimbResult> Results {get;}=[];
    public int Wins=>Results.Count(r=>r.FullRun&&r.Outcome=="win");
    public int Losses=>Results.Count(r=>r.FullRun&&r.Outcome=="loss");
    public void Enable()=>Enabled=true;
    public void RequestStopAfterRun(bool value)=>StopAfterRun=value;
    public void MarkAssisted(string reason)
    {if(Current!=null)Current=Current with{FullRun=false,Reason=reason};}
    public void Start(string seed,bool full)
    {
        if(!Enabled||Current!=null)throw new InvalidOperationException("Cannot start a second active climb.");
        Current=new(Guid.NewGuid().ToString("N"),seed,full,"running","");
    }
    public ClimbResult? Finish(bool win,string? reason=null)
    {
        if(Current==null)return null;
        var result=Current with{Outcome=win?"win":"loss",Reason=reason??Current.Reason};
        Results.Add(result);Current=null;
        if(StopAfterRun){Enabled=false;StopAfterRun=false;}
        return result;
    }
    public ClimbResult? Stop(string reason)
    {
        Enabled=false;StopAfterRun=false;
        if(Current==null)return null;
        var result=Current with{Outcome="interrupted",Reason=reason};
        Results.Add(result);Current=null;
        return result;
    }
}
