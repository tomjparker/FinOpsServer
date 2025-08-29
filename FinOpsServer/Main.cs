using System.Text.Json;
using FinOpsServer.Domain;
using FinOpsServer.Features;
using FinOpsServer.Infra;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Register services

// lifetimes (10-sec refresher)
// Singleton: one instance for the whole app. Great for stateless helpers, caches, background schedulers. Must be thread-safe.
// Scoped: one per request. Good when the object holds request-specific state.
// Transient: new every time. Fine for lightweight, stateless helpers.

builder.Services.AddSingleton<SagaOrchestrator>(); // Runs with compensation stack for rollback
builder.Services.AddSingleton(_ => new SlidingWindowRateLimiter(100, TimeSpan.FromMinutes(1))); // per API key - provides queues of timestamps - one limiter across all requests - lock(q) provides thread safety

// builder.Services.AddSingleton<RollingMax>(_ => new(TimeSpan.FromMinutes(5))); // rolling p95-ish helper - monotonic deque showing latency - though Linkedlist<> is not thread safe
// builder.Services.AddSingleton<PrefixBuckets>(); // Single shared time series, for minute buckets and range aggregation metrics - used later
// builder.Services.AddSingleton<PriorityScheduler>(); // Min-heap of delayed jobs (retries, throttled) - runs on background thread - used later (possibly want IHostedService later)
// builder.Services.AddSingleton<IntervalsService>(); // pure functions for capacity/overlap math - singleton unnecessary might change later
// builder.Services.AddSingleton<ServiceGraph>(); // holds adjacency list with SetEdges() - likely dont want to be a singleton due to race conditions - a scoped service graph is better
// builder.Services.AddMemoryCache(); // Single shared cache for ephemeral data, thread safe - used later

var app = builder.Build();

var api = app.MapGroup("/v1");

// Sliding-window rate limiter
app.Use(async (ctx, next) =>
{
    var limiter = ctx.RequestServices.GetRequiredService<SlidingWindowRateLimiter>();
    var key = ctx.Request.Headers["X-Api-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key)) key = "anon";

    if (!limiter.Allow(key, DateTime.UtcNow))
    { ctx.Response.StatusCode = 429; await ctx.Response.WriteAsync("rate limited"); return; }

    await next();
});

// Feature endpoints
app.UseMiddleware<IdempotencyMiddleware>(); // HashMap/set

PaymentsApi.Map(api);
AnalyticsApi.Map(api);

// Simple test endpoint
app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Run();

// idempotency behavior applies only to POST /v1/payments calls. That’s because the middleware is registered globally (app.UseMiddleware<IdempotencyMiddleware>()) 
// but it self-filters on POST and Path.StartsWithSegments("/v1/payments"). When such a request includes an Idempotency-Key header, the middleware captures the 
// response the first time and replays the exact same status/body on any repeat with the same key—so clients can safely retry without creating duplicate payments. 
// Other routes (e.g., /, /health, anything not /v1/payments) aren’t idempotent under this scheme. If I later add /v2, either loosen the path check 
// (e.g., just "/payments") or attach the middleware to each versioned group.