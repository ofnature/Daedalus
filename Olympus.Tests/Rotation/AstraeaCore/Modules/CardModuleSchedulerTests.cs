using Moq;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.AstraeaCore.Modules;
using Olympus.Services.Action;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Olympus.Tests.Rotation.AstraeaCore.Modules;

public class CardModuleSchedulerTests
{
    private readonly CardModule _module = new();

    [Fact]
    public void CollectCandidates_PlayCard_PushesOnlyHeldCards()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCards = true;
        config.Astrologian.DumpCardsWhenIdle = true;
        config.Astrologian.CardsUnderDivinationOnly = false;

        var cardService = AstraeaTestContext.CreateMockCardService(
            hasCard: true,
            currentCard: ASTActions.CardType.TheBalance);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        var partyHelper = AstraeaTestContext.CreatePartyWithInjured(healthyCount: 3, injuredCount: 1);

        var context = AstraeaTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            cardService: cardService,
            level: 100,
            hasCard: true,
            inCombat: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Contains(queue, c => c.Behavior.Action.ActionId == ASTActions.TheBalance.ActionId);
        Assert.DoesNotContain(queue, c => c.Behavior.Action.ActionId == ASTActions.TheSpear.ActionId);
    }

    [Fact]
    public void CollectCandidates_PlayCard_NoCardInHand_PushesNoCards()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCards = true;

        var cardService = AstraeaTestContext.CreateMockCardService(hasCard: false);
        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AstraeaTestContext.Create(
            config: config,
            actionService: actionService,
            cardService: cardService,
            level: 100,
            hasCard: false,
            inCombat: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.DoesNotContain(queue, c => c.Behavior.Action.ActionId == ASTActions.TheBalance.ActionId);
    }

    [Fact]
    public void CollectCandidates_Draw_InCombat_PushesWhenActiveDrawAllows()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCards = true;

        var cardService = AstraeaTestContext.CreateMockCardService(hasCard: false);
        cardService.Setup(x => x.CanAstralDraw).Returns(true);
        cardService.Setup(x => x.CanUmbralDraw).Returns(true);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AstraeaTestContext.Create(
            config: config,
            actionService: actionService,
            cardService: cardService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Contains(queue, c => c.Behavior.Action.ActionId == ASTActions.AstralDraw.ActionId);
        Assert.Contains(queue, c => c.Behavior.Action.ActionId == ASTActions.UmbralDraw.ActionId);
    }

    [Fact]
    public void CollectCandidates_Draw_OutOfCombat_PushesNoDrawCandidates()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCards = true;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AstraeaTestContext.Create(
            config: config,
            actionService: actionService,
            level: 100,
            inCombat: false,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.DoesNotContain(queue, c => c.Behavior.Action.ActionId == ASTActions.AstralDraw.ActionId);
    }

    [Fact]
    public void CollectCandidates_CardsDisabled_PushesNothing()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCards = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);

        var context = AstraeaTestContext.Create(
            config: config,
            actionService: actionService,
            level: 100,
            hasCard: true,
            inCombat: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void CollectCandidates_Divination_OnCooldownMode_PushesAtHighestPriority()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCards = true;
        config.Astrologian.EnableDivination = true;
        config.Astrologian.DivinationOnBurst = false;

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        actionService.Setup(x => x.IsActionReady(ASTActions.Divination.ActionId)).Returns(true);

        var context = AstraeaTestContext.Create(
            config: config,
            actionService: actionService,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        var divCandidate = Assert.Single(queue, c => c.Behavior.Action.ActionId == ASTActions.Divination.ActionId);
        Assert.Equal(0, divCandidate.Priority);
    }
}
