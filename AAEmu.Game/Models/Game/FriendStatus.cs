namespace AAEmu.Game.Models.Game;

/// <summary>
/// Relationship state used by the 10.0.2.13 Friend wire record.
/// </summary>
public enum FriendStatus : byte
{
    Accepted = 0,
    OutgoingRequest = 1,
    IncomingRequest = 2
}
