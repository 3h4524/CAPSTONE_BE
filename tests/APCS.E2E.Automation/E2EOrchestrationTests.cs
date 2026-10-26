using System;
using System.Threading.Tasks;

namespace APCS.E2E;

[TestClass]
public sealed class E2EOrchestrationTests
{
    [TestMethod]
    public async Task OrchestrationVerb_CarriesOutTheRequestedFlow()
    {
        var options = E2EOptions.FromEnvironment();
        var orchestrator = new E2EOrchestrator(options);

        var outcome = await orchestrator.RunAsync();

        if (options.Command == "test")
        {
            Assert.IsNotNull(outcome);
            Assert.IsTrue(outcome.ExitCode == 0, $"the suite exited {outcome.ExitCode}: {outcome.LogTail}");
            Assert.IsTrue(outcome.Total > 0, $"no tests showed up in {options.ReportPath}");
            Assert.AreEqual(0, outcome.Failed, $"failures in the suite: {outcome.LogTail}");
        }
    }
}
