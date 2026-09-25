using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Shared, fail-closed authorization for the permission values carried by a house or coffer.
/// The values are the client/content enum; no content ids or relationship names are inferred here.
/// </summary>
public static class HousingPermissionRules
{
    public static bool IsDefined(HousingPermission permission) =>
        permission is HousingPermission.Private
            or HousingPermission.Guild
            or HousingPermission.Public
            or HousingPermission.Family;

    /// <summary>
    /// Checks the selected house/coffer access mode against the owner and actor. Private coffer
    /// containers can opt into owner-only access; normal house furniture keeps account access.
    /// </summary>
    public static bool CanAccess(Character actor, uint ownerId, HousingPermission permission,
        bool privateOwnerOnly = false)
    {
        if (actor == null || ownerId == 0 || !IsDefined(permission))
            return false;

        if (actor.Id == ownerId)
            return true;

        switch (permission)
        {
            case HousingPermission.Public:
                return true;
            case HousingPermission.Private:
                if (privateOwnerOnly)
                    return actor.Id == ownerId;
                if (actor.Id == ownerId)
                    return true;
                var ownerAccountId = NameManager.Instance.GetCharacterAccount(ownerId);
                return ownerAccountId != 0 && ownerAccountId == actor.AccountId;
            case HousingPermission.Family:
                var ownerFamilyId = FamilyManager.Instance.GetFamilyOfCharacter(ownerId);
                return ownerFamilyId != 0 && ownerFamilyId == actor.Family;
            case HousingPermission.Guild:
                var ownerGuildId = ExpeditionManager.Instance.GetExpeditionOfCharacter(ownerId);
                return ownerGuildId != 0 && ownerGuildId == actor.Expedition?.Id;
            default:
                return false;
        }
    }

    /// <summary>
    /// A player may only select a family/guild mode when they belong to that relationship. The
    /// owner-only check remains with the caller that owns the house/coffer.
    /// </summary>
    public static bool CanSelect(Character actor, HousingPermission permission)
    {
        if (actor == null || !IsDefined(permission))
            return false;

        return permission switch
        {
            HousingPermission.Family => actor.Family != 0,
            HousingPermission.Guild => actor.Expedition?.Id > 0,
            _ => true
        };
    }
}
