namespace SpireStrategist.Core;

// Plain data only: safe to replay without a game process. Hidden information is separated explicitly.
public sealed record CardSnapshot
{
    public string Id { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string Description { get; init; } = "";
    public int Cost { get; init; }
    public bool CostsX { get; init; }
    public int UpgradeLevel { get; init; }
    public bool CanUpgrade { get; init; }
    public bool Modified { get; init; }
    public string? Enchantment { get; init; }
    public int EnchantmentAmount { get; init; }
    public string? Affliction { get; init; }
    public string[] Keywords { get; init; } = [];
    public Dictionary<string, decimal> Variables { get; init; } = [];
}

public sealed record RunSnapshot
{
    public string Character { get; init; } = "";
    public int PlayerCount { get; init; } = 1;
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public int Gold { get; init; }
    public int Energy { get; init; } = 3;
    public bool CombatSolverAvailable { get; init; }
    public int Act { get; init; }
    public int Floor { get; init; }
    public int Ascension { get; init; }
    public string Room { get; init; } = "";
    public CardSnapshot[] Deck { get; init; } = [];
    public string[] Relics { get; init; } = [];
    public string?[] PotionSlots { get; init; } = [];
    public string[] NextNodeTypes { get; init; } = [];
    public HiddenSnapshot? Hidden { get; init; }
    public MapContext? Map { get; init; }
    public CombatSummary[] RecentBattles {get;init;}=[];
    public CommerceContext? Commerce {get;init;}
    // Captured at an ordinary camp so offline policy replay includes the same survival check.
    public Dictionary<string,double> CampRouteRisk {get;init;}=[];
}

public sealed record CommerceContext(int ReachableShops,int? NearestShopDistance,int ReachableUnknowns,int FutureActs);

public sealed record CombatSummary(int Act,string Room,string? Encounter,int DamageTaken,int Healing,int PotionsUsed,int MaxHp);

public sealed record MapContext(int TotalActs,int Rows,int CurrentRow,int BossRow,string MapType,
    bool LongMap=false,int ExtraLayers=0,int ExtraActs=0,int ContentAct=1,
    double EnemyHpScale=1,double EnemyDamageScale=1,double CombatGoldScale=1,string? Error=null);

public sealed record RngSnapshot(string Stream, int Counter, ulong S0, ulong S1, ulong S2, ulong S3, int[] Preview);
public sealed record ActForecast(int Act, string[] NormalEncounters, int NormalVisited, string[] EliteEncounters,
    int EliteVisited, string[] EventPool, int EventsVisited, string? Boss, string? SecondBoss, string? Ancient);
public sealed record HiddenSnapshot(string Seed, RngSnapshot[] Streams, ActForecast[] Acts, string[] DrawPile,
    bool SourceUnchanged, string? Error = null);

public sealed record DecisionOption(string Key, string Name, string Kind,
    bool Enabled = true, CardSnapshot? Card = null, double HealAmount = 0, int SmithCount = 1,double? EffectValue=null);
public sealed record ScorePart(string Reason, double Value);
public sealed record OptionScore(string Key, string Name, bool Supported, bool Enabled,
    double? Score, ScorePart[] Parts, string[] Notes);
public sealed record Evaluation(string PolicyVersion, string? RecommendedKey,
    OptionScore[] Options, string[] Warnings);

public sealed record PolicyWeights
{
    public double DamageNeed { get; init; } = 4;
    public double BlockNeed { get; init; } = 4;
    public double Draw { get; init; } = 2.5;
    public double Energy { get; init; } = 2;
    public double ScalingNeed { get; init; } = 3;
    public double Synergy { get; init; } = 3;
    public double CardTax { get; init; } = 3;
    public double DuplicatePenalty { get; init; } = 1.4;
    public double DeckSizePenalty { get; init; } = 0.3;
    public double ExpensivePenalty { get; init; } = 2;
    public double HpRisk { get; init; } = 3;
    public double HealValue { get; init; } = 30;
    public double HealUrgency { get; init; } = 6;
    public double UpgradeValue { get; init; } = 1;

    public void Validate()
    {
        foreach (var property in GetType().GetProperties())
        {
            var value = (double)property.GetValue(this)!;
            if (!double.IsFinite(value) || value < 0 || value > 100)
                throw new ArgumentException($"参数 {property.Name} 必须在 0–100 之间。");
        }
    }
}
