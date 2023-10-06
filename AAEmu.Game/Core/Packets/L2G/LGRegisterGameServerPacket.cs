using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.L2G;

public class LGRegisterGameServerPacket : LoginPacket
{
    public LGRegisterGameServerPacket() : base(LGOffsets.LGRegisterGameServerPacket)
    {
    }

    public override void Read(PacketStream stream)
    {
        var result = stream.ReadByte();
        if (result != 0)
        {
            Logger.Error("Error registering on LoginServer");
            Connection.Close(); // TODO or shutdown?
        }
        else
        {
            Logger.Info("Successfully registered on LoginServer");
        }
    }
}
