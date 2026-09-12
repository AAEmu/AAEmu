using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>Associates the requesting character's farmhand with the finished house that owns this doodad.</summary>
public sealed class DoodadFuncBindButler : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character)
            return;

        var house = ResolveHouse(character, owner, HousingManager.Instance.GetHouseById);
        if (house == null)
        {
            character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
            return;
        }

        lock (house.LifecycleSyncRoot)
        {
            var result = ButlerManager.Instance.Bind(character, house, HousingManager.Instance.GetHouseById);
            if (!result.Success)
            {
                character.SendErrorMessage(result.Error);
                return;
            }

            var presentation = ButlerManager.Instance.GetPresentation(character);
            if (!presentation.IsBound)
            {
                character.SendErrorMessage(ErrorMessageType.InternalError);
                return;
            }

            character.SendPacket(new SCButlerBoundPacket(
                presentation.Info,
                presentation.HouseName,
                (ushort)ErrorMessageType.NoErrorMessage));
        }
    }

    internal static House ResolveHouse(Character character, Doodad owner, Func<uint, House> houseById)
    {
        if (character == null || owner == null || owner.OwnerType != DoodadOwnerType.Housing)
            return null;

        var parentHouse = owner.ParentObj as House;
        var registeredHouse = owner.OwnerDbId > 0 ? houseById(owner.OwnerDbId) : null;
        if (parentHouse != null && !ReferenceEquals(parentHouse, registeredHouse))
            return null;
        var house = parentHouse ?? registeredHouse;
        if (house == null || owner.OwnerDbId != house.Id || house.ParentWorld == null)
            return null;

        var hasParentLink = ReferenceEquals(owner.ParentObj, house) ||
            owner.ParentObj == null && owner.ParentObjId == house.ObjId;
        return hasParentLink && ReferenceEquals(owner.ParentWorld, house.ParentWorld) &&
               ReferenceEquals(character.ParentWorld, house.ParentWorld)
            ? house
            : null;
    }
}
