using Godot;
namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static void CheckLanguage0622(AdvisorPanel panel)
    {
        var previous=UiLocalization.Preference;
        using var root=new VBoxContainer();
        var label=new Label{Text="开始接管"};root.AddChild(label);
        var input=new TextEdit{Text="玩家意见保持原样 / my feedback",PlaceholderText="开始接管"};root.AddChild(input);
        UiLocalization.Set("en");UiLocalization.Apply(root);
        Check(label.Text=="Take over","English presentation uses catalogue");
        Check(input.Text=="玩家意见保持原样 / my feedback","localization never edits player feedback");
        UiLocalization.Set("zh");UiLocalization.Apply(root);
        Check(label.Text=="开始接管","switching back restores Chinese source");
        label.Text="停止接管";UiLocalization.Set("en");UiLocalization.Apply(root);
        Check(label.Text=="Stop","dynamic source changes are translated");
        UiLocalization.Set(previous);
        Check(File.ReadAllText(Path.Combine(OS.GetUserDataDir(),"SpireStrategist","language.json")).Contains(previous),"language preference persisted");
    }
}
