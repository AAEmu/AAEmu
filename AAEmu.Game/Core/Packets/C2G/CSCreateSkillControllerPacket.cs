using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateSkillControllerPacket() : GamePacket(CSOffsets.CSCreateSkillControllerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        var fallDamageImmune = stream.ReadBoolean();

        SkillControllerPacketDebug.LogCsCreateSkillController(objId, scType, fallDamageImmune);
    }
}
