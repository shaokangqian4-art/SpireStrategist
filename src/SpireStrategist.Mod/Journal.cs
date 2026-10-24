using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed class Journal
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    internal string DirectoryPath { get; }
    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    private readonly string logPath;

    internal Journal(string root)
    {
        DirectoryPath = root;
        Directory.CreateDirectory(root);
        logPath = Path.Combine(root, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SessionId[..8]}.jsonl");
    }

    internal void Write(string type, object payload)
    {
        var line = JsonSerializer.Serialize(new { schemaVersion = 1, utc = DateTimeOffset.UtcNow,
            sessionId = SessionId, type, payload }, Json);
        File.AppendAllText(logPath, line + "\n", new UTF8Encoding(false));
    }

    internal void Latest(string name, object value)
    {
        var path = Path.Combine(DirectoryPath, name + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    internal string ExportFeedback(object state)
    {
        var folder=Path.Combine(DirectoryPath,"feedback-exports");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,$"StrategyAssistant-feedback-{DateTime.Now:yyyyMMdd-HHmmss}-{SessionId[..8]}-{Guid.NewGuid():N}.zip");
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        if(File.Exists(logPath))zip.CreateEntryFromFile(logPath,UiLocalization.Text("本局记录.jsonl"),CompressionLevel.Optimal);
        void Add(string name,string text)
        {using var writer=new StreamWriter(zip.CreateEntry(UiLocalization.Text(name)).Open(),new UTF8Encoding(false));writer.Write(name.EndsWith(".txt")?UiLocalization.Text(text):text);}
        Add("当前状态.json",JsonSerializer.Serialize(state,Json));
        Add("反馈说明.txt","这是战略助手本局的本地反馈包，包含种子、牌组、路线、决策和你填写的意见。没有自动上传。\n"+
            "请把本 ZIP 发给模组作者或给你试玩包的人，可另附截图和复现步骤。\n"+
            "不需要 GPT、账号或开发聊天记录；作者会结合局面分析，意见不会自动改权重。\n"+
            "如果意见里包含私人信息，可以先检查再发送。此包不包含其他局、游戏存档或账号配置。\n"+
            "建议描述：当时想选什么、原因、助手哪里不符合预期。");
        return path;
    }

    internal PolicyWeights LoadWeights()
    {
        var file = Path.Combine(DirectoryPath, "weights.json");
        if (!File.Exists(file))
            File.WriteAllText(file, JsonSerializer.Serialize(new PolicyWeights(), new JsonSerializerOptions(Json) { WriteIndented = true }));
        var weights = JsonSerializer.Deserialize<PolicyWeights>(File.ReadAllText(file), Json)
            ?? throw new InvalidDataException("weights.json 内容为空。");
        weights.Validate();
        return weights;
    }
}
