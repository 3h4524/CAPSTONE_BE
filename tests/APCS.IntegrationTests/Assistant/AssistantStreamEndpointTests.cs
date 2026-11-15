using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Persistence;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StackExchange.Redis;

namespace APCS.IntegrationTests.Assistant;

[TestClass]
public sealed class AssistantStreamEndpointTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly ScriptedChatModel Model = new();
    private static IntegrationTestHost _host = null!;
    private static WebApplicationFactory<Program>? _assistantHost;

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
        if (_host.IsAvailable)
        {
            _assistantHost = _host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAssistantChatModel>();
                services.AddSingleton<IAssistantChatModel>(Model);
            }));
        }
    }

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        if (_assistantHost is not null)
            await _assistantHost.DisposeAsync();
        await _host.DisposeAsync();
    }

    [TestInitialize]
    public async Task ResetAsync()
    {
        Model.Reset();
        await _host.ResetDatabaseAsync();
        await DedicatedDatabaseReset.TruncateConfiguredAsync();
    }

    [TestMethod]
    public async Task Reattach_AfterTheClientDisconnected_ReceivesTheRemainingEventsOfTheUsersOwnTurn()
    {
        _host.EnsureAvailable();
        var owner = await SellerAsync("Owner batch");
        await SellerAsync("Stranger batch");
        var toolGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Model.Enqueue((_, cancellationToken) => FirstRound(toolGate.Task, cancellationToken));
        Model.Enqueue((request, _) => EchoToolResult(request));

        string conversationId;
        string assistantMessageId;
        string lastSeenId;
        using (var chat = await StartChatAsync(owner))
        {
            var reader = new SseReader(await chat.Content.ReadAsStreamAsync());
            var opening = await reader.NextAsync();
            opening!.Type.Should().Be("conversation");
            conversationId = opening.Data.GetProperty("conversationId").GetString()!;
            assistantMessageId = opening.Data.GetProperty("assistantMessageId").GetString()!;
            var firstDelta = await reader.NextAsync();
            firstDelta!.Type.Should().Be("delta");
            lastSeenId = firstDelta.Id;
        }

        toolGate.SetResult();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/assistant/messages/{assistantMessageId}/stream");
        request.Headers.Add("Last-Event-ID", lastSeenId);
        using var resumed = await owner.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        var remaining = await new SseReader(await resumed.Content.ReadAsStreamAsync()).ReadToEndAsync().WaitAsync(Patience);

        remaining.Select(item => item.Type).Should().Equal("tool_started", "tool_finished", "delta", "done");
        remaining.Should().OnlyContain(item => CompareIds(item.Id, lastSeenId) > 0);
        var answer = remaining.Single(item => item.Type == "delta").Data.GetProperty("text").GetString();
        answer.Should().Contain("Owner batch").And.NotContain("Stranger batch");
        remaining.Last().Data.GetProperty("assistantMessageId").GetString().Should().Be(assistantMessageId);

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/assistant/conversations/{conversationId}");
        var messages = detail.GetProperty("messages").EnumerateArray().ToArray();
        messages.Select(message => message.GetProperty("role").GetString()).Should().Equal("user", "assistant");
        messages[1].GetProperty("id").GetString().Should().Be(assistantMessageId);
        messages[1].GetProperty("status").GetString().Should().Be("done");
        messages[1].GetProperty("content").GetString().Should().StartWith("Let me look.").And.Contain("Owner batch");
    }

    [TestMethod]
    public async Task Stop_EndsTheRunningTurnAsStoppedAndKeepsThePartialAnswer()
    {
        _host.EnsureAvailable();
        var owner = await SellerAsync("Batch");
        Model.Enqueue((_, cancellationToken) => Hang("partial", cancellationToken));

        using var chat = await StartChatAsync(owner);
        var reader = new SseReader(await chat.Content.ReadAsStreamAsync());
        var opening = await reader.NextAsync();
        var conversationId = opening!.Data.GetProperty("conversationId").GetString()!;
        var assistantMessageId = opening.Data.GetProperty("assistantMessageId").GetString()!;
        (await reader.NextAsync())!.Type.Should().Be("delta");

        using var second = await owner.PostAsJsonAsync("/api/assistant/chat", ChatBody("again", conversationId));
        (await ProblemResponses.ReadCodeAsync(second, HttpStatusCode.Conflict)).Should().Be("assistant.turn_in_progress");

        using var stop = await owner.PostAsync($"/api/assistant/messages/{assistantMessageId}/stop", null);
        stop.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var rest = await reader.ReadToEndAsync().WaitAsync(Patience);

        rest.Last().Type.Should().Be("error");
        rest.Last().Data.GetProperty("code").GetString().Should().Be("assistant.stopped");
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/assistant/conversations/{conversationId}");
        var assistant = detail.GetProperty("messages").EnumerateArray().Last();
        assistant.GetProperty("status").GetString().Should().Be("stopped");
        assistant.GetProperty("content").GetString().Should().Be("partial");
        using var again = await owner.PostAsync($"/api/assistant/messages/{assistantMessageId}/stop", null);
        (await ProblemResponses.ReadCodeAsync(again, HttpStatusCode.Conflict)).Should().Be("assistant.not_streaming");
    }

    [TestMethod]
    public async Task StreamAndStop_ForAnotherUsersMessage_ReturnNotFound()
    {
        _host.EnsureAvailable();
        var owner = await SellerAsync("Batch");
        var stranger = await SellerAsync("Other");
        Model.Enqueue((_, cancellationToken) => Hang("partial", cancellationToken));
        using var chat = await StartChatAsync(owner);
        var reader = new SseReader(await chat.Content.ReadAsStreamAsync());
        var assistantMessageId = (await reader.NextAsync())!.Data.GetProperty("assistantMessageId").GetString()!;

        using var stream = await stranger.GetAsync($"/api/assistant/messages/{assistantMessageId}/stream");
        using var stop = await stranger.PostAsync($"/api/assistant/messages/{assistantMessageId}/stop", null);

        (await ProblemResponses.ReadCodeAsync(stream, HttpStatusCode.NotFound)).Should().Be("assistant.message_not_found");
        (await ProblemResponses.ReadCodeAsync(stop, HttpStatusCode.NotFound)).Should().Be("assistant.message_not_found");
        using var ownStop = await owner.PostAsync($"/api/assistant/messages/{assistantMessageId}/stop", null);
        ownStop.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await reader.ReadToEndAsync().WaitAsync(Patience);
    }

    [TestMethod]
    public async Task Stream_ReplaysAFinishedTurnUntilItsBufferExpires()
    {
        _host.EnsureAvailable();
        var owner = await SellerAsync("Batch");
        Model.Enqueue((_, _) => Answer("All done."));
        string assistantMessageId;
        using (var chat = await StartChatAsync(owner))
        {
            var events = await new SseReader(await chat.Content.ReadAsStreamAsync()).ReadToEndAsync().WaitAsync(Patience);
            events.Select(item => item.Type).Should().Equal("conversation", "delta", "done");
            assistantMessageId = events[0].Data.GetProperty("assistantMessageId").GetString()!;
        }

        using (var replay = await owner.GetAsync($"/api/assistant/messages/{assistantMessageId}/stream", HttpCompletionOption.ResponseHeadersRead))
        {
            replay.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
            var events = await new SseReader(await replay.Content.ReadAsStreamAsync()).ReadToEndAsync().WaitAsync(Patience);
            events.Select(item => item.Type).Should().Equal("conversation", "delta", "done");
        }

        var redis = _assistantHost!.Services.GetRequiredService<IConnectionMultiplexer>();
        await redis.GetDatabase().KeyDeleteAsync($"APCS:test:assistant:turn:{assistantMessageId}");
        using var expired = await owner.GetAsync($"/api/assistant/messages/{assistantMessageId}/stream");
        (await ProblemResponses.ReadCodeAsync(expired, HttpStatusCode.NotFound)).Should().Be("assistant.stream_not_found");
    }

    [TestMethod]
    public async Task StartupSweep_MarksOnlyStreamingAnswersOlderThanTheCutoffAsInterrupted()
    {
        _host.EnsureAvailable();
        var owner = await SellerAsync("Batch");
        Model.Enqueue((_, _) => Answer("Hi."));
        using (var chat = await StartChatAsync(owner))
            await new SseReader(await chat.Content.ReadAsStreamAsync()).ReadToEndAsync().WaitAsync(Patience);
        var connectionString = _assistantHost!.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var conversationId = await ScalarAsync<Guid>(connection, "SELECT id FROM public.assistant_conversations LIMIT 1");
        var stale = await InsertStreamingRowAsync(connection, conversationId, DateTime.UtcNow.AddMinutes(-10));
        var live = await InsertStreamingRowAsync(connection, conversationId, DateTime.UtcNow);

        using (var scope = _assistantHost.Services.CreateScope())
        {
            var swept = await scope.ServiceProvider.GetRequiredService<IAssistantConversationRepository>()
                .MarkStreamingMessagesInterruptedAsync(DateTime.UtcNow.AddMinutes(-4));
            swept.Should().Be(1);
        }

        (await ScalarAsync<string>(connection, $"SELECT metadata->>'status' FROM public.assistant_messages WHERE id = '{stale}'")).Should().Be("interrupted");
        (await ScalarAsync<string>(connection, $"SELECT metadata->>'kind' FROM public.assistant_messages WHERE id = '{stale}'")).Should().Be("chat");
        (await ScalarAsync<string>(connection, $"SELECT metadata->>'status' FROM public.assistant_messages WHERE id = '{live}'")).Should().Be("streaming");
        (await ScalarAsync<long>(connection, "SELECT count(*) FROM public.assistant_messages WHERE metadata->>'status' = 'done'")).Should().Be(1);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> InsertStreamingRowAsync(NpgsqlConnection connection, Guid conversationId, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.assistant_messages (id, conversation_id, role, content, metadata, created_at)
            VALUES (@id, @conversation, 'assistant', '', '{"kind":"chat","status":"streaming"}'::jsonb, @created)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("conversation", conversationId);
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<HttpClient> SellerAsync(string batchName)
    {
        var account = await TestAccount.SignInAsync(_host, TestAccount.Unique("podi") + "@apcs.test");
        account.Client.Dispose();
        var client = _assistantHost!.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = TestAccount.SecureOrigin, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.MintToken([TestAuth.SellerRole], account.UserId, account.Email));
        using var batch = await client.PostAsJsonAsync("/api/batches", new { name = batchName, description = "Fixture batch" });
        batch.StatusCode.Should().Be(HttpStatusCode.OK, await batch.Content.ReadAsStringAsync());
        return client;
    }

    private static object ChatBody(string message, string? conversationId = null) => new
    {
        conversationId,
        message,
        context = new
        {
            route = "/workflows",
            canvas = new { workflowId = Guid.NewGuid(), revision = 1, isDirty = false, nodes = Array.Empty<object>(), edges = Array.Empty<object>() }
        }
    };

    private static async Task<HttpResponseMessage> StartChatAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/chat") { Content = JsonContent.Create(ChatBody("What batches do I have?")) };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK, response.StatusCode == HttpStatusCode.OK ? null : await response.Content.ReadAsStringAsync());
        return response;
    }

    private static int CompareIds(string left, string right)
    {
        static (long Time, long Sequence) Parse(string id)
        {
            var parts = id.Split('-');
            return (long.Parse(parts[0]), long.Parse(parts[1]));
        }

        return Parse(left).CompareTo(Parse(right));
    }

    private static AssistantModelUpdate Completed(string text, params AssistantToolCall[] calls) =>
        AssistantModelUpdate.Completed(new AssistantModelMessage(AssistantMessageRole.Assistant, text, calls), 1, 1, "scripted-model");

    private static async IAsyncEnumerable<AssistantModelUpdate> FirstRound(Task gate, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return AssistantModelUpdate.TextDelta("Let me look.");
        await gate.WaitAsync(cancellationToken);
        yield return Completed("Let me look.", new AssistantToolCall("call-1", "list_batches", JsonDocument.Parse("{}").RootElement.Clone()));
    }

    private static async IAsyncEnumerable<AssistantModelUpdate> EchoToolResult(AssistantModelRequest request)
    {
        await Task.Yield();
        var result = request.Messages.Last(message => message.ToolResult is not null).ToolResult!.Json;
        yield return AssistantModelUpdate.TextDelta(result);
        yield return Completed(result);
    }

    private static async IAsyncEnumerable<AssistantModelUpdate> Answer(string text)
    {
        await Task.Yield();
        yield return AssistantModelUpdate.TextDelta(text);
        yield return Completed(text);
    }

    private static async IAsyncEnumerable<AssistantModelUpdate> Hang(string text, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return AssistantModelUpdate.TextDelta(text);
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return Completed(text);
    }

    private sealed record SseEvent(string Id, string Type, JsonElement Data);

    private sealed class SseReader(Stream stream)
    {
        private readonly StreamReader reader = new(stream);

        public async Task<SseEvent?> NextAsync()
        {
            string? id = null;
            string? type = null;
            string? data = null;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Length == 0)
                {
                    if (type is not null)
                        return new SseEvent(id ?? string.Empty, type, JsonDocument.Parse(data ?? "{}").RootElement.Clone());
                    continue;
                }

                if (line.StartsWith("id: ", StringComparison.Ordinal))
                    id = line[4..];
                else if (line.StartsWith("event: ", StringComparison.Ordinal))
                    type = line[7..];
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    data = line[6..];
            }

            return null;
        }

        public async Task<List<SseEvent>> ReadToEndAsync()
        {
            var events = new List<SseEvent>();
            while (await NextAsync() is { } next)
                events.Add(next);
            return events;
        }
    }

    private sealed class ScriptedChatModel : IAssistantChatModel
    {
        private readonly ConcurrentQueue<Func<AssistantModelRequest, CancellationToken, IAsyncEnumerable<AssistantModelUpdate>>> rounds = new();

        public string ModelName => "scripted-model";

        public void Reset() => rounds.Clear();

        public void Enqueue(Func<AssistantModelRequest, CancellationToken, IAsyncEnumerable<AssistantModelUpdate>> round) => rounds.Enqueue(round);

        public IAsyncEnumerable<AssistantModelUpdate> StreamAsync(AssistantModelRequest request, CancellationToken cancellationToken = default) =>
            rounds.TryDequeue(out var round) ? round(request, cancellationToken) : Answer("DONE");
    }
}
