namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// Locked 10.0.2.13 instant-game / indun queue SC wire (client handler branches).
/// Do not send raw bool combinations — use G2C.SCCancelInstantGamePacket factories.
///
/// SCAppliedToInstantGame 0x1D2: u32 catalog/type, u16 ErrorMessage (0 = ok).
///
/// SCCancelInstantGame 0x1D3: u16 ErrorMessage, u8 fromHomeland.
/// Client branch selection (10.0.2.13):
///   fromHomeland=1, ErrorMessage=0 → full queue clear (dungeon/battlefield apply UI).
///   fromHomeland=0, ErrorMessage!=0 → set error on instance UI (no full clear).
///   fromHomeland=0, ErrorMessage=0 → no-op (never send).
/// The wire name fromHomeland selects the clear branch, not “player is in homeland”.
/// </summary>
public static class InstantGameWireContract
{
    public const ushort OpcodeApplied = 0x1D2;
    public const ushort OpcodeCancel = 0x1D3;

    /// <summary>Wire fromHomeland byte for queue-clear acks.</summary>
    public const byte CancelBranchClearQueue = 1;

    /// <summary>Wire fromHomeland byte for error-only acks.</summary>
    public const byte CancelBranchErrorOnly = 0;

    /// <summary>
    /// The u32 <c>type</c> carried by the instant-game packets names a battle field, and a dungeon
    /// has none. Zero is the only value the client treats as "no battle field"; anything else it
    /// resolves in its battle field table and uses without checking the result, so a dungeon match
    /// must send this instead of its catalog id. Battle field ids start at one, and a dungeon is
    /// identified instead by the zone group the client already holds for the instance.
    /// </summary>
    public const uint NoBattleFieldType = 0;

    /// <summary>
    /// Round a match is on when it starts. Battle fields count rounds up from here; a dungeon runs
    /// as a single round for its whole duration and never leaves it.
    /// </summary>
    public const uint FirstRound = 1;

    /// <summary>
    /// Value for <c>SCInviteToInstantGame.maxEntry</c> that makes the client open the plain
    /// "Enter Instance" dialog (<c>DLG_TASK_JOIN_INSTANT_GAME</c>). Any other value opens the squad
    /// "Allow Team Queue" dialog instead. A dungeon invite must send this; a battle field keeps
    /// sending its real roster cap so the team-queue UI can show accept counts.
    /// </summary>
    public const uint DungeonEnterDialogSelector = 1;
}
