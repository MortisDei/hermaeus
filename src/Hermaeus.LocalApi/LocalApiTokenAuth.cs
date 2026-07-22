using System.Security.Cryptography;
using System.Text;
using Hermaeus.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hermaeus.LocalApi;

/// <summary>
/// Requires a bearer token on every request, even though the host only binds
/// loopback: other local processes on a shared machine could otherwise call
/// it silently. Fails closed when no token has been configured yet. Each
/// configured token is named (docs/review/03-next-level-roadmap.md Phase 2):
/// the matched name is stashed in <see cref="HttpContext.Items"/> under
/// <see cref="VerifiedTokenNameItemKey"/> so downstream endpoint code can use
/// it as the caller's verified identity instead of the merely self-reported
/// X-Hermaeus-Client header.
/// </summary>
public static class LocalApiTokenAuth
{
    public const string TokenHeaderName = "X-Hermaeus-Token";
    public const string VerifiedTokenNameItemKey = "LocalApi.VerifiedTokenName";

    public static IApplicationBuilder UseLocalApiTokenAuth(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            // Unauthenticated so LocalApiProcessManager can health-poll during
            // startup before a token is necessarily configured; it reveals
            // nothing beyond "the process is up".
            if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var settings = context.RequestServices.GetRequiredService<ISettingsService>().Settings;
            var secrets = context.RequestServices.GetRequiredService<ISecretStore>();
            var tokens = settings.LocalApi.Tokens;

            if (tokens.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("No local API token is configured. Generate one in Settings first.");
                return;
            }

            var provided = context.Request.Headers.TryGetValue(TokenHeaderName, out var values)
                ? values.ToString()
                : string.Empty;

            string? matchedName = null;
            foreach (var entry in tokens)
            {
                var resolved = string.IsNullOrWhiteSpace(entry.SecretRef)
                    ? string.Empty
                    : await secrets.ResolveAsync(entry.SecretRef, context.RequestAborted);

                // Every entry is compared, even after a match, so the
                // response time does not itself leak which entry (or how
                // many) matched.
                if (FixedTimeEquals(provided, resolved) && !string.IsNullOrWhiteSpace(resolved))
                    matchedName = entry.Name;
            }

            if (matchedName is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync($"Missing or invalid {TokenHeaderName} header.");
                return;
            }

            context.Items[VerifiedTokenNameItemKey] = matchedName;
            await next(context);
        });

        return app;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length)
        {
            // Still compare against a same-length buffer so an empty or
            // mismatched-length header doesn't short-circuit before any
            // constant-time work happens.
            CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
