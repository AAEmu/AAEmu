using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// Why a public-farm placement request was refused. The values are the only outcomes the wire
/// handler turns into an error message, so they stay enumerated rather than free-form strings.
/// </summary>
public enum PublicFarmPlaceFailure
{
    /// <summary>The request is acceptable.</summary>
    None = 0,

    /// <summary>The request named a farm type this build does not know.</summary>
    UnknownType,

    /// <summary>The request named a farm type the target area does not host.</summary>
    TypeMismatch,

    /// <summary>The farm group has no capacity row in content, so nothing may be planted there.</summary>
    NoContent,

    /// <summary>The request asked for zero doodads.</summary>
    EmptyRequest,

    /// <summary>Honouring the request would push the character past the farm group's content capacity.</summary>
    CountOver,
}

/// <summary>
/// Pure decision rules for the public-farm request packets. Every input is supplied by the caller so
/// the wire packets, the farm manager and the tests all share one implementation, and so no shipped
/// value (a farm type, a capacity, a planted count) is baked into the decision itself.
/// </summary>
public static class PublicFarmPlacementRules
{
    /// <summary>
    /// A farm type is usable only when it is a real, non-empty farm tab. <see cref="FarmType.Invalid"/>
    /// is the "not in a farm" sentinel and is never a usable request.
    /// </summary>
    public static bool IsUsableType(FarmType type) => type != FarmType.Invalid && Enum.IsDefined(type);

    /// <summary>
    /// Validates one placement request against the area it targets and the group's content capacity.
    /// </summary>
    /// <param name="requestedType">Farm type carried by the request.</param>
    /// <param name="areaType">Farm type the target point actually resolves to.</param>
    /// <param name="maxCount">Per-character capacity for the group, from content. Zero means "no row".</param>
    /// <param name="alreadyPlanted">How many of this type the character already has planted.</param>
    /// <param name="requestedCount">How many the request asks to add.</param>
    public static PublicFarmPlaceFailure ValidatePlacement(
        FarmType requestedType,
        FarmType areaType,
        uint maxCount,
        uint alreadyPlanted,
        uint requestedCount)
    {
        if (!IsUsableType(requestedType))
            return PublicFarmPlaceFailure.UnknownType;

        if (areaType != requestedType)
            return PublicFarmPlaceFailure.TypeMismatch;

        // A group with no capacity row is not a farm this build can plant in; refuse instead of
        // guessing a default capacity.
        if (maxCount == 0)
            return PublicFarmPlaceFailure.NoContent;

        if (requestedCount == 0)
            return PublicFarmPlaceFailure.EmptyRequest;

        // Widen before adding so a hostile count near uint.MaxValue cannot wrap into "fits".
        if ((ulong)alreadyPlanted + requestedCount > maxCount)
            return PublicFarmPlaceFailure.CountOver;

        return PublicFarmPlaceFailure.None;
    }

    /// <summary>
    /// Maps a placement failure onto the client error the farm content already names. Anything the
    /// rules accept maps to <c>null</c> so the caller sends no error at all.
    /// </summary>
    public static AAEmu.Game.Models.Game.ErrorMessageType? ToErrorMessage(PublicFarmPlaceFailure failure) => failure switch
    {
        PublicFarmPlaceFailure.None => null,
        // A type the client should never send, and a group with no content row, are both "this is
        // not a farm you may plant in" as far as the client is concerned.
        PublicFarmPlaceFailure.UnknownType => AAEmu.Game.Models.Game.ErrorMessageType.CommonFarmNotAllowedType,
        PublicFarmPlaceFailure.TypeMismatch => AAEmu.Game.Models.Game.ErrorMessageType.CommonFarmNotAllowedType,
        PublicFarmPlaceFailure.NoContent => AAEmu.Game.Models.Game.ErrorMessageType.CommonFarmNotAllowedType,
        PublicFarmPlaceFailure.EmptyRequest => AAEmu.Game.Models.Game.ErrorMessageType.CommonFarmNotAllowedType,
        PublicFarmPlaceFailure.CountOver => AAEmu.Game.Models.Game.ErrorMessageType.CommonFarmCountOver,
        _ => AAEmu.Game.Models.Game.ErrorMessageType.CommonFarmNotAllowedType,
    };
}
