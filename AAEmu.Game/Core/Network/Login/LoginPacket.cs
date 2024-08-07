using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Login;

public abstract class LoginPacket : PacketBase<LoginConnection>
{
    protected LoginPacket(ushort typeId) : base(typeId)
    {
    }

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