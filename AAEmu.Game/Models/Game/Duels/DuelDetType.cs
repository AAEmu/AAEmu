namespace AAEmu.Game.Models.Game.Duels;

/// <summary>
/// The outcome code carried by SCDuelEnded. These are the client's own values: it looks the field up
/// in two result-message tables, one for the loser's text and one for the winner's:
///
///   det 0 -> "result_draw"    both tables
///   det 1 -> "result_loser" / "result_winner"   - the isWin flag picks between them
///   det 2 -> "result_cancel"  both tables
///
/// There is no entry 3: the win table ends after three pointers, so a det of 3 reads past it and the
/// client shows garbage. The previous values (Lose=0, Win=1, Surrender=2, Draw=3) were from an older
/// protocol - a draw addressed the missing fourth slot and every decided duel announced "draw" to the
/// loser, which is why no result message ever appeared correctly.
/// </summary>
public enum DuelDetType : byte
{
    /// <summary>Nobody won - both sides see "result_draw".</summary>
    Draw = 0,

    /// <summary>Someone won; the per-recipient isWin flag decides which text each side gets.</summary>
    Decided = 1,

    /// <summary>The duel was broken off (fled past the flag, timed out) - both sides see "result_cancel".</summary>
    Cancel = 2
}
