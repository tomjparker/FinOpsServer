using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Routing;
using FinTrans.Infra;

namespace FinOpsServer.Features;

public static class PaymentsApi
{
    private static readonly ConcurrentDictionary<string, Payment> _store = new();

    public record PaymentRequest(string IdempotencyKey, string From, string To, decimal Amount, string Currency);
    public record Payment(string Id, string From, string To, decimal Amount, string Currency, DateTimeOffset CreatedAt, string Status);

    public static void Map(IEndpointRouteBuilder routes)
    {
        var grp = routes.MapGroup("/payments");

        grp.MapPost("/", async (HttpRequest req, SagaOrchestrator saga, CancellationToken ct) =>
        {
            var pr = await JsonSerializer.DeserializeAsync<PaymentRequest>(req.Body, cancellationToken: ct);
            if (pr is null) return Results.BadRequest(new { error = "invalid json" });
            if (string.IsNullOrWhiteSpace(pr.From) || string.IsNullOrWhiteSpace(pr.To) || pr.Amount <= 0 || string.IsNullOrWhiteSpace(pr.Currency))
                return Results.BadRequest(new { error = "missing/invalid fields" });

            Payment? created = null;

            var ok = await saga.ExecuteAsync(
                // Create the record first
                async () => {
                    created = new Payment(
                        Id: Guid.NewGuid().ToString("n"),
                        From: pr.From.Trim(),
                        To: pr.To.Trim(),
                        Amount: pr.Amount,
                        Currency: pr.Currency.Trim().ToUpperInvariant(),
                        CreatedAt: DateTimeOffset.UtcNow,
                        Status: "created");
                    _store[created.Id] = created;
                    return (true, async () => { _store.TryRemove(created.Id, out _); await Task.CompletedTask; });
                },
                // Then reserve funds (mocked for now)
                async () => {
                    // if failure: return (false, undo)
                    return (true, async () => { /* release funds */ await Task.CompletedTask; });
                },
                // Post ledger check (pretend)
                async () => {
                    return (true, async () => { /* unpost */ await Task.CompletedTask; });
                }
            );

            if (!ok) return Results.StatusCode(500);

            return Results.Created($"/v1/payments/{created!.Id}", created);
        });

        grp.MapGet("/{id}", (string id) =>
            _store.TryGetValue(id, out var p) ? Results.Ok(p) : Results.NotFound());
    }
}

