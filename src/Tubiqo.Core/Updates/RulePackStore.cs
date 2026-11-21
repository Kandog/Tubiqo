using System.Security.Cryptography;
using System.Text;
using Tubiqo.Core.Rules;

namespace Tubiqo.Core.Updates;

/// <summary>A downloaded Rule Pack that passed every check, with its comparable version.</summary>
public sealed record InstalledRulePack(RulePack Pack, Version Version);

/// <summary>
/// Keeps downloaded Rule Packs in Tubiqo Data: the current one and the previous one for rollback.
/// Each is stored with its signed manifest and re-verified every time it's loaded, so a pack
/// edited on disk is ignored and the built-in pack is used instead.
/// </summary>
public sealed class RulePackStore(string dataRoot, SignatureVerifier verifier)
{
    const string ManifestFile = "manifest.json", SignatureFile = "manifest.sig", PackFile = "rulepack.json";

    readonly string root = Path.Combine(dataRoot, "rulepacks");
    string Current => Path.Combine(root, "current");
    string Previous => Path.Combine(root, "previous");

    public InstalledRulePack? LoadCurrent() => Load(Current);
    public bool HasPrevious => Load(Previous) != null;

    /// <summary>Checks a downloaded pack against its signed manifest and makes it current. Throws <see cref="UpdateException"/> on any problem, leaving everything as it was.</summary>
    public InstalledRulePack Install(byte[] manifestBytes, string signature, byte[] packBytes)
    {
        var installed = Check(manifestBytes, signature, packBytes, out var problem)
            ?? throw new UpdateException(problem);

        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, ManifestFile), manifestBytes);
        File.WriteAllText(Path.Combine(staging, SignatureFile), signature.Trim());
        File.WriteAllBytes(Path.Combine(staging, PackFile), packBytes);

        if (Directory.Exists(Previous)) Directory.Delete(Previous, recursive: true);
        if (Directory.Exists(Current)) Directory.Move(Current, Previous);
        Directory.Move(staging, Current);
        return installed;
    }

    /// <summary>Swaps the current and previous packs. False when there's no valid previous pack.</summary>
    public bool Rollback()
    {
        if (Load(Previous) == null) return false;
        var swap = Path.Combine(root, "swap-" + Guid.NewGuid().ToString("N"));
        Directory.Move(Current, swap);
        Directory.Move(Previous, Current);
        Directory.Move(swap, Previous);
        return true;
    }

    /// <summary>The pack to use: the installed one unless the app's built-in pack is newer (for example after an app update).</summary>
    public static RulePack Choose(RulePack builtIn, InstalledRulePack? installed)
    {
        var builtInVersion = Version.TryParse(builtIn.Version, out var v) ? v : new Version(0, 0);
        return installed != null && installed.Version >= builtInVersion ? installed.Pack : builtIn;
    }

    InstalledRulePack? Load(string dir)
    {
        try
        {
            var manifest = File.ReadAllBytes(Path.Combine(dir, ManifestFile));
            var signature = File.ReadAllText(Path.Combine(dir, SignatureFile));
            var pack = File.ReadAllBytes(Path.Combine(dir, PackFile));
            return Check(manifest, signature, pack, out _);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    InstalledRulePack? Check(byte[] manifestBytes, string signature, byte[] packBytes, out string problem)
    {
        problem = "";
        if (!verifier.Verify(manifestBytes, signature)) { problem = "The release signature is not valid."; return null; }

        ReleaseManifest manifest;
        try { manifest = ReleaseManifest.Parse(Encoding.UTF8.GetString(manifestBytes)); }
        catch (UpdateException e) { problem = e.Message; return null; }
        if (manifest.RulePack is not { } asset) { problem = "The release has no Rule Pack."; return null; }

        if (packBytes.Length != asset.Size || Convert.ToHexStringLower(SHA256.HashData(packBytes)) != asset.Sha256)
        {
            problem = "The Rule Pack doesn't match its signed size and hash.";
            return null;
        }

        RulePack pack;
        try { pack = RulePack.Parse(Encoding.UTF8.GetString(packBytes)); }
        catch (RulePackException e) { problem = "The Rule Pack failed its checks: " + e.Message; return null; }

        if (!Version.TryParse(pack.Version, out var version) || version != asset.Version)
        {
            problem = "The Rule Pack's version doesn't match the signed release.";
            return null;
        }
        return new InstalledRulePack(pack, version);
    }
}
