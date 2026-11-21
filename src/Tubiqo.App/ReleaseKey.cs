namespace Tubiqo.App;

/// <summary>
/// The maintainer's release public key (ECDSA P-256, base64 SubjectPublicKeyInfo). Every downloaded
/// release manifest must be signed by the matching private key, which never leaves the maintainer's PC.
/// Changing this key means older Tubiqo versions will refuse releases signed with the new one.
/// </summary>
internal static class ReleaseKey
{
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHHRMm5l0wFBKB9D3BpQoUKdCnYtFcqijKbySWJirSWWUMOrxbRq4sqSmm6tAAGc40qyUYDbgSpmYwZggZ4y3tw==";
}