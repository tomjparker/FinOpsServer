namespace FinOpsServer.Infra;

public interface IApiKeyValidator { bool IsValid(string key); }

public sealed class StaticApiKeyValidator : IApiKeyValidator
{
    private readonly HashSet<string> _keys =
        new(StringComparer.Ordinal) { "demo-123" }; // TODO: move to config/user-secrets
    public bool IsValid(string key) => _keys.Contains(key);
}

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IApiKeyValidator _validator;

    public ApiKeyMiddleware(RequestDelegate next, IApiKeyValidator validator)
    { _next = next; _validator = validator; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var hv) ||
            string.IsNullOrWhiteSpace(hv) ||
            !_validator.IsValid(hv!))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("missing/invalid api key");
            return;
        }

        // make key available downstream (rate limiter, handlers)
        ctx.Items["ApiKey"] = hv.ToString();
        await _next(ctx);
    }
}
