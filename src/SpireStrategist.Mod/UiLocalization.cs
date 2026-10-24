using Godot;
using MegaCrit.Sts2.Core.Localization;
using SpireStrategist.Core;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static class UiLocalization
{
    private sealed record Settings(string Language="auto");
    private sealed class TextState { public readonly Dictionary<string,(string Raw,string Display)> Values=[]; }
    private static readonly ConditionalWeakTable<Control,TextState> States=new();
    private static readonly string SettingsPath=Path.Combine(OS.GetUserDataDir(),"SpireStrategist","language.json");
    private static Settings preferences=Load();
    internal static string Preference=>preferences.Language;
    internal static bool English => preferences.Language=="en" || preferences.Language=="auto" &&
        !(LocManager.Instance?.Language is "zhs" or "zht");
    internal static string Text(string value)=>English?Localization.English(value):value;
    private static Settings Load()
    {
        try { var result=JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath),Journal.Json);
            return result?.Language is "auto" or "zh" or "en"?result:new(); }
        catch { return new(); }
    }
    internal static void Set(string language)
    {
        if(language is not ("auto" or "zh" or "en"))throw new ArgumentException(nameof(language));
        var next=new Settings(language);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,JsonSerializer.Serialize(next,Journal.Json));preferences=next;
    }
    internal static void AddPicker(VBoxContainer parent)
    {
        parent.AddChild(AdvisorUi.Text("语言 / Language"));
        var choice=new OptionButton{Name="AssistantLanguage",FocusMode=Control.FocusModeEnum.None};
        choice.AddItem("跟随游戏");choice.AddItem("简体中文");choice.AddItem("English");
        choice.Select(Array.IndexOf(new[]{"auto","zh","en"},Preference));parent.AddChild(choice);
        choice.ItemSelected+=i=>{try{Set(new[]{"auto","zh","en"}[(int)i]);}catch(Exception ex){GD.PrintErr("[SpireStrategist] Language settings: "+ex.Message);}};
        choice.TooltipText="语言设置已保存。卡牌与事件名称跟随游戏语言。";
    }
    internal static void Apply(Node root)
    {
        if(root is Control control)
        {
            var state=States.GetOrCreateValue(control);
            void Update(string key,string current,Action<string> set)
            {
                var raw=state.Values.TryGetValue(key,out var old)&&current==old.Display?old.Raw:current;
                var value=Text(raw);state.Values[key]=(raw,value);if(value!=current)set(value);
            }
            Update("tooltip",control.TooltipText,s=>control.TooltipText=s);
            switch(control)
            {
                case OptionButton options:
                    for(var i=0;i<options.ItemCount;i++){var index=i;Update("option"+i,options.GetItemText(i),s=>options.SetItemText(index,s));}
                    if(options.Name=="AssistantLanguage")options.Select(Array.IndexOf(new[]{"auto","zh","en"},Preference));
                    break;
                case Label label:Update("text",label.Text,s=>label.Text=s);break;
                case RichTextLabel rich:Update("text",rich.Text,s=>rich.Text=s);break;
                case Button button:Update("text",button.Text,s=>button.Text=s);break;
                case TextEdit edit:Update("placeholder",edit.PlaceholderText,s=>edit.PlaceholderText=s);break;
                case TabContainer tabs:
                    for(var i=0;i<tabs.GetTabCount();i++){var index=i;Update("tab"+i,tabs.GetTabTitle(i),s=>tabs.SetTabTitle(index,s));}
                    break;
            }
        }
        foreach(var child in root.GetChildren())Apply(child);
    }
}
