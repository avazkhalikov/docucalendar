using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DocuCalendar.WebApi.Auth;

/// <summary>Who Docurest says is arriving, and what they may manage here.</summary>
public sealed record SsoIdentity(
    string TenantId,
    Guid UserId,
    string Name,
    string Role,          // "owner" | "operator"
    string? TimeZoneId,
    string? AccountName)
{
    public bool IsOwner => string.Equals(Role, "owner", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The handshake that lets a Docurest user walk into this site without a second password.
///
/// Docurest signs a tiny payload with a secret shared ONLY for this purpose — never the main JWT
/// signing key, so a leak here cannot mint Docurest sessions. The token lives about two minutes:
/// long enough to survive a slow redirect, short enough that a link left in a browser history is
/// worthless. It is a door key, not a session; the session is the cookie this service issues after
/// checking it.
/// </summary>
public static class SsoToken
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static SsoIdentity? Verify(string? token, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret)) return null;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;

        var payloadPart = token[..dot];
        var signaturePart = token[(dot + 1)..];

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = FromBase64Url(payloadPart);
            providedSignature = FromBase64Url(signaturePart);
        }
        catch (FormatException) { return null; }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payloadBytes);
        // Fixed-time comparison: a signature check that leaks timing is a signature check that can
        // be guessed at, one byte per thousand attempts.
        if (!CryptographicOperations.FixedTimeEquals(expected, providedSignature)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            var exp = root.TryGetProperty("exp", out var e) && e.TryGetInt64(out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : (DateTimeOffset?)null;
            if (exp is null || exp < now) return null;
            // A token claiming to live for a week is not one of ours, whatever it is signed with.
            if (exp > now.Add(MaxAge).AddMinutes(1)) return null;

            var tenantId = Str(root, "tenantId");
            var sub = Str(root, "sub");
            if (string.IsNullOrWhiteSpace(tenantId) || !Guid.TryParse(sub, out var userId)) return null;

            return new SsoIdentity(
                tenantId!,
                userId,
                Str(root, "name") ?? "Someone",
                Str(root, "role") ?? "operator",
                Str(root, "tz"),
                Str(root, "accountName"));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Mints a token — used by the tests, and by any future tooling that needs to
    /// impersonate the Docurest side deliberately.</summary>
    public static string Mint(SsoIdentity identity, string secret, DateTimeOffset now)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tenantId = identity.TenantId,
            sub = identity.UserId.ToString(),
            name = identity.Name,
            role = identity.Role,
            tz = identity.TimeZoneId,
            accountName = identity.AccountName,
            exp = now.Add(MaxAge).ToUnixTimeSeconds(),
        });
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return ToBase64Url(payload) + "." + ToBase64Url(signature);
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", 1 => throw new FormatException("bad base64url"), _ => s };
        return Convert.FromBase64String(s);
    }
}
