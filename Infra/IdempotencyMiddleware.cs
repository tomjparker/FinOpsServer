using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace FinTrans.Infra;

public class IdempotencyMiddleware
{
    private static readonly ConcurrentDictionary<string, (int Status, string Body)> _store = new();
    private readonly RequestDelegate _next;
    public IdempotencyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Method == HttpMethods.Post &&
            ctx.Request.Path.StartsWithSegments("/v1/payments") &&
            ctx.Request.Headers.TryGetValue("Idempotency-Key", out var key) &&
            !string.IsNullOrWhiteSpace(key))
        {
            if (_store.TryGetValue(key!, out var hit))
            {
                ctx.Response.StatusCode = hit.Status;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(hit.Body);
                return;
            }

            // capture response
            var originalBody = ctx.Response.Body;
            using var mem = new MemoryStream();
            ctx.Response.Body = mem;

            await _next(ctx); // execute pipeline

            mem.Position = 0;
            var body = await new StreamReader(mem).ReadToEndAsync();
            _store.TryAdd(key!, (ctx.Response.StatusCode, body));

            mem.Position = 0;
            await mem.CopyToAsync(originalBody);
            ctx.Response.Body = originalBody;
            return;
        }

        await _next(ctx);
    }
}
