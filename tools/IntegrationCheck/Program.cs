using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

if(args.Length==0)throw new ArgumentException("Pass the path to your game data_sts2_windows_x86_64 directory.");
var data = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var path = Path.Combine(data, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var mod = Assembly.Load("SpireStrategist");
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
