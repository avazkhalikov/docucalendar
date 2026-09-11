using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace DocuCalendar.Infrastructure.Services.Sync;

/// <summary>
/// Protects the secrets this service must keep: provider refresh tokens and the api key it signs
/// webhooks with. ASP.NET Data Protection underneath — the key ring lives outside the deploy
/// folders and the application name is pinned, which together are what let a value protected
/// today be read after next week's deploy on the other slot.
/// </summary>
public sealed class TokenVault
{
    private readonly IDataProtector _protector;

    public TokenVault(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("DocuCalendar.ExternalTokens.v1");
    }

    public string Protect(string plain) => _protector.Protect(plain);

    /// <summary>Null when the value cannot be read — a key ring that changed underneath us. The
    /// caller treats that as "reconnect", never as a crash.</summary>
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return _protector.Unprotect(protectedValue); }
        catch (CryptographicException) { return null; }
    }
}

/// <summary>Who started an OAuth connection, carried through the provider round trip.</summary>
public sealed record SyncState(string TenantId, Guid UserId, Guid CalendarId, string Provider, string Nonce);

/// <summary>
/// The state parameter of the OAuth dance, protected and time-limited. The callback trusts nothing
/// in the query string except what it can unprotect from here — which is how a callback arriving
/// without a session cookie still knows exactly whose calendar it is binding.
/// </summary>
public sealed class SyncStateProtector
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector;

    public SyncStateProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("DocuCalendar.SyncState.v1").ToTimeLimitedDataProtector();
    }

    public string Protect(SyncState state) => Protect(state, Lifetime);

    public string Protect(SyncState state, TimeSpan lifetime) =>
        _protector.Protect(JsonSerializer.Serialize(state), lifetime);

    public SyncState? Unprotect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var json = _protector.Unprotect(token);
            return JsonSerializer.Deserialize<SyncState>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
