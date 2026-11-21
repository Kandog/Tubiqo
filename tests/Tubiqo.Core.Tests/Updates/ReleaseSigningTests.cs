using System.Security.Cryptography;
using System.Text;
using Tubiqo.Core.Updates;

namespace Tubiqo.Core.Tests.Updates;

public class ReleaseSigningTests
{
    static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    static readonly ECDsa OtherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    static string PublicKey(ECDsa k) => Convert.ToBase64String(k.ExportSubjectPublicKeyInfo());

    readonly byte[] manifest = Encoding.UTF8.GetBytes("""{"format":1}""");

    [Fact]
    public void A_signature_made_with_the_maintainer_key_verifies()
    {
        var sig = ReleaseSigner.Sign(manifest, Key);
        Assert.True(new SignatureVerifier(PublicKey(Key)).Verify(manifest, sig));
    }

    [Fact]
    public void Changing_a_single_byte_breaks_the_signature()
    {
        var sig = ReleaseSigner.Sign(manifest, Key);
        var tampered = (byte[])manifest.Clone();
        tampered[^2] ^= 1;
        Assert.False(new SignatureVerifier(PublicKey(Key)).Verify(tampered, sig));
    }

    [Fact]
    public void A_signature_from_another_key_is_refused()
    {
        var sig = ReleaseSigner.Sign(manifest, OtherKey);
        Assert.False(new SignatureVerifier(PublicKey(Key)).Verify(manifest, sig));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void Malformed_signatures_are_refused_not_thrown(string sig)
    {
        Assert.False(new SignatureVerifier(PublicKey(Key)).Verify(manifest, sig));
    }

    [Fact]
    public void Signature_text_tolerates_surrounding_whitespace()
    {
        var sig = ReleaseSigner.Sign(manifest, Key);
        Assert.True(new SignatureVerifier(PublicKey(Key)).Verify(manifest, "\n " + sig + "\r\n"));
    }

    [Fact]
    public void Only_p256_public_keys_are_accepted()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() => new SignatureVerifier(PublicKey(p384)));
        Assert.Throws<ArgumentException>(() => new SignatureVerifier("not a key"));
    }
}
