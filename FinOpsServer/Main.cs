using System.Text.Json;
using FinTrans.Domain;
using FinTrans.Features;
using FinTrans.Infra;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SlidingWindowRateLimiter>(_ =>
    new(maxRequests: 100, window: TimeSpan.FromMinutes(1))); // per API key
builder.Services.AddSingleton<RollingMax>(_ => new(TimeSpan.FromMinutes(5))); // rolling p95-ish helper
builder.Services.AddSingleton<PrefixBuckets>();
builder.Services.AddSingleton<PriorityScheduler>();
builder.Services.AddSingleton<IntervalsService>();
builder.Services.AddSingleton<ServiceGraph>();
builder.Services.AddSingleton<SagaOrchestrator>();

var app = builder.Build();

var api = app.MapGroup("/v1");

// Sliding-window rate limiter
app.Use(async (ctx, next) =>
{
    var limiter = ctx.RequestServices.GetRequiredService<SlidingWindowRateLimiter>();
    var key = ctx.Request.Headers["X-Api-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key)) key = "anon";

    if (!limiter.Allow(key, DateTime.UtcNow))
    {
        ctx.Response.StatusCode = 429;
        await ctx.Response.WriteAsync("rate limited");
        return;
    }
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