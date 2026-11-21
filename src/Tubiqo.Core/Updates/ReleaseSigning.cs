using System.Security.Cryptography;

namespace Tubiqo.Core.Updates;

public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Signs release manifests with the maintainer's ECDSA P-256 key (SHA-256, IEEE P1363 signature, base64).
/// Used by the maintainer's signing tool; the app only ever verifies.
/// </summary>
public static class ReleaseSigner
{
    public static string Sign(byte[] data, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}

/// <summary>Checks release signatures against the public key compiled into the app.</summary>
public sealed class SignatureVerifier
{
    readonly byte[] publicKey;

    /// <param name="publicKeySpkiBase64">The maintainer's P-256 public key, as base64 SubjectPublicKeyInfo.</param>
    public SignatureVerifier(string publicKeySpkiBase64)
    {
        try
        {
            publicKey = Convert.FromBase64String(publicKeySpkiBase64);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (key.KeySize != 256) throw new ArgumentException("Release key must be ECDSA P-256.", nameof(publicKeySpkiBase64));
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            throw new ArgumentException("Not a valid public key.", nameof(publicKeySpkiBase64), e);
        }
    }

    public bool Verify(byte[] data, string signatureBase64)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64.Trim()); }
        catch (FormatException) { return false; }
        if (signature.Length != 64) return false;

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out _);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
