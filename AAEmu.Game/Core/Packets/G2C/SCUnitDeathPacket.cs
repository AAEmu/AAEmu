using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitDeathPacket(uint objId, KillReason killReason, Unit killer = null)
    : GamePacket(SCOffsets.SCUnitDeathPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write((byte)killReason);
        // ---------------
        stream.Write(15000u); // resurrectionWaitingTime
        stream.Write(0); // lostExp
        stream.Write((byte)0); // deathDurabilityLossRatio
        // ---------------
        stream.WriteBc(killer?.ObjId ?? 0);
        if (killer != null)
        {
            // ---------------
            stream.Write((byte)0); // GameType
            // ---------------
            stream.Write((ushort)0); // killStreak
            stream.Write((byte)0); // param1
            stream.Write((byte)0); // param2
            stream.Write((byte)0); // param3
            stream.Write(killer.Name);

        }

        return stream;
    }
}
