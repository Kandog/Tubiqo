using System.Text.Json;

namespace H2o.Core.Playback;

/// <summary>
/// The user's rules for which video formats h2o accepts from YouTube: whether AV1 is allowed
/// (it isn't by default, so the GPU can decode) and the Quality Cap, the highest resolution
/// h2o will play, measured on the video's short side so portrait Shorts are capped the same way.
/// </summary>
public sealed record PlaybackPolicy
{
    const string Placeholder = "/*H2O_POLICY*/null";

    public static readonly IReadOnlyList<int> ValidCaps = [144, 240, 360, 480, 720, 1080, 1440, 2160];

    public static PlaybackPolicy Default { get; } = new();

    public bool AllowAv1 { get; init; }
    public int QualityCap { get; private init; } = 1080;

    public PlaybackPolicy WithQualityCap(int cap)
    {
        if (!ValidCaps.Contains(cap)) throw new ArgumentOutOfRangeException(nameof(cap), cap, "Not a YouTube resolution.");
        return this with { QualityCap = cap };
    }

    /// <summary>The settings the page script needs, as JSON.</summary>
    public string ToScriptConfig() =>
        JsonSerializer.Serialize(new { allowAv1 = AllowAv1, maxShortSide = QualityCap });

    /// <summary>Fills the policy placeholder in the page script template.</summary>
    public string BuildScript(string template)
    {
        if (!template.Contains(Placeholder, StringComparison.Ordinal))
            throw new ArgumentException($"Template has no {Placeholder} placeholder.", nameof(template));
        return template.Replace(Placeholder, ToScriptConfig(), StringComparison.Ordinal);
    }
}
