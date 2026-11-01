using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using System.Text.Json;
using SpireStrategist.Core;

if(args.Length==0)throw new ArgumentException("Pass the path to your game data_sts2_windows_x86_64 directory.");
var data = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var path = Path.Combine(data, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var mod = Assembly.Load("SpireStrategist");
if(args.Contains("--camp-audit"))
{
    var game=Assembly.Load("sts2");
    foreach(var type in game.GetTypes().Where(t=>!t.IsAbstract&&!t.IsNested).OrderBy(t=>t.FullName))
    {
        var hooks=type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
            .Where(m=>m.IsVirtual&&m.Name.Contains("RestSite")).Select(m=>m.Name).ToArray();
        if(type.Namespace=="MegaCrit.Sts2.Core.Entities.RestSite"||hooks.Length>0)
            Console.WriteLine(type.FullName+": "+string.Join(", ",hooks));
    }
    return 0;
}
// Exercise the shipped assembly, not only the separately built Core test project.
if(args.Length>=3)
{
    using var embedded=mod.GetManifestResourceStream("SpireStrategist.DefectPrior.json")!;
    using var memory=new MemoryStream();embedded.CopyTo(memory);
    if(!memory.ToArray().SequenceEqual(File.ReadAllBytes(args[1])))throw new Exception("Packaged Defect prior differs from the configured source file");
    using var neutralStream=mod.GetManifestResourceStream("SpireStrategist.DefectNeutral.json")!;
    using var neutralBytes=new MemoryStream();neutralStream.CopyTo(neutralBytes);
    var neutralSource=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"defect-neutral-v1.json");
    if(!neutralBytes.ToArray().SequenceEqual(File.ReadAllBytes(neutralSource)))throw new Exception("Packaged colorless mechanisms differ from reviewed source");
    using var prior=JsonDocument.Parse(memory.ToArray());
    if(prior.RootElement.GetProperty("version").GetString()!=DefectPolicy.Version)throw new Exception("Policy code and packaged prior versions disagree");
    using var fixtures=JsonDocument.Parse(File.ReadAllText(args[2]));
    var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};var count=0;
    foreach(var entry in fixtures.RootElement.EnumerateArray())
    {
        var state=entry.GetProperty("state").Deserialize<RunSnapshot>(json)!;
        foreach(var expected in entry.GetProperty("scores").EnumerateArray())
        {
            var name=expected.GetProperty("name").GetString()!;var upgraded=expected.GetProperty("upgraded").GetBoolean();
            var card=DefectPolicy.Card(name,upgraded)??(upgraded?CardRegistry.All[name].Upgraded:CardRegistry.All[name].Basic);
            var actual=DefectPolicy.Evaluate(state,[new("pick",name,"card",Card:card)]).Options[0].Score;
            var target=expected.GetProperty("score");
            if(target.ValueKind==JsonValueKind.Null?actual!=null:actual==null||Math.Abs(actual.Value-target.GetDouble())>.0011)
                throw new Exception($"Packaged policy mismatch: {name}, expected {target}, actual {actual}");
            count++;
        }
    }
    Console.WriteLine($"PASS packaged DLL: prior bytes/version and {count} scoring cases match source and offline fixtures");
}
var targets = mod.GetTypes().Where(t => t.GetCustomAttributes<HarmonyPatch>().Any()).ToArray();
var harmony = new Harmony("local.SpireStrategist.integrationcheck");
var passed = 0;
var rngChecks = false;
try
{
    foreach (var type in targets)
    {
        try
        {
            var methods = harmony.CreateClassProcessor(type).Patch();
            if (methods == null || methods.Count == 0) throw new Exception("No matched game methods");
            Console.WriteLine("PASS hook " + type.Name + " -> " + string.Join(", ", methods.Select(m => m.Name)));
            passed++;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL hook " + type.Name + ": " + ex); }
    }
    var readStream = mod.GetType("SpireStrategist.Mod.HiddenReader")!.GetMethod("ReadStream", BindingFlags.Static | BindingFlags.NonPublic)!;
    var rngType = Assembly.Load("sts2").GetType("MegaCrit.Sts2.Core.Random.Rng")!;
    var save = rngType.GetMethod("ToSerializable")!;
    var next = rngType.GetMethod("NextInt", [typeof(int)])!;
    for (ulong seed = 0; seed < 100; seed++)
    {
        var rng = Activator.CreateInstance(rngType, [seed])!;
        for (var n=0; n<10; n++)
        {
            var before = save.Invoke(rng, null)!;
            var a = readStream.Invoke(null, ["test", rng])!;
            var b = readStream.Invoke(null, ["test", rng])!;
            if (!before.Equals(save.Invoke(rng,null))) throw new Exception("Hidden reader mutated source RNG");
            if (System.Text.Json.JsonSerializer.Serialize(a) != System.Text.Json.JsonSerializer.Serialize(b)) throw new Exception("Preview differs at identical RNG state");
            next.Invoke(rng, [1000000]);
        }
    }
    rngChecks = true;
    Console.WriteLine("PASS 1000 real-game RNG copy previews: deterministic and source state unchanged");
}
finally { harmony.UnpatchAll(harmony.Id); }
Console.WriteLine($"{passed}/{targets.Length} hooks bind successfully; game/UI execution not performed.");
return passed == targets.Length && targets.Length > 0 && rngChecks ? 0 : 1;
