namespace AAEmu.Game.Models.StaticValues;

/// <summary>
/// Discriminator on <c>CSInstantTime</c> (<c>u32 timeType</c>). The client exports only
/// <c>INSTANT_TIME_EXPEDITION_REJOIN</c> (4) to Lua; the mute kinds are not named constants.
/// They are the values the two NotRecv binds write:
/// <c>X2Faction:RequestMobilizationOrderNotRecv</c> sends kind 1,
/// <c>X2Faction:RequestExpeditionSummonNotRecv</c> sends kind 3.
/// Only <see cref="MobilizationOrderNotRecv"/> is handled here.
/// </summary>
public enum InstantTimeKind : uint
{
    MobilizationOrderNotRecv = 1,
    ExpeditionSummonNotRecv = 3,
}
