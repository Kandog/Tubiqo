// Tubiqo release signing tool (maintainer only).
//
//   keygen  --out <folder>
//       Creates the maintainer's ECDSA P-256 key pair. The private key never belongs in the repo.
//   release --key <private.pem> --out <folder> [--rulepack <rulepack.json>] [--installer <setup.exe> --app-version <x.y.z>]
//       Checks the files, writes tubiqo-release.json and its .sig, and copies the files next to them,
//       ready to attach to a GitHub release.
//   verify  --dir <folder> --public-key <base64>
//       Checks a release folder the way the app will.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tubiqo.Core.Rules;
using Tubiqo.Core.Updates;

return Run(args);

static int Run(string[] args)
{
    try
    {
        return args.FirstOrDefault() switch
        {
            "keygen" => KeyGen(Options(args)),
            "release" => Release(Options(args)),
            "verify" => Verify(Options(args)),
            _ => Usage(),
        };
    }
    catch (Exception e) when (e is ToolException or UpdateException or RulePackException or IOException or CryptographicException)
    {
        Console.Error.WriteLine("error: " + e.Message);
        return 1;
    }
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          Tubiqo.Sign keygen  --out <folder>
          Tubiqo.Sign release --key <private.pem> --out <folder> [--rulepack <file>] [--installer <file> --app-version <x.y.z>]
          Tubiqo.Sign verify  --dir <folder> --public-key <base64>
        """);
    return 2;
}

static Dictionary<string, string> Options(string[] args)
{
    var o = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ToolException($"Unexpected argument '{args[i]}'.");
        o[args[i][2..]] = args[++i];
    }
    return o;
}

static string Need(Dictionary<string, string> o, string name) =>
    o.TryGetValue(name, out var v) ? v : throw new ToolException($"--{name} is required.");

static int KeyGen(Dictionary<string, string> o)
{
    var dir = Need(o, "out");
    var keyFile = Path.Combine(dir, "tubiqo-signing-key.pem");
    if (File.Exists(keyFile)) throw new ToolException($"{keyFile} already exists; refusing to overwrite a signing key.");
    Directory.CreateDirectory(dir);

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using (var f = CreateOwnerOnly(keyFile))
    using (var w = new StreamWriter(f, new UTF8Encoding(false)))
        w.Write(key.ExportPkcs8PrivateKeyPem());

    var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    File.WriteAllText(Path.Combine(dir, "tubiqo-signing-key.public.txt"), publicKey + Environment.NewLine);

    Console.WriteLine($"Private key written to {keyFile}");
    Console.WriteLine("Keep it secret and back it up somewhere safe. Never commit it.");
    Console.WriteLine();
    Console.WriteLine("Public key (put this in src/Tubiqo.App/ReleaseKey.cs):");
    Console.WriteLine(publicKey);
    return 0;
}

// The private key file gets an access list of just the current user: no inherited entries,
// so other accounts, groups and sandboxed apps can't read it.
static FileStream CreateOwnerOnly(string path)
{
    if (!OperatingSystem.IsWindows()) return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    var security = new System.Security.AccessControl.FileSecurity();
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
        System.Security.Principal.WindowsIdentity.GetCurrent().User!,
        System.Security.AccessControl.FileSystemRights.FullControl,
        System.Security.AccessControl.AccessControlType.Allow));
    return new FileInfo(path).Create(FileMode.CreateNew, System.Security.AccessControl.FileSystemRights.Write,
        FileShare.None, 4096, FileOptions.None, security);
}

static int Release(Dictionary<string, string> o)
{
    var outDir = Need(o, "out");
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Need(o, "key")));
    if (key.KeySize != 256) throw new ToolException("The signing key must be ECDSA P-256.");

    object? app = null, rulePack = null;
    Directory.CreateDirectory(outDir);

    if (o.TryGetValue("rulepack", out var packPath))
    {
        var bytes = File.ReadAllBytes(packPath);
        var pack = RulePack.Parse(Encoding.UTF8.GetString(bytes)); // the same checks the app applies
        if (!Version.TryParse(pack.Version, out _))
            throw new ToolException($"Rule Pack version '{pack.Version}' must look like 2026.11.22.1 so the app can compare it.");
        rulePack = Asset(pack.Version, packPath, bytes, outDir, "rulepack.json");
        Console.WriteLine($"Rule Pack {pack.Version}: {pack.Rules.Count} rules");
    }

    if (o.TryGetValue("installer", out var installerPath))
    {
        var version = Need(o, "app-version");
        if (!Version.TryParse(version, out _)) throw new ToolException($"App version '{version}' is not a version number.");
        app = Asset(version, installerPath, File.ReadAllBytes(installerPath), outDir, Path.GetFileName(installerPath));
        Console.WriteLine($"App {version}: {Path.GetFileName(installerPath)}");
    }

    if (app == null && rulePack == null) throw new ToolException("Give --rulepack and/or --installer.");

    var manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { format = 1, app, rulePack },
        new JsonSerializerOptions { WriteIndented = true }) + "\n");
    ReleaseManifest.Parse(Encoding.UTF8.GetString(manifest)); // the app must accept what we write

    var signature = ReleaseSigner.Sign(manifest, key);
    var verifier = new SignatureVerifier(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    if (!verifier.Verify(manifest, signature)) throw new ToolException("Self-check of the signature failed.");

    File.WriteAllBytes(Path.Combine(outDir, ReleaseManifest.FileName), manifest);
    File.WriteAllText(Path.Combine(outDir, ReleaseManifest.SignatureFileName), signature + "\n");
    Console.WriteLine($"Wrote {ReleaseManifest.FileName} and {ReleaseManifest.SignatureFileName} to {outDir}");
    Console.WriteLine("Attach every file in that folder to the GitHub release.");
    return 0;
}

static object Asset(string version, string sourcePath, byte[] bytes, string outDir, string fileName)
{
    var target = Path.Combine(outDir, fileName);
    if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        File.WriteAllBytes(target, bytes);
    return new { version, file = fileName, size = bytes.LongLength, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
}

static int Verify(Dictionary<string, string> o)
{
    var dir = Need(o, "dir");
    var verifier = new SignatureVerifier(Need(o, "public-key"));
    var manifestBytes = File.ReadAllBytes(Path.Combine(dir, ReleaseManifest.FileName));
    var signature = File.ReadAllText(Path.Combine(dir, ReleaseManifest.SignatureFileName));
    if (!verifier.Verify(manifestBytes, signature)) throw new ToolException("Signature does NOT match this public key.");

    var manifest = ReleaseManifest.Parse(Encoding.UTF8.GetString(manifestBytes));
    foreach (var asset in new[] { manifest.App, manifest.RulePack }.OfType<ReleaseAsset>())
    {
        var bytes = File.ReadAllBytes(Path.Combine(dir, asset.File));
        var ok = bytes.LongLength == asset.Size && Convert.ToHexStringLower(SHA256.HashData(bytes)) == asset.Sha256;
        Console.WriteLine($"{asset.File} {asset.Version}: {(ok ? "OK" : "MISMATCH")}");
        if (!ok) return 1;
    }
    Console.WriteLine("Signature OK.");
    return 0;
}

sealed class ToolException(string message) : Exception(message);
