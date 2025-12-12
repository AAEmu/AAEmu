using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Login;

public abstract class LoginPacket(ushort typeId) : PacketBase<LoginConnection>(typeId)
{
    public override PacketStream Encode()
    {
        var ps = new PacketStream();
        try
        {
            ps.Write(new PacketStream().Write(TypeId).Write(this));
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }

        return ps;
    }

    public override PacketBase<LoginConnection> Decode(PacketStream ps)
    {
        try
        {
            Read(ps);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }

        return this;
    }
}