using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

if (args.Length < 3) { Console.Error.WriteLine("Inspect <assembly> <output-directory> <type ... | @type-list>"); return 2; }
Directory.CreateDirectory(args[1]);
var decompiler = new CSharpDecompiler(args[0], new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
var names = args.Skip(2).SelectMany(s => s.StartsWith('@') ? File.ReadAllLines(s[1..]) : [s]).Where(s => !string.IsNullOrWhiteSpace(s));
foreach (var name in names)
{
    var source = decompiler.DecompileTypeAsString(new FullTypeName(name));
    File.WriteAllText(Path.Combine(args[1], name.Split('.').Last() + ".cs"), source);
    Console.WriteLine(name);
}
return 0;
