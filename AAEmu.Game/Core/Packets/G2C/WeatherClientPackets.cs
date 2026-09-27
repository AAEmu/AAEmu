using AAEmu.Game.Models.Game.Features;

namespace AAEmu.Game.Core.Packets.G2C;

public static class WeatherClientPackets
{
    /// <summary>
    /// Captures the connection-local feature blob and publishes the current global snow state.
    /// The client copies the blob into its player object when the feature config is
    /// received, then <c>OnLoadingWorldComplete</c> applies the snow bit during world load.
    /// </summary>
    public static FeatureSet InitialFeatures(FeatureSet source, bool isSnowing)
    {
        ArgumentNullException.ThrowIfNull(source);

        var snapshot = source.Copy();
        snapshot.Set(Feature.fset_7_2_unknown, isSnowing);
        return snapshot;
    }
}
