using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SynoAI.Settings;
using System.Security.Cryptography;
using System.Text;

namespace SynoAI.Controllers
{
    /// <summary>
    /// Requires a valid admin API key (header <see cref="HeaderName"/>) matching
    /// <see cref="AppSettings.AdminApiKey"/>. When no key is configured the endpoint is disabled entirely,
    /// so the admin surface is never reachable unauthenticated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class AdminApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        /// <summary>The header carrying the admin API key.</summary>
        public const string HeaderName = "X-Api-Key";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            string configuredKey = context.HttpContext.RequestServices
                .GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue.AdminApiKey;

            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                context.Result = new ObjectResult(new { error = "The admin API is disabled. Set 'AdminApiKey' in configuration to enable it." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return Task.CompletedTask;
            }

            string? providedKey = context.HttpContext.Request.Headers[HeaderName];
            if (string.IsNullOrEmpty(providedKey) || !FixedTimeEquals(providedKey, configuredKey))
            {
                context.Result = new UnauthorizedResult();
            }

            return Task.CompletedTask;
        }

        // Constant-time comparison to avoid leaking the key length/prefix via timing.
        private static bool FixedTimeEquals(string a, string b)
        {
            byte[] aBytes = Encoding.UTF8.GetBytes(a);
            byte[] bBytes = Encoding.UTF8.GetBytes(b);
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
