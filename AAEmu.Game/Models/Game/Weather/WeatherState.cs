namespace AAEmu.Game.Models.Game.Weather;

/// <summary>
/// Server-side weather state for the schedule-driven weather cycle. This is a typed classification
/// of the weather, never derived from display names: configuration keys and content rows map onto
/// these values, and only <see cref="Snow"/> has a client packet to publish.
/// </summary>
public enum WeatherState
{
    /// <summary>No configured weather phase is open.</summary>
    Clear = 0,

    /// <summary>Rain phase. Tracked server-side; the 10.0.2.13 client has no rain-state packet.</summary>
    Rain = 1,

    /// <summary>Wind phase. Tracked server-side; the 10.0.2.13 client has no wind-state packet.</summary>
    Wind = 2,

    /// <summary>Snow phase. Publishes through the established global snow packet and feature bit.</summary>
    Snow = 3,
}
