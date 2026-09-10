using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocuCalendar.WebApi.Auth;

/// <summary>
/// Machine-to-machine authentication for the booking endpoints: Docurest presents the api key it
/// was given at provisioning, and the tenant it belongs to is stashed for the action to use.
/// Keys are matched by hash, so this class never holds a secret longer than the request.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string TenantItem = "docucalendar.tenant";
    public const string ApiKeyItem = "docucalendar.apikey";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var key = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var tenants = context.HttpContext.RequestServices.GetRequiredService<TenantService>();
        var tenant = await tenants.AuthenticateAsync(key, context.HttpContext.RequestAborted);

        if (tenant == null)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "A valid X-Api-Key is required." });
            return;
        }

        context.HttpContext.Items[TenantItem] = tenant;
        context.HttpContext.Items[ApiKeyItem] = key;
    }
}

/// <summary>Provisioning is the one operation the platform itself performs, with the master key.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireMasterKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["Docurest:MasterKey"];
        var provided = context.HttpContext.Request.Headers["X-Master-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected),
                System.Text.Encoding.UTF8.GetBytes(provided)))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Master key required." });
        }
    }
}

public static class HttpContextExtensions
{
    public static TenantRegistration Tenant(this HttpContext ctx) =>
        (TenantRegistration)ctx.Items[RequireApiKeyAttribute.TenantItem]!;

    public static string ApiKey(this HttpContext ctx) =>
        (string?)ctx.Items[RequireApiKeyAttribute.ApiKeyItem] ?? string.Empty;
}
