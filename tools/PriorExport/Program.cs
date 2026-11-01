using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Globalization;
using SpireStrategist.Core;

if(args.Length!=2)throw new ArgumentException("Usage: PriorExport <native cards localization.json> <output directory>");
var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
var names=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[0]),json)!;
var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
JsonDocument Resource(string name)=>JsonDocument.Parse(typeof(Policy).Assembly.GetManifestResourceStream("SpireStrategist."+name+".json")!);
using var defectCards=Resource("DefectCards");using var prior=Resource("DefectPrior");using var neutral=Resource("DefectNeutral");
var definitions=CardRegistry.All.Values.Concat(defectCards.RootElement.EnumerateArray().Select(x=>new CardDefinition("DefectCardPool",
    x.GetProperty("basic").Deserialize<CardSnapshot>(json)! with{MultiplayerOnly=x.GetProperty("multiplayerConstraint").GetString()=="MultiplayerOnly"},
    x.GetProperty("upgraded").Deserialize<CardSnapshot>(json)! with{MultiplayerOnly=x.GetProperty("multiplayerConstraint").GetString()=="MultiplayerOnly"}))).ToList();
// Include explicit profiles absent from reward registries, without inventing a reward pool.
foreach(var key in CardCatalog.Profiles.Keys.Where(k=>definitions.All(d=>d.Basic.ClassName!=k)))
{
    var id=System.Text.RegularExpressions.Regex.Replace(key,"(?<=[a-z0-9])(?=[A-Z])","_").ToUpperInvariant();
    var c=new CardSnapshot{ClassName=key,Id="CARD."+id,Name=key};
    definitions.Add(new("OtherProfile",c,c));
}
var rows=new List<PriorRow>();var policy=new Policy();
Quote QuoteFor(CardSnapshot c,string character)
{
    var result=policy.Evaluate(new RunSnapshot{Character=character,Hp=80,MaxHp=80,Act=1},[new("card",c.Name,"card",Card:c)]).Options.Single();
    if(!result.Supported)return new(null,null,"未建模");
    var basis=result.Parts.FirstOrDefault(p=>p.Reason.Contains("基础先验")||p.Reason.StartsWith("无色牌在机器人构筑中的净收益先验"));
    var upgrade=result.Parts.FirstOrDefault(p=>p.Reason is "已有升级" or "该牌已升级的实际机制增益先验");
    // Defect omits zero-valued parts from its explanation.
    return new(basis?.Value??0,upgrade?.Value??0,"已建模");
}
foreach(var d in definitions)
{
    var c=d.Basic;var ignored=MultiplayerAdvice.Ignore(c);
    var name=names.GetValueOrDefault(c.Id.Replace("CARD.","")+".title",c.Name);
    Quote? iron=d.Pool=="DefectCardPool"?null:QuoteFor(c,"Ironclad");
    Quote? ironUp=iron==null?null:QuoteFor(d.Upgraded,"Ironclad");
    Quote? defect=d.Pool=="IroncladCardPool"?null:QuoteFor(c,"Defect");
    Quote? defectUp=defect==null?null:QuoteFor(d.Upgraded,"Defect");
    JsonElement p=default;var hasPrior=prior.RootElement.GetProperty("cards").TryGetProperty(c.ClassName,out p);
    var phase=hasPrior?p.GetProperty("phase").EnumerateArray().Select(x=>x.GetDouble()).ToArray():null;
    var note=hasPrior&&p.TryGetProperty("note",out var n)?n.GetString():CardCatalog.Find(c)?.Note;
    if(neutral.RootElement.GetProperty("cards").TryGetProperty(c.ClassName,out var neutralModel))note=neutralModel.GetProperty("note").GetString();
    rows.Add(new(d.Pool,name,c.ClassName,c.Rarity,ignored,iron,ironUp,defect,defectUp,phase,note??""));
    if(ignored&&new[]{iron,ironUp,defect,defectUp}.Any(q=>q?.Base!=null))throw new Exception("Cooperative card received a base: "+c.ClassName);
    if(d.Pool=="DefectCardPool"&&!ignored&&hasPrior&&defect?.Base!=p.GetProperty("base").GetDouble())throw new Exception("Runtime base mismatch: "+c.ClassName);
    if(neutralModel.ValueKind==JsonValueKind.Object&&!ignored&&defect?.Base!=neutralModel.GetProperty("base").GetDouble())throw new Exception("Neutral runtime mismatch: "+c.ClassName);
}
string N(double? n)=>n?.ToString("0.###",CultureInfo.InvariantCulture)??"未建模";
string Q(Quote? q)=>q==null?"—":N(q.Base);
string R(string r)=>r switch{"Basic"=>"初始","Common"=>"普通","Uncommon"=>"罕见","Rare"=>"稀有","Ancient"=>"先古","Event"=>"事件",_=>r};
var text=new StringBuilder();
text.AppendLine("全局战略助手 0.6.45｜全部已登记卡牌的基础先验");
text.AppendLine("日期：2026-10-29；游戏数据0.111.0；机器人策略defect-draft-0.14。");
text.AppendLine("本表通过当前编译策略实际评分函数提取基础项；不修改任何权重，不是重新训练。");
text.AppendLine($"共{rows.Count}个条目；含多人未建模条目{rows.Count(r=>r.MultiplayerOnly)}个。仅当前已登记卡池/显式档案，不是五个角色全卡强度榜。");
text.AppendLine();
text.AppendLine("一、基础先验是什么意思");
text.AppendLine("它是对单卡的起始估值，和卡牌强弱判断有关，但没有包含当前牌组需求、配套、重复份数、阶段、抽牌占位、商店价格和辉眼投资等全部因素。");
text.AppendLine("它不是伤害、百分比、胜率或最终抓牌分。0表示这一项不预加分，不表示无用；负数也不表示在所有体系都不抓。");
text.AppendLine("战士的基础项常较低：抽牌、供能、伤害、防御、成长会在其他项加分。机器人本色牌使用独立base，另扣抽牌成本2.5，再加阶段和配套等。");
text.AppendLine("机器人无色牌的基础项已经包含自身抽牌与启动成本；升级版基础项直接含该牌upgrade增量，不能再扣一次2.5或重复加升级。");
text.AppendLine("三类数值口径不同，不能横跨战士/机器人/无色表直接排序，尤其不能拿战士黑暗之拥的0与机器人某牌的5判断谁更强。");
text.AppendLine("升级版基础先验、拿到升级牌的单列加分、火堆升级价值是三回事。本表升级加分仅指拿到已升级牌的那一项；实际伤害/抽牌变化仍在其他项体现。火堆优先级不在本表。");
text.AppendLine("多人专属牌写“未建模”，不是0分，也不是代码中历史占位值；从本版实际推荐中排除。");
text.AppendLine();
foreach(var (pool,title) in new[]{("IroncladCardPool","二、战士卡池"),("DefectCardPool","三、故障机器人卡池"),("ColorlessCardPool","四、无色牌：战士与机器人分别列值"),("EventLegacy","五、事件/先古旧牌档案"),("OtherProfile","六、奖励登记之外的显式档案")})
{
    var group=rows.Where(r=>r.Pool==pool).OrderBy(r=>r.ClassName).ToArray();
    text.AppendLine($"{title}（{group.Length}条）");
    if(pool is "IroncladCardPool" or "DefectCardPool")
        text.AppendLine("卡名 [代码名] | 稀有度 | 基础先验 | 升级版基础项 | 升级牌单列加分"+(pool=="DefectCardPool"?" | 一/二/三幕另加":""));
    else text.AppendLine("卡名 [代码名] | 稀有度 | 战士基础/升级基础/单列升级加分 | 机器人基础/升级基础/单列升级加分");
    foreach(var r in group)
    {
        var line=$"{r.Name} [{r.ClassName}] | {R(r.Rarity)} | ";
        if(r.MultiplayerOnly)line+="多人专属，忽略评分";
        else if(pool is "IroncladCardPool" or "DefectCardPool")
        {
            var basic=pool=="DefectCardPool"?r.Defect:r.Ironclad;var upgraded=pool=="DefectCardPool"?r.DefectUpgraded:r.IroncladUpgraded;
            line+=$"{Q(basic)} | {Q(upgraded)} | {N(upgraded?.UpgradeBonus)}";
            if(pool=="DefectCardPool")line+=" | "+(r.Phase==null?"—":string.Join(" / ",r.Phase.Select(x=>N(x))));
        }
        else line+=$"{Q(r.Ironclad)} / {Q(r.IroncladUpgraded)} / {N(r.IroncladUpgraded?.UpgradeBonus)} | {Q(r.Defect)} / {Q(r.DefectUpgraded)} / {N(r.DefectUpgraded?.UpgradeBonus)}";
        text.AppendLine(line);
    }
    text.AppendLine();
}
text.AppendLine("来源：CardCatalog、ExtraCardModels、CardRegistry、Policy、DefectPolicy、DefectNeutral；JSON详细版同时保存逐卡现有备注。");
text.AppendLine("其他角色、尚未登记的生成牌/诅咒/模组牌没有在此伪造专属先验；部分状态/诅咒仅有统一负担处理，并非逐卡模型。");
var file=Path.Combine(output,"0.6.45_全部卡牌基础先验.txt");File.WriteAllText(file,text.ToString(),new UTF8Encoding(true));
File.WriteAllText(Path.Combine(output,"0.6.45_全部卡牌基础先验.json"),JsonSerializer.Serialize(rows,json),new UTF8Encoding(false));
Console.WriteLine(JsonSerializer.Serialize(new{file,rows=rows.Count,pools=rows.GroupBy(r=>r.Pool).ToDictionary(g=>g.Key,g=>g.Count()),untranslated=rows.Where(r=>r.Name==r.ClassName).Select(r=>r.ClassName),multiplayerIgnored=rows.Count(r=>r.MultiplayerOnly)},json));
record Quote(double? Base,double? UpgradeBonus,string Status);
record PriorRow(string Pool,string Name,string ClassName,string Rarity,bool MultiplayerOnly,Quote? Ironclad,Quote? IroncladUpgraded,Quote? Defect,Quote? DefectUpgraded,double[]? Phase,string Note);
