using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

var root=Path.GetFullPath(args.Length>0?args[0]:".");
var entries=new Dictionary<string,List<string>>();
foreach(var folder in new[]{"src/SpireStrategist.Core","src/SpireStrategist.Mod"})
foreach(var file in Directory.GetFiles(Path.Combine(root,folder),"*.cs"))
{
    if(Path.GetFileName(file).Contains("Lab")||Path.GetFileName(file).Contains("Localization"))continue;
    var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(file));
    foreach(var node in tree.GetRoot().DescendantNodes())
    {
        string? text=null;
        if(node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))text=literal.Token.ValueText;
        if(node is InterpolatedStringExpressionSyntax interpolated)
        {
            var i=0;text=string.Concat(interpolated.Contents.Select(c=>c is InterpolatedStringTextSyntax t?t.TextToken.ValueText:"{"+(i++)+"}"));
        }
        if(text==null||!Regex.IsMatch(text,"[\\u3400-\\u9fff]"))continue;
        if(!entries.TryGetValue(text,out var locations))entries[text]=locations=[];
        locations.Add(Path.GetRelativePath(root,file).Replace('\\','/')+":"+(tree.GetLineSpan(node.Span).StartLinePosition.Line+1));
    }
}
var json=new JsonSerializerOptions{WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
Directory.CreateDirectory(Path.Combine(root,"artifacts/localization"));
File.WriteAllText(Path.Combine(root,"artifacts/localization/source.json"),JsonSerializer.Serialize(entries,json));
Console.WriteLine($"{entries.Count} unique strings/templates");

if(args.Contains("--check"))
{
    var catalog=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"localization/en.json")))!;
    var missing=entries.Keys.Where(k=>!catalog.ContainsKey(k)).ToArray();
    foreach(var key in missing)Console.Error.WriteLine("Missing: "+key+" @ "+string.Join(", ",entries[key]));
    if(missing.Length>0)Environment.ExitCode=1;
    else Console.WriteLine("All extracted source strings/templates have English translations.");
}
