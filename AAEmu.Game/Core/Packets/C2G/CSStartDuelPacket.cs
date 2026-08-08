using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartDuelPacket() : GamePacket(CSOffsets.CSStartDuelPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Client layout (VA 0x39C772B0): challenger id u64, error i16, duelType u8. Reading the id as
        // u32 meant a decline looked up the wrong key, threw, and left the duel entry behind - which is
        // how both players stayed "already in a duel" until a restart.
        var challengerId = (uint)stream.ReadUInt64(); // u64 type - who challenged us
        var errorMessage = stream.ReadInt16();        // i16 - 0 accepted, 507 refused
        _ = stream.ReadByte();                        // u8  duelType

        Logger.Warn("StartDuel, Id: {0}, ErrorMessage: {1}", challengerId, errorMessage);

        if (errorMessage != 0)
        {
            DuelManager.Instance.DuelCancel(challengerId, (ErrorMessageType)errorMessage);
            return;
        }

        DuelManager.Instance.DuelAccepted(Connection.ActiveChar, challengerId);
    }
}
