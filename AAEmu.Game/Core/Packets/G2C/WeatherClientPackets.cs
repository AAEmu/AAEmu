using AAEmu.Game.Models.Game.Features;

namespace AAEmu.Game.Core.Packets.G2C;

public static class WeatherClientPackets
{
    /// <summary>
    /// Captures the connection-local feature blob and publishes the current global snow state.
    /// Native 10.0.2.13 copies the blob into ClientPlayer in handler <c> </c>, then
    /// <c>OnLoadingWorldComplete</c> at <c> </c> applies its snow bit during world load.
    /// </summary>
    public static FeatureSet InitialFeatures(FeatureSet source, bool isSnowing)
    {
        ArgumentNullException.ThrowIfNull(source);

        var snapshot = source.Copy();
        snapshot.Set(Feature.fset_7_2_unknown, isSnowing);
        return snapshot;
    }
}
