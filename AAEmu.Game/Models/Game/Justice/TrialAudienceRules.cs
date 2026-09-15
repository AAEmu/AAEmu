namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// Who may watch a case, and what the gallery gets when someone takes a seat in it.
/// </summary>
/// <remarks>
/// The gallery is onlookers only: the defendant and the bench are parties to the case. A case can be
/// watched while it is being heard - once the ruling has closed it there is nothing left to show, and
/// the client's court window would open on an empty file.
/// </remarks>
public static class TrialAudienceRules
{
    /// <summary>True while a case is being heard, i.e. from the summon until the ruling closes it.</summary>
    public static bool CanWatch(TrialState state) => state is not (TrialState.PostSentence or TrialState.Free);

    /// <summary>The defendant and a seated juror are parties, not onlookers.</summary>
    public static bool CanJoinGallery(bool isDefendant, bool isSeatedJuror) => !isDefendant && !isSeatedJuror;
}
