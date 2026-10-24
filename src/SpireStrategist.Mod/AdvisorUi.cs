using Godot;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal sealed record AdvisorDisplaySettings(bool Items = true, bool Routes = true)
{
    private static string PathName => Path.Combine(OS.GetUserDataDir(), "SpireStrategist", "display-settings.json");
    internal static AdvisorDisplaySettings Load()
    {
        try { return JsonSerializer.Deserialize<AdvisorDisplaySettings>(File.ReadAllText(PathName), Journal.Json) ?? new(); }
        catch { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this, Journal.Json));
    }
}

internal static class AdvisorUi
{
    internal const string SolverLocked = "安装求解器mod（战斗路线求解器 / CombatSolver）开放";
    internal static readonly Color Accent = new("65dfc1");
    internal static Theme Theme()
    {
        var theme=new Theme { DefaultFont = new SystemFont { FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC"] }, DefaultFontSize = 16 };
        StyleBoxFlat Tile(string color)=>new(){BgColor=new Color(color),CornerRadiusTopLeft=4,CornerRadiusTopRight=4,
            CornerRadiusBottomLeft=4,CornerRadiusBottomRight=4,ContentMarginLeft=8,ContentMarginRight=8,ContentMarginTop=6,ContentMarginBottom=6};
        theme.SetStylebox("normal","Button",Tile("203440"));theme.SetStylebox("hover","Button",Tile("30545b"));
        theme.SetStylebox("pressed","Button",Tile("396b68"));theme.SetStylebox("disabled","Button",Tile("18252d"));
        theme.SetStylebox("panel","TabContainer",new StyleBoxEmpty());
        theme.SetStylebox("tab_selected","TabContainer",Tile("30545b"));
        theme.SetStylebox("tab_unselected","TabContainer",Tile("182832"));
        theme.SetColor("font_selected_color","TabContainer",Accent);
        return theme;
    }
    internal static StyleBoxFlat Surface() => new()
    {
        BgColor = new Color("111c27f5"), BorderColor = new Color("3b626b"),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 2, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10
    };
    internal static Label Text(string text, int width = 350) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0),
        MouseFilter = Control.MouseFilterEnum.Ignore
    };
    internal static Button Button(string text) => new() { Text = text, FocusMode = Control.FocusModeEnum.None };
    internal static void PassiveContainers(Node node)
    {
        if (node is Label || node is Container and not ScrollContainer) ((Control)node).MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in node.GetChildren()) PassiveContainers(child);
    }
}
