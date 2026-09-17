using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocuCalendar.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuCalendar.Infrastructure.Services;

/// <summary>
/// Tells Docurest that something happened on a calendar, so the people who work there hear about
/// it where they already are — an in-app notification and their Telegram operator group — instead
/// of having to keep this site open.
///
/// Best effort with retries, and never on the request path: a visitor's booking must not fail
/// because a notification could not be delivered.
/// </summary>
public sealed class DocurestWebhookSender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<DocurestWebhookSender> _logger;

    public DocurestWebhookSender(HttpClient http, IConfiguration config, ILogger<DocurestWebhookSender> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public Task SendBookedAsync(TenantRegistration tenant, StaffCalendar calendar, Appointment appointment, string apiKeyForSigning) =>
        SendAppointmentEventAsync(tenant, calendar, appointment, apiKeyForSigning, "booked");

    /// <summary>
    /// "booked", "moved" or "cancelled". The last two come from the sync when the person changed
    /// the appointment inside Outlook or Google — <paramref name="via"/> names where, and
    /// <paramref name="previousStartUtc"/> says what a moved appointment used to be.
    /// </summary>
    public async Task SendAppointmentEventAsync(
        TenantRegistration tenant, StaffCalendar calendar, Appointment appointment, string apiKeyForSigning,
        string eventName, DateTimeOffset? previousStartUtc = null, string? via = null)
    {
        var url = _config["Docurest:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogDebug("[Webhook] No Docurest:WebhookUrl configured — skipping the {Event} notification.", eventName);
            return;
        }

        var zone = TenantService.ZoneOf(tenant);
        var payload = JsonSerializer.Serialize(new
        {
            @event = eventName,
            tenantId = tenant.TenantId,
            calendarId = calendar.Id,
            calendarLabel = calendar.Label,
            previousLocal = previousStartUtc is { } prev ? Application.Scheduling.SlotEngine.FormatLocal(prev, zone) : null,
            via,
            appointment = new
            {
                id = appointment.Id,
                startsAtUtc = appointment.StartsAt,
                endsAtUtc = appointment.EndsAt,
                local = Application.Scheduling.SlotEngine.FormatLocal(appointment.StartsAt, zone),
                minutes = (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes,
                visitorName = appointment.VisitorName,
                visitorPhone = appointment.VisitorPhone,
                // What the visitor said, and what actually rang — two different facts.
                callerPhone = appointment.CallerPhone,
                topic = appointment.Topic,
                serviceName = appointment.ServiceName,
                answers = BookingService.ParseAnswers(appointment.AnswersJson).Select(a => new { question = a.Question, answer = a.Answer }),
                channel = appointment.Channel,
                status = appointment.Status,
                notifyEmail = appointment.NotifyEmail,
            },
        });

        // Signed with the tenant's own api key: Docurest can prove the message came from this
        // service and concerns the account it claims to.
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiKeyForSigning), Encoding.UTF8.GetBytes(payload)));

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                req.Headers.Add("X-Calendar-Signature", signature);
                req.Headers.Add("X-Calendar-Tenant", tenant.TenantId);
                using var resp = await _http.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Webhook] {Event} delivered for {Tenant}.", eventName, tenant.TenantId);
                    return;
                }
                _logger.LogWarning("[Webhook] {Event} for {Tenant} refused: HTTP {Status} (attempt {Attempt}).",
                    eventName, tenant.TenantId, (int)resp.StatusCode, attempt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Webhook] {Event} for {Tenant} failed (attempt {Attempt}).", eventName, tenant.TenantId, attempt);
            }
            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
        }
    }
}
