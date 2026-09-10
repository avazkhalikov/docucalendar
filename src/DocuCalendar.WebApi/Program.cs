using DocuCalendar.Infrastructure;
using DocuCalendar.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Refuse to boot rather than run with secrets that cannot protect anything. A calendar service
// that starts happily with an empty SSO secret would accept forged identities from anyone.
if (builder.Environment.IsProduction())
{
    foreach (var key in new[] { "Docurest:MasterKey", "Docurest:SsoSecret" })
    {
        var value = builder.Configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Length < 32)
            throw new InvalidOperationException(
                $"{key} must be at least 32 characters in production. Set it in the server's appsettings.Production.json — never in the repository.");
    }
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "docucalendar.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // This is an API behind a SPA: an unauthenticated call deserves a 401, not a redirect to
        // a login page that does not exist here.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDocuCalendarInfrastructure(builder.Configuration);

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? new[] { "https://calendar.docurest.com" };
builder.Services.AddCors(options =>
    options.AddPolicy("DocuCalendar", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // the UI authenticates with a cookie

var app = builder.Build();

// Schema is applied at startup so a deploy needs no manual step. Anything that READS a new column
// must run after this block — the lesson a sibling service learned the hard way.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
            logger.LogInformation("[Startup] Applying {Count} migration(s): {Names}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Startup] Migrations could not be applied.");
        throw; // a calendar on the wrong schema books people into times that do not exist
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("DocuCalendar");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "docucalendar" }));

app.Run();

/// <summary>Exposed so the test project can spin the host up in-process.</summary>
public partial class Program { }
