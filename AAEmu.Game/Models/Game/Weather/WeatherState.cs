namespace AAEmu.Game.Models.Game.Weather;

/// <summary>
/// Server-side weather state for the schedule-driven weather cycle. This is a typed classification
/// of the weather, never derived from display names: configuration keys and content rows map onto
/// these values. Snow is the only weather this client has a packet for, so it is the only state a
/// phase can produce besides clear skies.
/// </summary>
public enum WeatherState
{
    /// <summary>No configured weather phase is open.</summary>
    Clear = 0,

    /// <summary>Snow phase. Publishes through the global snow packet and feature bit.</summary>
    Snow = 1,
}
