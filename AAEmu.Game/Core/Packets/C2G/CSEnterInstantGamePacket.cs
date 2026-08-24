using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSEnterInstantGamePacket() : GamePacket(CSOffsets.CSEnterInstantGamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (IndunMatchmakingManager.Instance.TryEnter(character))
            return;

        // Battlefield InstantGame enter remains incomplete; Indun path handles H-window matches.
        Logger.Debug("CSEnterInstantGame no active Indun match for {0}", character.Name);
    }
}
