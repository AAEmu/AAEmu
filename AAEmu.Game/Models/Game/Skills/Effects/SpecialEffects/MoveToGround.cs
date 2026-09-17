using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Snaps a unit onto the terrain floor at the XY it already holds. The teleport and blink skills carry it
/// (39293 순간 이동: 안개, 40333 / 41756 급습, 36355 멜리사라의 잠입): the client moves the character with its own
/// effects and the server lands it, so the two have to agree on the Z or the zone pulls the character back.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 38 <c>special_effects</c> rows of type 73, 11 of them reachable
/// through <c>skill_effects</c> (no buff and no trigger carries the type). All 11 are movement skills the
/// caster performs: 10 are <c>skills.target_type_id</c> 6 (pos), where the framework hands the effect a
/// synthetic position unit rather than the unit being moved, and 레이븐의 급습 47006 is the single
/// hostile-target row. The unit moved is therefore the caster, and the effect's own target is not read.
/// <c>value1</c> is 0 on 35 of the 38 rows; the three that set it (15 on 36622 and 20 on 35808, whose two
/// rows are not reachable) say nothing about a unit, so it is not applied.
/// </remarks>
public class MoveToGround : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.MoveToGround;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Unit unit || unit.ParentWorld == null)
            return;

        var position = unit.Transform.World.Position;
        var ground = TerrainFloor.SampleHeightmap(unit.ParentWorld, position.X, position.Y);
        if (ground <= 0f)
            ground = TerrainFloor.SampleHeightmap(unit.Transform.ZoneId, position.X, position.Y);

        var overWater = TerrainFloor.TryWaterSurface(unit.ParentWorld, position, out var waterSurfaceZ);
        if (!GroundSnapRules.TryGetFloorZ(position.Z, ground, overWater, waterSurfaceZ, out var z))
            return;

        switch (unit)
        {
            case Character character:
                // SetPosition writes the LOCAL transform: convert the world landing first, exactly as Blink
                // does, so a character parented to a house seat or standing on a ship is not moved by the
                // parent's offset. The packet stays world-space.
                var local = character.Transform.GetLocalFromWorld(position.X, position.Y, z);
                character.SetPosition(local.X, local.Y, local.Z,
                    character.Transform.Local.Rotation.X,
                    character.Transform.Local.Rotation.Y,
                    character.Transform.Local.Rotation.Z);
                character.SendPacket(new SCBlinkUnitPacket(character.ObjId, 0f, 0f, true, position.X, position.Y, z));
                if (WorldIntegration.ZoneAuthority)
                {
                    WorldIntegration.RelayBlinkToZone?.Invoke(
                        character.ObjId, character.ObjId, true, position.X, position.Y, z);
                }

                break;
            case Npc npc when ZoneOwnedUnitRules.IsDrivenByZone(WorldIntegration.ZoneAuthority, npc.IsZoneMirror):
                // The dedicate owns the NPC's position; a World-side write would be undone by its next
                // movement record, so the drop has to be relayed the way TeleportToUnit relays one.
                WorldIntegration.RelayBlinkToZone?.Invoke(npc.ObjId, npc.ObjId, true, position.X, position.Y, z);
                break;
            default:
                var other = unit.Transform.GetLocalFromWorld(position.X, position.Y, z);
                unit.SetPosition(other.X, other.Y, other.Z,
                    unit.Transform.Local.Rotation.X,
                    unit.Transform.Local.Rotation.Y,
                    unit.Transform.Local.Rotation.Z);
                unit.BroadcastPacket(new SCBlinkUnitPacket(unit.ObjId, 0f, 0f, true, position.X, position.Y, z), true);
                break;
        }
    }
}
