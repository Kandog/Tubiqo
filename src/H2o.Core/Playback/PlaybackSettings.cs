using H2o.Core.Storage;

namespace H2o.Core.Playback;

/// <summary>Stores the user's Playback Policy in h2o Data. AV1 stays excluded; only the Quality Cap is a user choice.</summary>
public sealed class PlaybackSettings(H2oDatabase db)
{
    const string CapKey = "playback.qualityCap";

    public PlaybackPolicy Policy
    {
        get => int.TryParse(db.GetSetting(CapKey), out var cap) && PlaybackPolicy.ValidCaps.Contains(cap)
            ? PlaybackPolicy.Default.WithQualityCap(cap)
            : PlaybackPolicy.Default;
        set => db.SetSetting(CapKey, value.QualityCap.ToString());
    }
}
