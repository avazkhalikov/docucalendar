using DocuCalendar.Infrastructure.Services.Sync;
using Microsoft.AspNetCore.DataProtection;

namespace DocuCalendar.Tests;

/// <summary>
/// The OAuth callback trusts nothing but this token. It had better round-trip exactly, refuse
/// tampering, and refuse age.
/// </summary>
public class SyncStateProtectorTests
{
    private static SyncStateProtector NewProtector() => new(new EphemeralDataProtectionProvider());

    private static readonly SyncState Sample = new("tenant-1", Guid.NewGuid(), Guid.NewGuid(), "google", "ABCD1234");

    [Fact]
    public void RoundTripsEveryField()
    {
        var protector = NewProtector();
        var token = protector.Protect(Sample);

        var back = protector.Unprotect(token);

        Assert.NotNull(back);
        Assert.Equal(Sample, back);
    }

    [Fact]
    public void ATamperedTokenIsRejected()
    {
        var protector = NewProtector();
        var token = protector.Protect(Sample);
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        Assert.Null(protector.Unprotect(tampered));
    }

    [Fact]
    public void AnExpiredTokenIsRejected()
    {
        var protector = NewProtector();
        var token = protector.Protect(Sample, TimeSpan.FromSeconds(-1));

        Assert.Null(protector.Unprotect(token));
    }

    [Fact]
    public void ATokenFromAnotherKeyRingIsRejected()
    {
        // Two slots that do not share a key ring must not accept each other's states — and this
        // is also what makes the SetApplicationName lesson visible: a different identity is a
        // different key ring.
        var token = NewProtector().Protect(Sample);

        Assert.Null(NewProtector().Unprotect(token));
    }

    [Fact]
    public void GarbageIsRejectedQuietly()
    {
        var protector = NewProtector();
        Assert.Null(protector.Unprotect(null));
        Assert.Null(protector.Unprotect(""));
        Assert.Null(protector.Unprotect("not-a-token"));
    }
}
