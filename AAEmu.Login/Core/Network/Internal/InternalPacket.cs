using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Network.Internal;

public abstract class InternalPacket(ushort typeId) : PacketBase<InternalConnection>(typeId)
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

    public override PacketBase<InternalConnection> Decode(PacketStream ps)
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
    
    /// <summary>
    /// This is called after <see cref="Decode"/>.
    /// The purpose is to separate packet data from packet behavior.
    /// </summary>
    public virtual void Execute() { }
}
