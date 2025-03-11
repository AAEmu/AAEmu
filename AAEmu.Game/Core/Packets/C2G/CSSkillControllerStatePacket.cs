using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSkillControllerStatePacket : GamePacket
{
    public CSSkillControllerStatePacket() : base(CSOffsets.CSSkillControllerStatePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        if (scType == 0)
        {
            var len = stream.ReadSingle();
            var teared = stream.ReadBoolean();
            var cutouted = stream.ReadBoolean();
        }

        Logger.Warn("SkillControllerState");
    }
}
