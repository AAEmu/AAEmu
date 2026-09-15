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
    /// <summary>
    /// How close a character has to stand to the courthouse to watch its case. The gallery is a room in
    /// that building: the placed juror chairs span about five metres and the courtroom a few tens, so
    /// this covers the room and its forecourt while still refusing a request sent from anywhere else in
    /// the world - without it, any player could ask to watch the nearest case and be handed the
    /// defendant's crime file from the other side of the continent.
    /// </summary>
    public const float GalleryRadiusMetres = 60f;

    /// <summary>True while a case is being heard, i.e. from the summon until the ruling closes it.</summary>
    public static bool CanWatch(TrialState state) => state is not (TrialState.PostSentence or TrialState.Free);

    /// <summary>The defendant and a seated juror are parties, not onlookers.</summary>
    public static bool CanJoinGallery(bool isDefendant, bool isSeatedJuror) => !isDefendant && !isSeatedJuror;

    /// <summary>True when the character is standing in (or at the door of) the courtroom.</summary>
    public static bool InGalleryRange(double distanceMetres) => distanceMetres <= GalleryRadiusMetres;
}
