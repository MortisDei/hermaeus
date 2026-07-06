using System.Security.Cryptography;
using System.Text;
using Aether.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.LocalApi;

/// <summary>
/// Requires a bearer token on every request, even though the host only binds
/// loopback: other local processes on a shared machine could otherwise call
/// it silently. Fails closed when no token has been configured yet.
/// </summary>
public static class LocalApiTokenAuth
{
    public const string TokenHeaderName = "X-Aether-Token";

    public static IApplicationBuilder UseLocalApiTokenAuth(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var settings = context.RequestServices.GetRequiredService<ISettingsService>().Settings;
            var secrets = context.RequestServices.GetRequiredService<ISecretStore>();
            var configuredToken = string.IsNullOrWhiteSpace(settings.LocalApi.ApiToken)
                ? string.Empty
                : await secrets.ResolveAsync(settings.LocalApi.ApiToken, context.RequestAborted);

            if (string.IsNullOrWhiteSpace(configuredToken))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Local API token is not configured. Generate one in Settings first.");
                return;
            }

            var provided = context.Request.Headers.TryGetValue(TokenHeaderName, out var values)
                ? values.ToString()
                : string.Empty;

            if (!FixedTimeEquals(provided, configuredToken))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync($"Missing or invalid {TokenHeaderName} header.");
                return;
            }

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
