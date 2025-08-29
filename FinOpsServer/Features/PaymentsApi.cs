// Features/PaymentsApi.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Routing;

namespace FinTrans.Features;

public static class PaymentsApi
{
    // thread-safe in-memory store (demo)
    private static readonly ConcurrentDictionary<string, Payment> _store = new();

    // very simple Data Transfer Objects (DTOs) (could be moved to Domain/)
    public record PaymentRequest(
        string IdempotencyKey,
        string From,
        string To,
        decimal Amount,
        string Currency);

    public record Payment(
        string Id,
        string From,
        string To,
        decimal Amount,
        string Currency,
        DateTimeOffset CreatedAt,
        string Status);

    public static void Map(IEndpointRouteBuilder routes)
    {
        var grp = routes.MapGroup("/payments");

        grp.MapPost("/", async (HttpRequest req, CancellationToken ct) =>
        {
            var pr = await JsonSerializer.DeserializeAsync<PaymentRequest>(req.Body, cancellationToken: ct);
            if (pr is null) return Results.BadRequest(new { error = "invalid json" });

            // tiny validation
            if (string.IsNullOrWhiteSpace(pr.From) ||
                string.IsNullOrWhiteSpace(pr.To) ||
                pr.Amount <= 0 ||
                string.IsNullOrWhiteSpace(pr.Currency))
            {
                return Results.BadRequest(new { error = "missing/invalid fields" });
            }

            var p = new Payment(
                Id: Guid.NewGuid().ToString("n"),
                From: pr.From.Trim(),
                To: pr.To.Trim(),
                Amount: pr.Amount,
                Currency: pr.Currency.Trim().ToUpperInvariant(),
                CreatedAt: DateTimeOffset.UtcNow,
                Status: "created");

            _store[p.Id] = p;
            return Results.Created($"/v1/payments/{p.Id}", p);
        });

        grp.MapGet("/{id}", (string id) =>
            _store.TryGetValue(id, out var p) ? Results.Ok(p) : Results.NotFound());
    }
}
