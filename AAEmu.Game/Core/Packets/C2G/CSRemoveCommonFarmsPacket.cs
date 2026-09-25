using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The bulk clear request (CS 0x163): the client asks the server to drop every farm doodad this
/// character has planted. The body is empty.
/// </summary>
/// <remarks>
/// The client serializer for this type emits no body; the request is a bare "clear my farm".
/// </remarks>
public class CSRemoveCommonFarmsPacket() : GamePacket(CSOffsets.CSRemoveCommonFarmsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
        {
            Logger.Warn("RemoveCommonFarms ignored: no active character.");
            return;
        }

        var removed = PublicFarmManager.Instance.RemoveCharacterFarms(character);
        Logger.Debug("Removed {0} farm doodads for {1}.", removed, character.Name);
    }
}
