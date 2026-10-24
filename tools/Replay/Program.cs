using System.Text.Encodings.Web;
using System.Text.Json;
using SpireStrategist.Core;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Replay <latest-decision.json | export-*.json> [weights.json]");
    return 2;
}
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true
};
using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var root = document.RootElement;
if (root.TryGetProperty("decision", out var nested)) root = nested;
if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("options", out var choices))
{
    Console.Error.WriteLine("This file does not contain a decision. Export while a reward or rest choice is shown.");
    return 2;
}
var state = root.GetProperty("state").Deserialize<RunSnapshot>(options)!;
var input = choices.Deserialize<DecisionOption[]>(options)!;
var weights = args.Length > 1 ? JsonSerializer.Deserialize<PolicyWeights>(File.ReadAllText(args[1]), options) : null;
Console.WriteLine(JsonSerializer.Serialize(new Policy(weights).Evaluate(state, input), options));
return 0;
