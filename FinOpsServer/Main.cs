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

// Simple test endpoint
app.MapGet("/", () => "Hello World!");

// Idempotency middleware (HashMap/Set)
app.UseMiddleware<IdempotencyMiddleware>();

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
PaymentsApi.Map(app);
AnalyticsApi.Map(app);

app.Run();
