using Microsoft.AspNetCore.Routing;

namespace FinOpsServer.Features;

public static class AnalyticsApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var grp = routes.MapGroup("/analytics");

        // simple stub so the project compiles; expand later
        grp.MapGet("/ping", () => Results.Ok(new
        {
            ok = true,
            ts = DateTimeOffset.UtcNow
        }));
    }
}
