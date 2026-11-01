using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

// Node2D has no GUI/input handler. Every mark is paint only, never a clickable control.
// Reuse the advisor's evaluated choices; painting cannot roll RNG or commit a decision.
internal sealed class RecommendationOverlay
{
    private readonly AdvisorService service;
    private readonly Node2D paint;
    private ShopEvaluation? shop;
    private NMerchantInventory? shopOwner;
    private RouteEvaluation? route;
    private NMapScreen? routeOwner;
    private string? routeOrigin;
    private int routeAct;
    private bool paintFaultLogged;
    internal string? VisibleItemKey { get; private set; }
    internal int RouteSegments { get; private set; }
    internal RecommendationOverlay(Node parent, AdvisorService service)
    {
        this.service=service;
        var layer=new CanvasLayer{Name="StrategistRecommendationPaint",Layer=80};parent.AddChild(layer);
        paint=new Node2D();layer.AddChild(paint);paint.Draw+=Draw;
        var timer=new Godot.Timer{WaitTime=1d/30};layer.AddChild(timer);
        timer.Timeout+=()=>paint.QueueRedraw();timer.Start();
    }
    internal void Shop(ShopEvaluation value)
    {shop=value;shopOwner=NMerchantRoom.Instance?.Inventory;}
    internal void Route(RouteEvaluation value)
    {route=value;routeOwner=NMapScreen.Instance;routeAct=service.State.CurrentActIndex;routeOrigin=service.State.CurrentMapPoint is {} p?RouteAdapter.Key(p):null;}
    internal void Clear(){shop=null;route=null;paint.QueueRedraw();}
    private void Draw()
    {
        try { DrawCore(); }
        catch(Exception ex)
        {
            VisibleItemKey=null;RouteSegments=0;
            if(!paintFaultLogged){paintFaultLogged=true;GD.Print("[SpireStrategist] 推荐标记暂不可用："+ex.GetBaseException().Message);}
        }
    }
    private void DrawCore()
    {
        VisibleItemKey=null;RouteSegments=0;
        if(service.Player.Character.GetType().Name=="Defect"&&service.Panel.DefectShadow)return;
        if(MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance?.OpenModal!=null)return;
        var screen=ActiveScreenContext.Instance?.GetCurrentScreen();
        if(service.Panel.DrawRoutes && NMapScreen.Instance is {IsOpen:true} map && ReferenceEquals(map,routeOwner) && ReferenceEquals(screen,map)) DrawRoute(map);
        if(!service.Panel.HighlightItems || NMapScreen.Instance?.IsOpen==true)return;
        if(service.Selection is {} selection && ReferenceEquals(screen,selection.Screen))
        {
            foreach(var holder in GameActions.Descendants<NCardHolder>(selection.Screen).Where(h=>h.CardModel!=null&&selection.Targets.Contains(h.CardModel)))
                Frame(holder.Hitbox,"selection:"+holder.GetInstanceId());
        }
        else if(screen is NEventRoom events && service.Decision is {Kind:"event"} ev && ev.Evaluation.RecommendedKey is {} eventKey)
            Frame(GameActions.Descendants<NEventOptionButton>(events).FirstOrDefault(b=>b.GetInstanceId().ToString()==eventKey),eventKey);
        else if(screen is NRestSiteRoom rest && service.Decision is {Kind:"rest"} rd && rd.Evaluation.RecommendedKey is {} restKey)
            Frame(GameActions.Descendants<MegaCrit.Sts2.Core.Nodes.RestSite.NRestSiteButton>(rest).FirstOrDefault(b=>b.Option.OptionId==restKey),restKey);
        else if(screen is NCardRewardSelectionScreen cards && ReferenceEquals(cards,service.CardScreen) &&
            service.Decision is {Kind:"cards"} d && d.Evaluation.RecommendedKey is {} key)
        {
            Control? target=null;
            if(key.StartsWith("card:")&&int.TryParse(key[5..],out var index)&&index>=0&&index<service.CardOptions.Count)
                target=cards.GetCardHolder(service.CardOptions[index].Card).Hitbox;
            else if(key.StartsWith("alternative:")&&int.TryParse(key[12..],out index)&&
                GameActions.Field(cards,"_rewardAlternativesContainer") is Control alternatives)
                target=alternatives.GetChildren().OfType<Control>().ElementAtOrDefault(index);
            Frame(target,key);
        }
        else if(screen is NMerchantInventory inventory && ReferenceEquals(inventory,shopOwner) && shop?.Recommendation is {} choice)
        {
            var slot=inventory.GetAllSlots().FirstOrDefault(s=>s.Entry.IsStocked &&
                (choice.Kind=="remove"?s.Entry is MerchantCardRemovalEntry:s.GetInstanceId().ToString()==choice.Key));
            if(slot?.Entry.EnoughGold==true)Frame(slot.Hitbox,choice.Key);
        }
    }
    private void Frame(Control? target,string key)
    {
        if(target==null||!GodotObject.IsInstanceValid(target)||!target.IsVisibleInTree())return;
        var t=target.GetGlobalTransformWithCanvas();
        var size=target.Size;
        if(size.X<1||size.Y<1)return;
        var rect=new Rect2(t*Vector2.Zero,size*t.Scale).Abs().Grow(7);
        var color=AdvisorUi.Accent;
        paint.DrawRect(rect.Grow(3),new Color(0,0,0,.7f),false,5);
        paint.DrawRect(rect,color,false,3,true);
        var font=ThemeDB.FallbackFont;
        paint.DrawString(font,rect.Position+new Vector2(4,-8),UiLocalization.Text("推荐"),fontSize:18,modulate:color);
        VisibleItemKey=key;
    }
    private void DrawRoute(NMapScreen map)
    {
        var current=service.State.CurrentMapPoint is {} p?RouteAdapter.Key(p):null;
        if(routeAct!=service.State.CurrentActIndex || current!=routeOrigin || route?.Options.FirstOrDefault(o=>o.Node==route.RecommendedNode) is not {} best)return;
        var points=GameActions.Descendants<NMapPoint>(map).Where(p=>p.IsVisibleInTree()).GroupBy(p=>RouteAdapter.Key(p.Point))
            .ToDictionary(g=>g.Key,g=>g.First());
        var ids=(current==null?best.Path:new[]{current}.Concat(best.Path)).Distinct().ToArray();
        var bounds=new Rect2(0,190,paint.GetViewportRect().Size.X,Math.Max(0,paint.GetViewportRect().Size.Y-190));
        Vector2 Center(NMapPoint node)=>node.GetGlobalTransformWithCanvas()*(node.Size/2);
        for(var i=1;i<ids.Length;i++)
        {
            if(!points.TryGetValue(ids[i-1],out var a)||!points.TryGetValue(ids[i],out var b))continue;
            var start=Center(a);var end=Center(b);
            // Draw short pieces clipped to the visible map below the potion/relic HUD.
            var length=start.DistanceTo(end);var count=Math.Max(1,(int)Math.Ceiling(length/10));
            var immediate=ids[i]==route.RecommendedNode;
            for(var n=0;n<count;n++)
            {
                if(!immediate&&n%3==2)continue;
                var x=start.Lerp(end,n/(float)count);var y=start.Lerp(end,(n+1)/(float)count);
                if(!bounds.HasPoint(x)||!bounds.HasPoint(y))continue;
                paint.DrawLine(x,y,immediate?new Color("ffd680"):new Color("65dfc1b8"),immediate?5:3,true);
            }
            RouteSegments++;
        }
        if(route.RecommendedNode is {} next&&points.TryGetValue(next,out var node)&&bounds.HasPoint(Center(node)))
            paint.DrawArc(Center(node),42,0,MathF.Tau,48,new Color("ffd680"),4,true);
    }
}
