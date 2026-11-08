using APCS.Application.Features.BatchProductPrompts.Common;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.BatchProductPrompts;

[TestClass]
public sealed class DesignPromptVersionsTests
{
    [TestMethod]
    public void All_IsOrderedByFirstUse_AndEndsWithTheCurrentVersion()
    {
        var starts = DesignPromptVersions.All.Skip(1).Select(version => version.SinceUtc!.Value).ToList();

        DesignPromptVersions.All[0].SinceUtc.Should().BeNull();
        starts.Should().BeInAscendingOrder();
        DesignPromptVersions.All[^1].Key.Should().Be(DesignPromptVersions.Current);
    }

    [TestMethod]
    [DataRow("2026-10-01T00:00:00Z", DesignPromptVersions.Original)]
    [DataRow("2026-10-20T07:51:00Z", DesignPromptVersions.Original)]
    [DataRow("2026-10-23T15:05:00Z", DesignPromptVersions.LetteringRules)]
    [DataRow("2026-10-25T08:34:00Z", DesignPromptVersions.LetteringRules)]
    [DataRow("2026-10-30T11:15:00Z", DesignPromptVersions.RuleFixes)]
    [DataRow("2026-10-30T15:00:00Z", DesignPromptVersions.KeywordRules)]
    public void Resolve_UnlabelledImageFromBeforeLabelling_UsesTheVersionOfItsDay(string createdAt, string expected)
    {
        DesignPromptVersions.Resolve(null, DateTime.Parse(createdAt).ToUniversalTime()).Should().Be(expected);
    }

    [TestMethod]
    public void Resolve_ImageCarryingALabel_KeepsIt_WhateverItsDay()
    {
        DesignPromptVersions.Resolve(DesignPromptVersions.RuleFixes, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be(DesignPromptVersions.RuleFixes);
    }

    [TestMethod]
    public void Resolve_UnlabelledImageFromAfterLabelling_IsUnlabeled_NotTheCurrentVersion()
    {
        DesignPromptVersions.Resolve(null, DesignPromptVersions.LabelingStartedUtc.AddHours(1))
            .Should().Be(DesignPromptVersions.Unlabeled);
    }

    [TestMethod]
    public void AtDate_AfterLabelling_IsTheCurrentVersion()
    {
        DesignPromptVersions.AtDate(DesignPromptVersions.LabelingStartedUtc.AddDays(3))
            .Should().Be(DesignPromptVersions.Current);
    }

    [TestMethod]
    public void OrderOf_PutsUnknownVersionsAfterKnownOnes_AndUnlabelledLast()
    {
        DesignPromptVersions.OrderOf(DesignPromptVersions.Original).Should().Be(0);
        DesignPromptVersions.OrderOf("v9-future").Should().BeGreaterThan(DesignPromptVersions.OrderOf(DesignPromptVersions.Current));
        DesignPromptVersions.OrderOf(DesignPromptVersions.Unlabeled).Should().BeGreaterThan(DesignPromptVersions.OrderOf("v9-future"));
    }
}
