using System.Text.Json;

namespace SpireStrategist.Core;

public sealed class ClimbStreak
{
    public int Current {get;set;}
    public int Best {get;set;}
}

// Opt-in automation is deliberately never persisted. Only preferences and audited streaks are.
public sealed class ClimbPreferences
{
    public static readonly string[] Characters=["Ironclad","Silent","Regent","Necrobinder","Defect"];
    public int Ascension {get;set;}
    public string Character {get;set;}="Ironclad";
    public Dictionary<string,ClimbStreak> Streaks {get;set;}=[];
    public string? PendingRunId {get;set;}
    public string? PendingMode {get;set;}
    public static string Mode(string character,int ascension)=>$"{character}:N{ascension}";
    public bool Supported=>DefectStrategy.Supported(Character);
    public ClimbStreak Get(string mode)
    {
        if(!Streaks.TryGetValue(mode,out var record))Streaks[mode]=record=new();
        return record;
    }
    public void Begin(ClimbResult run,string character,int ascension)
    {
        RecoverInterrupted();
        PendingRunId=run.Id;PendingMode=Mode(character,ascension);
        if(!run.FullRun)Get(PendingMode).Current=0;
    }
    public void Assisted()
    {if(PendingMode!=null)Get(PendingMode).Current=0;}
    public void Complete(ClimbResult? result)
    {
        if(result==null||PendingRunId!=result.Id||PendingMode==null)return;
        var record=Get(PendingMode);
        record.Current=result.FullRun&&result.Outcome=="win"?record.Current+1:0;
        record.Best=Math.Max(record.Best,record.Current);
        PendingMode=null;PendingRunId=null;
    }
    public void RecoverInterrupted()
    {
        if(PendingMode!=null)Get(PendingMode).Current=0;
        PendingMode=null;PendingRunId=null;
    }
    public static ClimbPreferences Load(string path)
    {
        var p=File.Exists(path)?JsonSerializer.Deserialize<ClimbPreferences>(File.ReadAllText(path))??new():new();
        p.Ascension=Math.Clamp(p.Ascension,0,10);
        if(!Characters.Contains(p.Character))p.Character="Ironclad";
        p.Streaks??=[];
        foreach(var key in p.Streaks.Keys.ToArray())
        {
            var r=p.Streaks[key]??new();p.Streaks[key]=r;
            r.Current=Math.Max(0,r.Current);r.Best=Math.Max(r.Current,r.Best);
        }
        p.RecoverInterrupted();return p;
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temporary,path,true);
    }
}

public static class AutomationTiming
{
    public static ulong Delay(bool looping,ulong ordinary)=>looping?Math.Min(250UL,ordinary):ordinary;
    public const ulong NextRunDelay=1000;
}
