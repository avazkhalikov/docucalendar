using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocuCalendar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocuCalendarInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        services.AddDbContext<CalendarDbContext>(options => options.UseNpgsql(
            connectionString,
            // The migration history belongs to this service alone. Sharing a database means sharing
            // it with another application's EF migrations, and one history table listing both would
            // let either side's tooling believe the other's migrations were missing.
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CalendarDbContext.SchemaName)));

        services.AddScoped<BookingService>();
        services.AddScoped<TenantService>();
        services.AddScoped<CalendarService>();
        // Outbound notifications to Docurest. A named client so its lifetime and DNS behaviour are
        // the platform's problem rather than a hand-rolled static HttpClient's.
        services.AddHttpClient<DocurestWebhookSender>();

        return services;
    }
}
