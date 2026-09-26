using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Server descriptor for the client's random-shop store window. The compact table supplies only the
/// merchant pack id; the existing random-shop manager owns window rolling and persistence.
/// </summary>
public sealed class DoodadFuncRandomStoreUi : DoodadFuncTemplate
{
    /// <summary>doodad_func_random_store_uis.merchant_random_pack_id.</summary>
    public uint MerchantRandomPackId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character || owner == null)
            return;

        if (!TryAuthorize(character, owner, this))
            return;

        if (!TryValidateInteraction(character, owner))
            return;

        // The descriptor is authoritative only when it agrees with the loaded doodad-to-pack
        // relationship. This prevents a stale or mismatched function row from opening another shop.
        var contentPackId = RandomMerchantGameData.Instance.GetPackIdForDoodad(owner.TemplateId);
        if (contentPackId == 0 || contentPackId != MerchantRandomPackId)
        {
            Logger.Error(
                "DoodadFuncRandomStoreUi descriptor {0} on doodad template {1} points to pack {2}, " +
                "but loaded content maps it to pack {3}",
                Id, owner.TemplateId, MerchantRandomPackId, contentPackId);
            character.SendErrorMessage(ErrorMessageType.NoInteractionAvailable);
            return;
        }

        // The client opens the store and then sends the existing random-shop request, which
        // carries the client's own shop-open type. This descriptor table has no such type, so
        // the handler validates the relationship and leaves the window response to that request.
        Logger.Debug(
            "DoodadFuncRandomStoreUi accepted doodad template {0}, pack {1}; awaiting the client random-shop request",
            owner.TemplateId, MerchantRandomPackId);

        // The shipped row has next_phase=-1. Keep the phase stable even if a caller supplied a
        // non-zero nextPhase value; the descriptor does not consume the doodad.
        owner.ToNextPhase = false;
    }

    internal static bool TryAuthorize(Character character, Doodad owner, DoodadFuncRandomStoreUi descriptor)
    {
        if (character == null || owner == null || descriptor == null)
            return false;

        foreach (var function in owner.CurrentFuncs ?? [])
        {
            if (function.FuncType != nameof(DoodadFuncRandomStoreUi) || function.FuncId != descriptor.Id)
                continue;

            // The shipped Q10 descriptor is public. Reject out-of-range values before any enum
            // conversion so a wrapped value can never become Public.
            if (function.PermId > byte.MaxValue)
            {
                Logger.Error(
                    "DoodadFuncRandomStoreUi descriptor {0} has out-of-range perm_id {1}",
                    descriptor.Id, function.PermId);
                character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                return false;
            }

            // The shipped Q10 descriptor is public. Other permission kinds are not guessed here;
            // fail closed until an evidence-backed permission policy is available for this UI.
            if (function.PermId == (uint)DoodadFuncPermission.Public)
                return true;

            character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
            return false;
        }

        Logger.Error(
            "DoodadFuncRandomStoreUi descriptor {0} is not present in the doodad's current function group",
            descriptor.Id);
        return false;
    }

    internal static bool TryValidateInteraction(Character character, Doodad owner)
    {
        return character?.ParentWorld != null &&
               owner?.ParentWorld == character.ParentWorld &&
               owner.IsVisible &&
               owner.ObjId != 0;
    }
}
