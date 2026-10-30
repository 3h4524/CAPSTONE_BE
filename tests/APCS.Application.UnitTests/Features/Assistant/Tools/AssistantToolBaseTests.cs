using APCS.Application.Features.Assistant.Tools;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class AssistantToolBaseTests
{
    private sealed record Args(string? Name, int? Count);

    private sealed class EchoTool : AssistantTool<Args>
    {
        public override string Name => "echo";

        public override AssistantToolKind Kind => AssistantToolKind.Query;

        protected override string Description => "echo";

        protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

        public override bool IsAvailable(AssistantToolContext context) => true;

        protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, Args arguments, CancellationToken cancellationToken) =>
            Task.FromResult(AssistantToolOutcome.Ok(new { arguments.Name, arguments.Count }));
    }

    [TestMethod]
    public void Declaration_QueryTool_HasNoMoreRequestsArgument() =>
        new EchoTool().Declaration.ParametersSchema.GetProperty("properties").TryGetProperty("moreRequests", out _).Should().BeFalse();

    [TestMethod]
    [DataRow(AssistantToolKind.ClientAction)]
    [DataRow(AssistantToolKind.Confirmation)]
    [DataRow(AssistantToolKind.Interaction)]
    public void Build_ActionToolSchema_AddsBooleanMoreRequestsAndKeepsOtherProperties(AssistantToolKind kind)
    {
        var schema = AssistantToolSchemas.Build("""{"type":"object","properties":{"reply":{"type":"string"}},"required":["reply"]}""", kind);

        var properties = schema.GetProperty("properties");
        properties.GetProperty("moreRequests").GetProperty("type").GetString().Should().Be("boolean");
        properties.TryGetProperty("reply", out _).Should().BeTrue();
        schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Equal("reply");
    }

    [TestMethod]
    [DataRow("""{"moreRequests":true}""", true)]
    [DataRow("""{"moreRequests":false}""", false)]
    [DataRow("""{"moreRequests":"true"}""", false)]
    [DataRow("""{}""", false)]
    public void RequestsMore_IsTrueOnlyForABooleanTrue(string arguments, bool expected) =>
        AssistantToolSchemas.RequestsMore(AssistantTestSupport.Json(arguments)).Should().Be(expected);

    private static AssistantToolContext Context() => new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller, null);

    [TestMethod]
    public async Task ExecuteAsync_InvalidArguments_ReturnsInvalidArgumentsError()
    {
        var outcome = await new EchoTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"count":"abc"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("invalid_arguments");
    }

    [TestMethod]
    public async Task ExecuteAsync_UndefinedArguments_TreatedAsEmptyObject()
    {
        var outcome = await new EchoTool().ExecuteAsync(Context(), default, default);

        outcome.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExecuteAsync_ValidArguments_PassesParsedValues()
    {
        var outcome = await new EchoTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"name":"x","count":2}"""), default);

        outcome.ModelResultJson.Should().Contain("\"x\"").And.Contain("2");
    }

    [TestMethod]
    public void Declaration_ParsesSchemaAndExposesName()
    {
        var declaration = new EchoTool().Declaration;

        declaration.Name.Should().Be("echo");
        declaration.ParametersSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [TestMethod]
    public void Ok_TruncatesOversizedResults()
    {
        var outcome = AssistantToolOutcome.Ok(new { text = new string('a', 20000) });

        outcome.ModelResultJson.Length.Should().BeLessThan(13000);
        outcome.ModelResultJson.Should().Contain("truncated");
    }
}
