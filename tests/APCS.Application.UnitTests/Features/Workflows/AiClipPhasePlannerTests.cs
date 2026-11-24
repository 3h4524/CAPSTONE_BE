using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiClipPhasePlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly AiClipPhaseSettings Settings = new()
    {
        MaxInFlight = 3, MaxInFlightPerUser = 2, FirstPollDelaySeconds = 30, SceneTimeoutMinutes = 25, MaxBusyAttempts = 3
    };

    private static AiShotJobState Scene(int order, string status = "pending", DateTime? submittedAt = null, DateTime? nextAttemptAt = null, int busy = 0) =>
        new(order, $"f{order}", status == "pending" ? null : $"op{order}", Status: status, SubmittedAt: submittedAt, NextAttemptAt: nextAttemptAt, BusyAttempts: busy);

    private static AiClipJob Job(Guid user, params AiShotJobState[] scenes) => new(Guid.NewGuid(), user, scenes);

    private static AiClipPlan Plan(params AiClipJob[] jobs) => AiClipPhasePlanner.Plan(jobs, Now, Settings);

    private static IEnumerable<int> Submits(AiClipPlan plan, AiClipJob job) =>
        plan.Work.Where(w => w.JobId == job.JobId && w.Action == AiClipAction.Submit).Select(w => w.SceneOrder);

    private static IEnumerable<int> Polls(AiClipPlan plan, AiClipJob job) =>
        plan.Work.Where(w => w.JobId == job.JobId && w.Action == AiClipAction.Poll).Select(w => w.SceneOrder);

    [TestMethod]
    public void Plan_SceneThatCallsBack_IsNotAskedAboutAtTheUsualPace()
    {
        // Past the first delay that a scene without a callback waits, but well within the callback's safety interval.
        var job = Job(Ann, Scene(0, "polling", submittedAt: Now.AddSeconds(-45)) with { Callback = true });

        Polls(Plan(job), job).Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_SceneThatCallsBackButStaysQuiet_IsAskedAboutAfterTheSafetyInterval()
    {
        var job = Job(Ann,
            Scene(0, "polling", submittedAt: Now.AddMinutes(-2)) with { Callback = true, PolledAt = Now.AddSeconds(-61) },
            Scene(1, "polling", submittedAt: Now.AddMinutes(-2)) with { Callback = true, PolledAt = Now.AddSeconds(-20) });

        Polls(Plan(job), job).Should().Equal(0);
    }

    [TestMethod]
    public void Plan_SceneTheProviderSaysIsFinished_IsAskedAboutAtOnce()
    {
        // Seconds after it was submitted: the call says it finished, so it is not left to wait its turn.
        var job = Job(Ann, Scene(0, "polling", submittedAt: Now.AddSeconds(-5)) with { Callback = true, AnnouncedAt = Now });

        Polls(Plan(job), job).Should().Equal(0);
    }

    [TestMethod]
    public void Plan_NothingToLookAfter_DoesNothing()
    {
        var plan = Plan();

        plan.Work.Should().BeEmpty();
        plan.Settlements.Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_AllScenesStored_HandsTheJobToTheWorker()
    {
        var job = Job(Ann, Scene(0, "stored"), Scene(1, "stored"));

        var plan = Plan(job);

        plan.Settlements.Should().ContainSingle().Which.Should().Be(new AiClipSettlement(job.JobId, AiClipSettlementKind.Complete));
        plan.Work.Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_SceneRefusedByTheProvider_FailsTheJobAsRejected()
    {
        var job = Job(Ann, Scene(0, "stored"), Scene(1, "failed"), Scene(2));

        var plan = Plan(job);

        plan.Settlements.Should().ContainSingle().Which.Should().Be(new AiClipSettlement(job.JobId, AiClipSettlementKind.Fail, "AiShotRejected"));
        plan.Work.Should().BeEmpty("a failed job is not given more work");
    }

    [TestMethod]
    public void Plan_SceneLeftSubmitting_FailsTheJobAsUncertainAndNeverSendsItAgain()
    {
        var job = Job(Ann, Scene(0, "submitting"));

        var plan = Plan(job);

        plan.Settlements.Should().ContainSingle().Which.Code.Should().Be("AiVideoSubmissionUncertain");
        plan.Work.Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_ProviderKeptTurningTheSceneAway_FailsTheJobAsBusy()
    {
        var job = Job(Ann, Scene(0, busy: 3));

        Plan(job).Settlements.Should().ContainSingle().Which.Code.Should().Be("AiVideoBusy");
    }

    [TestMethod]
    public void Plan_SceneNotFinishedInTime_FailsTheJob()
    {
        var job = Job(Ann, Scene(0, "polling", submittedAt: Now.AddMinutes(-26)));

        Plan(job).Settlements.Should().ContainSingle().Which.Code.Should().Be("AiShotFailed");
    }

    [TestMethod]
    public void Plan_ClipFinishedButNeverStoredInTime_FailsTheJob()
    {
        var job = Job(Ann, Scene(0, "ready", submittedAt: Now.AddMinutes(-26)));

        Plan(job).Settlements.Should().ContainSingle().Which.Code.Should().Be("AiShotFailed");
    }

    [TestMethod]
    public void Plan_SceneWithinItsTime_IsNotGivenUp()
    {
        var job = Job(Ann, Scene(0, "polling", submittedAt: Now.AddMinutes(-24)));

        Plan(job).Settlements.Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_SceneJustSubmitted_IsNotAskedAboutBeforeItHadTimeToFinish()
    {
        var job = Job(Ann, Scene(0, "polling", submittedAt: Now.AddSeconds(-29)));

        Plan(job).Work.Should().BeEmpty();
    }

    [TestMethod]
    public void Plan_SceneSubmittedLongEnoughAgo_IsAskedAbout()
    {
        var job = Job(Ann, Scene(0, "polling", submittedAt: Now.AddSeconds(-30)));

        Plan(job).Work.Should().ContainSingle().Which.Should().Be(new AiClipWork(job.JobId, 0, AiClipAction.Poll));
    }

    [TestMethod]
    public void Plan_FinishedClip_IsStoredAtOnce()
    {
        var job = Job(Ann, Scene(0, "ready", submittedAt: Now.AddMinutes(-3)), Scene(1, "stored"));

        Plan(job).Work.Should().ContainSingle().Which.Should().Be(new AiClipWork(job.JobId, 0, AiClipAction.Store));
    }

    [TestMethod]
    public void Plan_SceneTheProviderTurnedAwayForNow_WaitsUntilItsTimeToTryAgain()
    {
        var waiting = Job(Ann, Scene(0, busy: 1, nextAttemptAt: Now.AddSeconds(5)));
        var due = Job(Ben, Scene(0, busy: 1, nextAttemptAt: Now));

        var plan = Plan(waiting, due);

        Submits(plan, waiting).Should().BeEmpty();
        Submits(plan, due).Should().Equal(0);
    }

    [TestMethod]
    public void Plan_ProviderRoomIsShared_SoNoMoreThanTheLimitIsBeingMadeAtOnce()
    {
        // Two are already being made, so one more fits within the limit of three.
        var running = Job(Ann, Scene(0, "polling", submittedAt: Now.AddMinutes(-1)), Scene(1, "polling", submittedAt: Now.AddMinutes(-1)));
        var waiting = Job(Ben, Scene(0), Scene(1), Scene(2));

        var plan = Plan(running, waiting);

        Submits(plan, waiting).Should().Equal(0);
    }

    [TestMethod]
    public void Plan_OneUserHasMoreScenesWaitingThanTheirShare_TheRestWaitEvenWhenThereIsRoom()
    {
        var ann = Job(Ann, Scene(0), Scene(1), Scene(2), Scene(3));

        // Ann may have two at once although the provider could take three.
        Submits(Plan(ann), ann).Should().Equal(0, 1);
    }

    [TestMethod]
    public void Plan_UsersTakeTurnsStartingWithWhoHasFewestBeingMade()
    {
        var annRunning = Job(Ann, Scene(0, "polling", submittedAt: Now.AddMinutes(-1)));
        var annWaiting = Job(Ann, Scene(0), Scene(1));
        var benWaiting = Job(Ben, Scene(0), Scene(1));

        var plan = Plan(annRunning, annWaiting, benWaiting);

        // Two more fit within the limit of three. Ben has none being made so he goes first; then each has one and Ann follows.
        Submits(plan, benWaiting).Should().Equal(0);
        Submits(plan, annWaiting).Should().Equal(0);
    }

    [TestMethod]
    public void Plan_OneUserAlreadyHasTheirShareBeingMade_TheLastSlotGoesToTheOther()
    {
        var annRunning = Job(Ann, Scene(0, "polling", submittedAt: Now.AddMinutes(-1)), Scene(1, "polling", submittedAt: Now.AddMinutes(-1)));
        var annWaiting = Job(Ann, Scene(0));
        var benWaiting = Job(Ben, Scene(0), Scene(1));

        var plan = Plan(annRunning, annWaiting, benWaiting);

        Submits(plan, annWaiting).Should().BeEmpty();
        Submits(plan, benWaiting).Should().Equal(0);
    }

    [TestMethod]
    public void Plan_StoringAndAskingNeedNoRoomAtTheProvider()
    {
        var full = Job(Ann, Scene(0, "polling", submittedAt: Now.AddMinutes(-1)), Scene(1, "polling", submittedAt: Now.AddMinutes(-1)));
        var other = Job(Ben, Scene(0, "polling", submittedAt: Now.AddMinutes(-1)), Scene(1, "ready", submittedAt: Now.AddMinutes(-2)), Scene(2));

        var plan = Plan(full, other);

        plan.Work.Count(w => w.Action == AiClipAction.Poll).Should().Be(3);
        plan.Work.Should().Contain(new AiClipWork(other.JobId, 1, AiClipAction.Store));
        plan.Work.Should().NotContain(w => w.Action == AiClipAction.Submit, "three scenes are already being made");
    }

    [TestMethod]
    public void Plan_JobsOfOtherUsersAreSettledIndependently()
    {
        var done = Job(Ann, Scene(0, "stored"));
        var failed = Job(Ben, Scene(0, "failed"));
        var running = Job(Ben, Scene(0, "polling", submittedAt: Now.AddMinutes(-1)));

        var plan = Plan(done, failed, running);

        plan.Settlements.Should().HaveCount(2);
        plan.Work.Should().ContainSingle().Which.Should().Be(new AiClipWork(running.JobId, 0, AiClipAction.Poll));
    }
}
