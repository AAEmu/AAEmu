using System.Buffers;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Network.Login;

public abstract class LoginPacket(ushort typeId) : PacketBase<ILoginConnection>(typeId)
{
    public void EncodeTo(IBufferWriter<byte> bufferWriter)
    {
        var packetStream = Encode();
        bufferWriter.Write(packetStream.Buffer.AsSpan(0, packetStream.Count));
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

    public override PacketBase<ILoginConnection> Decode(PacketStream ps)
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
