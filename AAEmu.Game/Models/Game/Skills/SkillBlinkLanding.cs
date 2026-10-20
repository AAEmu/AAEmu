using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Same-zone hop that keeps the soft blink packet. Recall and ground snaps must not go through the
/// full teleport restream — that looks like loading a zone for a hop that never left it.
/// </summary>
public static class SkillBlinkLanding
{
    public static void Apply(Unit unit, float worldX, float worldY, float worldZ,
        float distance = 0f, float degree = 0f, bool move3D = true)
    {
        if (unit == null)
            return;

        var local = unit.Transform.GetLocalFromWorld(worldX, worldY, worldZ);
        unit.SetPosition(local.X, local.Y, local.Z,
            unit.Transform.Local.Rotation.X,
            unit.Transform.Local.Rotation.Y,
            unit.Transform.Local.Rotation.Z);

        var packet = new SCBlinkUnitPacket(unit.ObjId, distance, degree, move3D, worldX, worldY, worldZ);
        switch (unit)
        {
            case Character character:
                // Drop CSMoveUnit still reporting the cast stand; keep soft blink (no SCTeleportUnit).
                character.ArmBlinkMoveSuppress(worldX, worldY, worldZ);
                // Broadcast so allies watching Grasping Void (and the caster) all see the hop.
                character.BroadcastPacket(packet, true);
                (character.SkillTask?.Skill)?.NoteCasterSoftMoved();
                break;
            case Npc npc when ZoneOwnedUnitRules.IsDrivenByZone(WorldIntegration.ZoneAuthority, npc.IsZoneMirror):
                WorldIntegration.RelayBlinkToZone?.Invoke(npc.ObjId, npc.ObjId, move3D, worldX, worldY, worldZ);
                return;
            default:
                unit.BroadcastPacket(packet, true);
                (unit.SkillTask?.Skill)?.NoteCasterSoftMoved();
                break;
        }

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayBlinkToZone?.Invoke(
                unit.ObjId, unit.ObjId, move3D, worldX, worldY, worldZ);
        }
    }
}
