using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckPandora0615(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        // Establish native history before obtaining a relic with transformation effects.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Wellspring>(),showTransition:false);
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        var before=p.Deck.Cards.ToArray();
        var basics=before.Where(c=>c.IsBasicStrikeOrDefend&&c.IsRemovable).ToArray();
        Check(basics.Length>0,"fixture contains basic cards for native Pandora transformation");
        await RelicCmd.Obtain(ModelDb.Relic<PandorasBox>().ToMutable(),p);
        await Until(()=>Screen() is NSimpleCardsViewScreen,"native Pandora result viewer");
        var viewer=(NSimpleCardsViewScreen)Screen()!;
        var results=(List<CardPileAddResult>)GameActions.Field(viewer,"_cardResults")!;
        Check(results.Count==basics.Length&&!p.Deck.Cards.Intersect(basics).Any(),
            "native Pandora transforms every eligible basic before result confirmation");
        var transformed=p.Deck.Cards.ToArray();
        Check(transformed.Length==before.Length&&results.All(r=>transformed.Contains(r.cardAdded)),
            "transformed cards already belong to deck");
        await Delay(3);
        Check(ReferenceEquals(Screen(),viewer),"advice mode leaves result viewer open");
        service.Panel.SetReviewPaused(true);auto.Enable(true);await Delay(3);
        Check(ReferenceEquals(Screen(),viewer),"review pause leaves result viewer open");
        service.Panel.SetReviewPaused(false);
        await Delay(.5);auto.Pause("test cancellation before result confirmation");await Delay(3);
        Check(ReferenceEquals(Screen(),viewer),"stop cancels pending result confirmation");
        ClimbController.SetEnabled(true);
        await Until(()=>Screen() is NMapScreen,"loop closes results and resumes map",30);
        Check(ClimbController.Enabled&&auto.Enabled&&auto.FullAutomatic&&!auto.WaitingForManual,
            "loop remains active and resumes after native Pandora results");
        Check(transformed.SequenceEqual(p.Deck.Cards),"result confirmation neither transforms again nor changes deck");
        ClimbController.Stop("fixture boundary");NMapScreen.Instance!.Close(false);await Delay(.3);
        // Reopen the same native read-only view with a new instance; ordinary takeover also works.
        var second=NSimpleCardsViewScreen.ShowScreen(results,new LocString("relics","PANDORAS_BOX.infoText"));
        await Until(()=>ReferenceEquals(Screen(),second),"second result viewer");
        auto.Enable();
        await Until(()=>Screen() is NMapScreen,"ordinary takeover resumes map",30);
        Check(auto.Enabled&&!auto.FullAutomatic&&!auto.WaitingForManual,
            "ordinary takeover confirms a new result viewer instance");
        Check(transformed.SequenceEqual(p.Deck.Cards),"repeated result display leaves deck unchanged");
        auto.Pause("fixture complete");
    }
}
