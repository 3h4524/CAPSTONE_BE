using System.Net;
using System.Text;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

/// <summary>
/// The platform's Ideogram keys from end to end: the real pool and usage counts, the real Ideogram client, and a fake
/// Ideogram API that answers each key the way the test says. Shows when an image moves on to the second key.
/// </summary>
[TestClass]
public sealed class SystemApiKeyRotationTests
{
    private const string KeyOne = "ideogram-test-key-1";
    private const string KeyTwo = "ideogram-test-key-2";
    private static readonly DateTimeOffset Noon = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Noon.UtcDateTime);
    private static readonly byte[] Image = [0x89, 0x50, 0x4E, 0x47];

    private readonly InMemorySystemApiKeyUsageStore usage = new();
    private readonly Dictionary<string, int> requestsPerKey = new() { [KeyOne] = 0, [KeyTwo] = 0 };
    // How the fake Ideogram API answers a new image on each key; a key not listed makes the image.
    private readonly Dictionary<string, Func<HttpResponseMessage>> refusals = [];

    [TestMethod]
    public async Task Fill_TheDefault_MakesEveryImageOnKeyOne_WhileItHasUsesLeft()
    {
        var pool = Pool(dailyLimit: 10);

        for (var i = 0; i < 6; i++)
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();

        requestsPerKey[KeyOne].Should().Be(6);
        requestsPerKey[KeyTwo].Should().Be(0, "key two is only used once key one is used up, refused or busy");
    }

    [TestMethod]
    public async Task Rotate_MakesTheImagesOnEachKeyInTurn()
    {
        var pool = Pool(dailyLimit: 10, strategy: SystemApiKeyStrategies.Rotate);
        var keysUsed = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            var before = requestsPerKey[KeyOne];
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();
            keysUsed.Add(requestsPerKey[KeyOne] > before ? KeyOne : KeyTwo);
        }

        keysUsed.Should().Equal(KeyOne, KeyTwo, KeyOne, KeyTwo);
    }

    [TestMethod]
    public async Task KeyOneHasMadeItsTenImagesToday_EveryNextImageIsMadeOnKeyTwo()
    {
        var pool = Pool(dailyLimit: 10);
        await UseUp(KeyOne, 10);

        for (var i = 0; i < 5; i++)
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();

        requestsPerKey[KeyOne].Should().Be(0, "key one has no use left today");
        requestsPerKey[KeyTwo].Should().Be(5);
        Used(KeyOne).Should().Be(10);
        Used(KeyTwo).Should().Be(5);
    }

    [TestMethod]
    [DataRow(SystemApiKeyStrategies.Fill)]
    [DataRow(SystemApiKeyStrategies.Rotate)]
    public async Task TwentyImagesOnTwoKeysOfTenADay_EachKeyMakesTen_AndTheTwentyFirstIsRefusedWithoutAsking(string strategy)
    {
        var pool = Pool(dailyLimit: 10, strategy: strategy);

        for (var i = 0; i < 20; i++)
        {
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();
            // Filling, key two is first asked for the eleventh image.
            if (strategy == SystemApiKeyStrategies.Fill && i == 9)
                (requestsPerKey[KeyOne], requestsPerKey[KeyTwo]).Should().Be((10, 0));
        }
        var twentyFirst = await MakeImage(pool);

        requestsPerKey[KeyOne].Should().Be(10);
        requestsPerKey[KeyTwo].Should().Be(10);
        twentyFirst.IsSuccess.Should().BeFalse();
        twentyFirst.StopsJob.Should().BeTrue();
        twentyFirst.ErrorMessage.Should().Be(SystemApiKeyPoolExtensions.UsedUpMessage(ImageGenerationProviders.Ideogram));
        requestsPerKey.Values.Sum().Should().Be(20, "nothing is sent once both keys are used up");
    }

    [TestMethod]
    [DataRow(429, """{"error":"Daily limit reached.","reject_reason":"daily_limit"}""", DisplayName = "429 daily limit")]
    [DataRow(402, """{"error":"Add a payment method and credits to use this API key.","reject_reason":"insufficient_funds"}""", DisplayName = "402 no credits")]
    [DataRow(401, """{"error":"Invalid API key."}""", DisplayName = "401 key no longer accepted")]
    public async Task IdeogramRefusesKeyOne_TheImageIsMadeOnKeyTwo_AndKeyOneIsLeftOutForTheRestOfTheDay(int status, string answer)
    {
        var pool = Pool(dailyLimit: 10);
        refusals[KeyOne] = () => Json((HttpStatusCode)status, answer);

        var first = await MakeImage(pool);
        for (var i = 0; i < 4; i++)
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();

        first.IsSuccess.Should().BeTrue("the image was asked of key two straight after key one was refused");
        requestsPerKey[KeyOne].Should().Be(1, "key one is asked once, then passed over");
        requestsPerKey[KeyTwo].Should().Be(5);
        Used(KeyOne).Should().Be(0, "the refused request is given back");
    }

    [TestMethod]
    public async Task KeyOneAnswersTooManyAtOnce_TheSameKeyIsAskedAgain_NotKeyTwo()
    {
        var pool = Pool(dailyLimit: 10);
        var busyOnce = true;
        refusals[KeyOne] = () =>
        {
            if (!busyOnce) return Answer();
            busyOnce = false;
            return Json(HttpStatusCode.TooManyRequests, """{"error":"Too many requests.","reject_reason":"inflight_limit"}""");
        };

        var result = await MakeImage(pool);

        // Too many images at once clears in seconds: Ideogram's client waits and sends it again on the same key.
        result.IsSuccess.Should().BeTrue();
        requestsPerKey[KeyOne].Should().Be(2);
        requestsPerKey[KeyTwo].Should().Be(0);
        Used(KeyOne).Should().Be(1, "one image, one use");
    }

    [TestMethod]
    public async Task KeyOneStillHasTooManyAtOnceAfterWaiting_TheImageIsMadeOnKeyTwo_AndKeyOneIsNotLeftOut()
    {
        var pool = Pool(dailyLimit: 10);
        // Something besides this server keeps key one full: every answer to it is "too many at once".
        refusals[KeyOne] = () => Json(HttpStatusCode.TooManyRequests, """{"error":"Too many requests.","reject_reason":"inflight_limit"}""");

        var image = await MakeImage(pool);
        var keyOneAsked = requestsPerKey[KeyOne];
        refusals.Remove(KeyOne);
        var next = await KeyUsedFor(pool);

        image.IsSuccess.Should().BeTrue("the image was asked of key two once key one stayed busy");
        keyOneAsked.Should().Be(3, "key one is asked once and twice more after waiting, then passed over for this image");
        requestsPerKey[KeyTwo].Should().Be(1);
        Used(KeyOne).Should().Be(1, "the busy request was given back; only the next image counts");
        next.Should().Be(KeyOne, "a busy key is not taken out of use: once free it makes images again");
    }

    [TestMethod]
    public async Task EveryKeyStillHasTooManyAtOnce_TheImageFailsAsBusy_AndNoKeyIsLeftOut()
    {
        var pool = Pool(dailyLimit: 10);
        var busy = () => Json(HttpStatusCode.TooManyRequests, """{"error":"Too many requests.","reject_reason":"inflight_limit"}""");
        refusals[KeyOne] = busy;
        refusals[KeyTwo] = busy;

        var image = await MakeImage(pool);

        image.IsSuccess.Should().BeFalse();
        image.KeyBusy.Should().BeTrue();
        image.StopsJob.Should().BeFalse("the job goes on: the keys are only busy");
        (requestsPerKey[KeyOne], requestsPerKey[KeyTwo]).Should().Be((3, 3));
        (Used(KeyOne), Used(KeyTwo)).Should().Be((0, 0), "nothing was made, so nothing is counted");
    }

    [TestMethod]
    public async Task KeyOneHasEveryPlaceTaken_NewImagesGoToKeyTwo()
    {
        var pool = Pool(dailyLimit: 10, maxConcurrent: 1);
        // A request of another Seller still running on key one: its only place is taken.
        await using var running = await pool.AcquireAsync(ImageGenerationProviders.Ideogram);
        running!.ApiKey.Should().Be(KeyOne);

        for (var i = 0; i < 3; i++)
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();

        requestsPerKey[KeyOne].Should().Be(0);
        requestsPerKey[KeyTwo].Should().Be(3);
    }

    // The limits as they are set in .env: SystemApiKeys__Ideogram__DailyLimitPerKey=500, MaxConcurrentPerKey=8.
    private const int RealDailyLimit = 500;
    private const int RealConcurrency = 8;

    [TestMethod]
    public async Task Fill_RealDailyLimit_KeyOneMakesItsFiveHundredImages_AndThe501stIsMadeOnKeyTwo()
    {
        var pool = Pool(RealDailyLimit, RealConcurrency);

        for (var i = 0; i < RealDailyLimit; i++)
            (await MakeImage(pool)).IsSuccess.Should().BeTrue();
        var afterFiveHundred = (requestsPerKey[KeyOne], requestsPerKey[KeyTwo]);
        var image501 = await KeyUsedFor(pool);

        afterFiveHundred.Should().Be((500, 0), "every one of the first 500 images is made on key one");
        image501.Should().Be(KeyTwo, "key one has made its 500 images today");
        Used(KeyOne).Should().Be(500);
        Used(KeyTwo).Should().Be(1);
    }

    [TestMethod]
    public async Task Fill_RealConcurrency_EightImagesRunOnKeyOneAtOnce_TheNinthIsMadeOnKeyTwo_AndKeyOneTakesNewOnesOnceOneFinishes()
    {
        var pool = Pool(RealDailyLimit, RealConcurrency);
        // Eight images still being made on the platform's keys (other Sellers' runs).
        var running = new List<ISystemApiKeyLease>();
        for (var i = 0; i < RealConcurrency; i++)
            running.Add((await pool.AcquireAsync(ImageGenerationProviders.Ideogram))!);

        var ninth = await KeyUsedFor(pool);
        await running[0].DisposeAsync();
        var afterOneFinished = await KeyUsedFor(pool);

        running.Select(lease => lease.ApiKey).Should().AllBe(KeyOne, "key one takes up to 8 images at once");
        ninth.Should().Be(KeyTwo, "key one has all 8 places taken");
        afterOneFinished.Should().Be(KeyOne, "one of key one's images finished, so it has a place again");
        foreach (var lease in running.Skip(1)) await lease.DisposeAsync();
    }

    // Makes one image and says which key the fake Ideogram API was asked with. Fails rather than hangs when the pool
    // waits for a busy key instead of going on to the other.
    private async Task<string> KeyUsedFor(SystemApiKeyPool pool)
    {
        var before = new Dictionary<string, int>(requestsPerKey);
        var result = await MakeImage(pool).WaitAsync(TimeSpan.FromSeconds(5));
        result.IsSuccess.Should().BeTrue();
        return requestsPerKey.Single(pair => pair.Value > before[pair.Key]).Key;
    }

    private async Task<ImageGenerationResult> MakeImage(SystemApiKeyPool pool)
    {
        var ideogram = Ideogram();
        return await pool.GenerateImageAsync(ImageGenerationProviders.Ideogram, ownKey: null,
            key => ideogram.GenerateAsync(key, "A fox on a bicycle.", "1:1"));
    }

    private async Task UseUp(string key, int uses)
    {
        for (var i = 0; i < uses; i++)
            await usage.TryTakeAsync(ImageGenerationProviders.Ideogram, SystemApiKeyPool.KeyIdOf(key), Today, null, default);
    }

    private int Used(string key) => usage.Used(ImageGenerationProviders.Ideogram, SystemApiKeyPool.KeyIdOf(key), Today);

    private SystemApiKeyPool Pool(int dailyLimit, int? maxConcurrent = null, string? strategy = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SystemApiKeys:Ideogram:Keys"] = $"{KeyOne};{KeyTwo}",
            ["SystemApiKeys:Ideogram:DailyLimitPerKey"] = dailyLimit.ToString(),
        };
        if (maxConcurrent is not null) settings["SystemApiKeys:Ideogram:MaxConcurrentPerKey"] = maxConcurrent.ToString();
        if (strategy is not null) settings["SystemApiKeys:Ideogram:Strategy"] = strategy;
        var options = new SystemApiKeysOptions();
        SystemApiKeysOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), options);
        return new SystemApiKeyPool(Microsoft.Extensions.Options.Options.Create(options), usage, new FakeTimeProvider(Noon),
            NullLogger<SystemApiKeyPool>.Instance);
    }

    // The real Ideogram client, talking to a fake API: a new image is answered per key, then made at once.
    private IdeogramImageProvider Ideogram()
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient("Ideogram")).Returns(() => new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Post) return Answer(request);
            var key = request.Headers.GetValues("Api-Key").Single();
            requestsPerKey[key]++;
            return refusals.TryGetValue(key, out var refuse) ? refuse() : Answer();
        })));
        var settings = new IdeogramOptions { RetryBaseDelay = TimeSpan.Zero, PollInterval = TimeSpan.Zero };
        return new IdeogramImageProvider(clients.Object, Microsoft.Extensions.Options.Options.Create(settings));
    }

    private const string GenerationUrl = "https://api.ideogram.ai/v2/generations/g1";
    private const string ImageUrl = "https://ideogram.ai/api/images/ephemeral/abc.png?exp=1";

    private static HttpResponseMessage Answer() => Json(HttpStatusCode.OK, """{"generation_id":"g1","seed":7}""");

    private static HttpResponseMessage Answer(HttpRequestMessage request) =>
        request.RequestUri!.AbsoluteUri == GenerationUrl
            ? Json(HttpStatusCode.OK,
                $$"""{"generation_id":"g1","status":"completed","created":"2026-11-15T00:00:00Z","response_type":"url","usage_cost_usd_micros":30000,"data":[{"prompt":"A fox.","resolution":"2048x2048","is_image_safe":true,"seed":7,"url":"{{ImageUrl}}"}]}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Image) { Headers = { ContentType = new("image/png") } } };

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
