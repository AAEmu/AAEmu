using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.World.Transform;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Applies a skill-driven teleport to the server-side character and tells the client about it. Both
/// sides have to agree on the landing: crossing instances shows a loading screen and the client answers
/// it with CSInstanceLoaded (the only packet that clears DisabledSetPosition), while a same-level
/// teleport is seamless and the server must move the character itself - otherwise only the client moves
/// and the zone simulation keeps pulling the character back to the old spot.
/// </summary>
public static class SkillTeleportLanding
{
    public static void Apply(
        Character character,
        uint worldId,
        uint zoneId,
        uint instanceId,
        float x,
        float y,
        float z,
        float yawRad,
        TeleportReason reason,
        bool stayInZone = false)
    {
        // A rider still parented to a ship or seat would keep following that parent after the
        // landing. Stand up first (no lift-ride Timeout — a recall must not fire the floor mover)
        // and drop mates / slaves / hang before the destination is written.
        BondDoodad.TryRelease(character, timeoutLiftRide: false);
        if (character.ParentWorld != null)
            character.ForceDismount();

        var loadedInstance = !stayInZone &&
                             ReturnTeleportRules.NeedsInstanceLoad(character.Transform.InstanceId, instanceId);

        if (loadedInstance)
        {
            character.DisabledSetPosition = true;
            character.SendPacket(new SCLoadInstancePacket(worldId, zoneId, x, y, z, 0f, 0f, yawRad));
            character.Transform = new Transform(character, null, zoneId, instanceId, x, y, z, yawRad);
        }
        else
        {
            // SetPosition writes the LOCAL transform: convert the world landing first so a parented
            // character (bonded to a doodad, standing on a ship) is not moved by the parent's offset.
            // The rotation is only replaced when there is no parent to be relative to.
            var local = character.Transform.GetLocalFromWorld(x, y, z);
            var rotation = character.Transform.Parent == null
                ? new System.Numerics.Vector3(0f, 0f, yawRad)
                : character.Transform.Local.Rotation;
            character.SetPosition(local.X, local.Y, local.Z, rotation.X, rotation.Y, rotation.Z);

            // FinalizeTransform re-resolves the zone from the coordinates and can hand the character to
            // another zone. That is wanted for a recall that crosses zones, but never for a move that
            // stays inside the current one: a few metres must not trigger a zone handoff.
            if (!stayInZone)
                character.Transform.FinalizeTransform();
        }

        character.SendPacket(new SCTeleportUnitPacket(reason, 0, x, y, z, yawRad));
        if (TeleportLandingRules.RelaysSameZoneBlink(stayInZone, WorldIntegration.ZoneAuthority))
        {
            WorldIntegration.RelayBlinkToZone?.Invoke(
                character.ObjId, character.ObjId, true, x, y, z);
        }

        if (!loadedInstance)
        {
            // A same-level teleport lands without any client confirmation to hang a repaint on, and
            // the region grid is a kilometre wide: a jump that resolves to the cell the character is
            // already filed under makes AddVisibleObject a no-op, so the destination streamed nothing
            // into a client that had just re-evaluated what it can see. That is what left a player
            // teleported into a courthouse or a jail cell standing in a bare room with no NPCs and no
            // doodads until they relogged. Re-file them and repaint the neighbourhood.
            character.Show();

            // The same no-op leaves the onlookers with the character at the position they were sent
            // before the teleport: drop and re-add it so they get a state packet built from the new
            // transform. Asked for by the trial, where a juror teleported onto a bench has to appear
            // seated there for the rest of the court.
            WorldManager.RepositionVisibleObject(character);

            WorldManager.ResendVisibleObjectsToCharacter(character, clientDroppedVisibility: true);
        }
    }
}
