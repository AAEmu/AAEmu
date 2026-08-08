namespace AAEmu.Game.Models.Game.Chat;

public enum ChatType : short
{
    Whispered = -4,
    Whisper = -3,
    System = -2,
    Notice = -1,
    White = 0, // say
    Shout = 1,
    Trade = 2,
    GroupFind = 3,
    Party = 4,
    Raid = 5,
    Region = 6,
    Clan = 7,
    System2 = 8,
    Family = 9,
    RaidLeader = 10,
    Judge = 11,
    Ally = 14,
    User = 15,

    /// <summary>
    /// The server-wide channel the client labels CSM (command /u). Measured from a live client:
    /// it sends type 18 with an otherwise empty channel descriptor (chat = 0x12, no subType, no
    /// faction), which fits a channel scoped to nothing at all.
    /// </summary>
    Csm = 18
}