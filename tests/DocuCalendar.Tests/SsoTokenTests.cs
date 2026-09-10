using DocuCalendar.WebApi.Auth;

namespace DocuCalendar.Tests;

/// <summary>
/// The token is the only way into this site. Everything below is a way in that must NOT work.
/// </summary>
public class SsoTokenTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-to-be-real";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static SsoIdentity Owner() =>
        new("wiut", Guid.Parse("11111111-1111-1111-1111-111111111111"), "Avaz", "owner", "Asia/Tashkent", "WIUT");

    [Fact]
    public void AFreshToken_LetsThePersonIn_AsThemselves()
    {
        var token = SsoToken.Mint(Owner(), Secret, Now);

        var verified = SsoToken.Verify(token, Secret, Now.AddSeconds(30));

        Assert.NotNull(verified);
        Assert.Equal("wiut", verified!.TenantId);
        Assert.Equal(Owner().UserId, verified.UserId);
        Assert.Equal("Avaz", verified.Name);
        Assert.True(verified.IsOwner);
        Assert.Equal("Asia/Tashkent", verified.TimeZoneId);
    }

    [Fact]
    public void ADifferentSecret_IsRefused()
    {
        var token = SsoToken.Mint(Owner(), Secret, Now);

        Assert.Null(SsoToken.Verify(token, "a-completely-different-secret-value-here", Now));
    }

    [Fact]
    public void ATamperedPayload_IsRefused()
    {
        // A real forgery, not a string edit: decode the payload, promote yourself to owner and
        // borrow somebody else's account, re-encode — and keep the original signature, which is
        // the only thing an attacker cannot recompute.
        var token = SsoToken.Mint(Owner() with { TenantId = "someone-else", Role = "operator" }, Secret, Now);
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(FromBase64Url(parts[0]));

        var forgedPayload = payload
            .Replace("\"role\":\"operator\"", "\"role\":\"owner\"")
            .Replace("\"tenantId\":\"someone-else\"", "\"tenantId\":\"wiut\"");
        Assert.NotEqual(payload, forgedPayload); // the test itself must actually change something

        var forged = ToBase64Url(System.Text.Encoding.UTF8.GetBytes(forgedPayload)) + "." + parts[1];

        Assert.Null(SsoToken.Verify(forged, Secret, Now));
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    [Fact]
    public void AnExpiredToken_IsRefused()
    {
        var token = SsoToken.Mint(Owner(), Secret, Now);

        // Five minutes is the whole life of a door key.
        Assert.Null(SsoToken.Verify(token, Secret, Now.AddMinutes(6)));
    }

    [Fact]
    public void ATokenClaimingALongLife_IsRefused()
    {
        // Even signed correctly, a token good for a week is not one of ours — a stolen link must
        // not be usable tomorrow.
        var farFuture = new SsoIdentity("wiut", Guid.NewGuid(), "Avaz", "owner", null, null);
        var token = SsoToken.Mint(farFuture, Secret, Now.AddDays(7));

        Assert.Null(SsoToken.Verify(token, Secret, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("only.")]
    [InlineData(".onlysignature")]
    [InlineData("!!!not-base64!!!.!!!also-not!!!")]
    public void Rubbish_IsRefusedWithoutThrowing(string? token)
    {
        Assert.Null(SsoToken.Verify(token, Secret, Now));
    }

    [Fact]
    public void NoSecretConfigured_LetsNobodyIn()
    {
        var token = SsoToken.Mint(Owner(), Secret, Now);

        Assert.Null(SsoToken.Verify(token, "", Now));
    }
}
