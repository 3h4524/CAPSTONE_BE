using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace APCS.E2E;

public sealed class SellerSeeder(E2EOptions options)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(90);
    private static readonly Regex VerificationToken = new("verify-email\\?token=([A-Za-z0-9._~%+-]+)", RegexOptions.Compiled);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task RegisterVerifiedSellerAsync(CancellationToken cancellationToken)
    {
        var registration = await PostAsync("register", $"/api/auth/register", new
        {
            email = options.SellerEmail,
            password = options.SellerPassword,
            fullName = options.SellerFullName,
        }, cancellationToken).ConfigureAwait(false);

        if (registration.Status == System.Net.HttpStatusCode.Conflict)
        {
            Console.WriteLine("[seed] address already registered; continuing to verification");
        }
        else if (registration.Status != System.Net.HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"The register endpoint answered {registration.Status}: {registration.Body}");
        }

        var token = await WaitForVerificationTokenAsync(cancellationToken).ConfigureAwait(false);

        var verification = await PostAsync("verify-email", "/api/auth/verify-email", new { token }, cancellationToken)
            .ConfigureAwait(false);

        if (verification.Status != System.Net.HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"verify-email answered {verification.Status}: {verification.Body}");
        }

        await PostAsync("login", "/api/auth/login", new
        {
            email = options.SellerEmail,
            password = options.SellerPassword,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> WaitForVerificationTokenAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + PollTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var token = TryReadVerificationToken(options.MailFile, options.SellerEmail);
            if (token is not null)
            {
                return token;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"No verification email for {options.SellerEmail} arrived at {options.MailFile} within {PollTimeout.TotalSeconds} seconds.");
    }

    private static string? TryReadVerificationToken(string mailFile, string recipient)
    {
        if (!File.Exists(mailFile))
        {
            return null;
        }

        foreach (var line in File.ReadLines(mailFile, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            MailRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<MailRecord>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (record is null || !MatchesRecipient(record, recipient))
            {
                continue;
            }

            var match = VerificationToken.Match(record.Body ?? string.Empty);
            if (match.Success)
            {
                return Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return null;
    }

    private static bool MatchesRecipient(MailRecord record, string recipient)
    {
        return (record.To ?? []).Any(address => ContainsRecipient(address, recipient))
            || (record.Envelope?.RcptTo ?? []).Any(address => ContainsRecipient(address, recipient));
    }

    private static bool ContainsRecipient(string address, string recipient) =>
        (address ?? string.Empty).ToLowerInvariant().Contains(recipient.ToLowerInvariant());

    private async Task<(System.Net.HttpStatusCode Status, string Body)> PostAsync<TBody>(
        string label,
        string path,
        TBody body,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(
            $"http://localhost:{options.ApiPort}{path}",
            content,
            cancellationToken).ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[seed] {label} answered {(int)response.StatusCode}");
        return (response.StatusCode, responseText);
    }
}

internal sealed record MailRecord(
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("to")] string[]? To,
    [property: JsonPropertyName("envelope")] MailEnvelope? Envelope);

internal sealed record MailEnvelope(
    [property: JsonPropertyName("rcptTo")] string[]? RcptTo);
