using System.Security.Cryptography;
using System.Text;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.Infrastructure.Services;

/// <summary>
/// Registers Docurest accounts and authenticates their API keys. There is no sign-up here by
/// design: an account exists in this service only because Docurest provisioned it.
/// </summary>
public sealed class TenantService
{
    private readonly CalendarDbContext _db;

    public TenantService(CalendarDbContext db) => _db = db;

    /// <summary>
    /// Creates or refreshes a tenant and returns a NEW api key in plaintext — the only moment it
    /// is ever visible. Idempotent on the tenant, deliberately not on the key: re-provisioning is
    /// how a key gets rotated when Docurest loses or suspects it.
    /// </summary>
    public async Task<string> ProvisionAsync(string tenantId, string name, string? timeZoneId, CancellationToken ct)
    {
        var apiKey = NewApiKey();
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (row == null)
        {
            row = new TenantRegistration { TenantId = tenantId };
            _db.Tenants.Add(row);
        }

        row.Name = string.IsNullOrWhiteSpace(name) ? tenantId : name.Trim();
        if (!string.IsNullOrWhiteSpace(timeZoneId)) row.TimeZoneId = timeZoneId.Trim();
        row.ApiKeyHash = Hash(apiKey);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return apiKey;
    }

    /// <summary>The tenant this key belongs to, or null. Constant-time comparison is unnecessary
    /// here because the lookup is by hash — the secret never takes part in a comparison.</summary>
    public async Task<TenantRegistration?> AuthenticateAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        var hash = Hash(apiKey);
        return await _db.Tenants.FirstOrDefaultAsync(t => t.ApiKeyHash == hash, ct);
    }

    /// <summary>The account's clock. Falls back to UTC rather than throwing: an unknown zone id
    /// must not take the whole calendar down, and UTC is visibly wrong rather than subtly wrong.</summary>
    public static TimeZoneInfo ZoneOf(TenantRegistration tenant)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static string NewApiKey() =>
        "dcal_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
