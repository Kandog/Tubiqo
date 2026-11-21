using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tubiqo.Core.Updates;

/// <summary>One downloadable file in a release, pinned by size and SHA-256.</summary>
public sealed record ReleaseAsset(Version Version, string File, long Size, string Sha256);

/// <summary>
/// The signed description of a GitHub release (<c>tubiqo-release.json</c>). Its signature covers
/// these exact bytes, and each asset is then checked against the hash listed here.
/// </summary>
public sealed partial record ReleaseManifest(ReleaseAsset? App, ReleaseAsset? RulePack)
{
    public const string FileName = "tubiqo-release.json";
    public const string SignatureFileName = "tubiqo-release.json.sig";
    public const long MaxManifestSize = 64 * 1024;
    public const long MaxSignatureSize = 1024;
    public const long MaxRulePackSize = 1024 * 1024;
    public const long MaxInstallerSize = 500L * 1024 * 1024;
    const int SupportedFormat = 1;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    public static ReleaseManifest Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new UpdateException("Release manifest must be a JSON object.");
            if (!root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.Number || !f.TryGetInt32(out var format) || format != SupportedFormat)
                throw new UpdateException("Unsupported release manifest format.");

            var app = Asset(root, "app", MaxInstallerSize);
            var pack = Asset(root, "rulePack", MaxRulePackSize);
            if (app == null && pack == null) throw new UpdateException("Release manifest lists nothing to update.");
            return new ReleaseManifest(app, pack);
        }
        catch (JsonException) { throw new UpdateException("Release manifest is not valid JSON."); }
    }

    static ReleaseAsset? Asset(JsonElement root, string name, long maxSize)
    {
        if (!root.TryGetProperty(name, out var a) || a.ValueKind == JsonValueKind.Null) return null;
        if (a.ValueKind != JsonValueKind.Object) throw new UpdateException($"'{name}' must be an object.");

        if (!a.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String || !Version.TryParse(v.GetString(), out var version))
            throw new UpdateException($"'{name}' has no valid version.");
        if (!a.TryGetProperty("file", out var fl) || fl.ValueKind != JsonValueKind.String || !FileNamePattern().IsMatch(fl.GetString()!))
            throw new UpdateException($"'{name}' has no valid file name.");
        if (!a.TryGetProperty("size", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt64(out var size) || size <= 0 || size > maxSize)
            throw new UpdateException($"'{name}' size is missing or over its limit.");
        if (!a.TryGetProperty("sha256", out var h) || h.ValueKind != JsonValueKind.String || !Sha256Pattern().IsMatch(h.GetString()!))
            throw new UpdateException($"'{name}' has no valid SHA-256.");

        return new ReleaseAsset(version, fl.GetString()!, size, h.GetString()!);
    }
}

/// <summary>What a verified release offers compared with what's installed.</summary>
public sealed record UpdatePlan(ReleaseManifest Manifest, bool AppUpdate, bool RulePackUpdate)
{
    public bool Any => AppUpdate || RulePackUpdate;
}

public static class UpdatePlanner
{
    /// <summary>Offers only strictly newer versions, so an old (still validly signed) release can never downgrade Tubiqo.</summary>
    public static UpdatePlan Plan(ReleaseManifest manifest, Version currentApp, Version currentRulePack) => new(
        manifest,
        manifest.App != null && manifest.App.Version > currentApp,
        manifest.RulePack != null && manifest.RulePack.Version > currentRulePack);
}
