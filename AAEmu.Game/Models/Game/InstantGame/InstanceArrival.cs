using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// Neighbourhood restream after an instance load, and the unlock that lets movement land again.
/// A dungeon copy streams during the load but keeps movement locked until the re-entry check:
/// a client that already cached the level can start falling through a floor that has no collision
/// yet, and those moves must not land. The snap puts that client back on the spawn the server
/// never left.
/// </summary>
public static class InstanceArrival
{
    public static void StreamNeighbourhood(Character character)
    {
        if (character == null)
            return;

        character.Transform.FinalizeTransform();
        character.Show();
        WorldManager.ResendVisibleObjectsToCharacter(character, clientDroppedVisibility: true);
    }

    public static void Complete(Character character, bool snapClient)
    {
        if (character == null)
            return;

        character.DisabledSetPosition = false;
        StreamNeighbourhood(character);

        if (!snapClient)
            return;

        var pos = character.Transform.World.Position;
        character.SendPacket(new SCBlinkUnitPacket(character.ObjId, 0f, 0f, true, pos.X, pos.Y, pos.Z));
        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayBlinkToZone?.Invoke(
                character.ObjId, character.ObjId, true, pos.X, pos.Y, pos.Z);
        }
    }
}
