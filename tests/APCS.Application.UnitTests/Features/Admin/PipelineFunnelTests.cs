using System;
using System.Collections.Generic;
using APCS.Application.Features.Admin.SubscriptionPlans;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PipelineFunnelTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [TestMethod]
    public void Nest_AStepReachedWithoutTheOneBefore_DoesNotCount()
    {
        // C has a mockup but no approved design: it stops at "designed", so mockups never exceed approvals.
        var counts = PipelineFunnel.Nest(new List<IReadOnlySet<Guid>>
        {
            new HashSet<Guid> { A, B, C },
            new HashSet<Guid> { A, B, C },
            new HashSet<Guid> { A, B },
            new HashSet<Guid> { A, B, C },
        });

        counts.Should().Equal(3, 3, 2, 2);
    }

    [TestMethod]
    public void Nest_NeverGrowsAndStopsAtTheFirstEmptyStep()
    {
        var counts = PipelineFunnel.Nest(new List<IReadOnlySet<Guid>>
        {
            new HashSet<Guid> { A, B },
            new HashSet<Guid>(),
            new HashSet<Guid> { A, B },
        });

        counts.Should().Equal(2, 0, 0);
    }

    [TestMethod]
    public void Nest_NoSteps_IsEmpty()
    {
        PipelineFunnel.Nest(new List<IReadOnlySet<Guid>>()).Should().BeEmpty();
    }
}
