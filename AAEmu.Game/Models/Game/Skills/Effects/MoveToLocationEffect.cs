using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Recalls the caster to a house they are entitled to return to — the "온기" warmth items
/// (29161 아련한 공간의 온기, 33476 아련한 영지의 온기, 48701 아련한 원정대의 온기).
///
/// <see cref="OwnHouseOnly"/> distinguishes them: the shipped rows are one true and two false, so one variant
/// only returns you to a house you own while the others also accept houses you merely have access to.
/// </summary>
public class MoveToLocationEffect : EffectTemplate
{
    public bool OwnHouseOnly { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character character)
            return;

        var myHouses = new Dictionary<uint, Housing.House>();
        HousingManager.Instance.GetByCharacterId(myHouses, character.Id);
        var candidates = myHouses.Values.ToList();

        // own_house_only false widens the recall to any house that would let this character walk in — a
        // family or guild building, or one left public. AllowedToInteract is the same permission test the
        // door uses, so the recall can never land somewhere the player could not have entered on foot.
        if (!OwnHouseOnly)
        {
            foreach (var house in HousingManager.Instance.GetAllHouses())
            {
                if (!candidates.Contains(house) && house.AllowedToInteract(character))
                    candidates.Add(house);
            }
        }

        // Nearest first, so a player with several buildings is returned to the one they were heading for
        // rather than to whichever happened to load first.
        var destination = candidates
            .OrderBy(h => AAEmu.Game.Utils.MathUtil.CalculateDistance(character.Transform.World.Position, h.Transform.World.Position))
            .FirstOrDefault();

        if (destination == null)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }

        // World routes players by Transform.ZoneId and fails closed, so recalling into a zone with no dedicate
        // drops the character somewhere nobody simulates, with no NPCs and no way back. Refuse instead.
        var destinationZoneId = destination.Transform.ZoneId;
        if (WorldIntegration.ZoneAuthority
            && WorldIntegration.IsZoneLoaded != null
            && !WorldIntegration.IsZoneLoaded(destinationZoneId))
        {
            Logger.Warn($"MoveToLocationEffect: refusing to recall {character.Name} to house {destination.Id}; zone {destinationZoneId} has no ZoneLoaded dedicate");
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }

        Logger.Debug($"MoveToLocationEffect: recalling {character.Name} to house {destination.Id}, ownHouseOnly {OwnHouseOnly}");

        character.DisabledSetPosition = true;
        character.SendPacket(new Core.Packets.G2C.SCTeleportUnitPacket(
            0, 0,
            destination.Transform.World.Position.X,
            destination.Transform.World.Position.Y,
            destination.Transform.World.Position.Z,
            0f));
    }
}
