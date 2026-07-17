using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client in response to an authentication request.
/// </summary>
/// <param name="accountId">The unique identifier of the account.</param>
/// <param name="slotCount"></param>
public class ACAuthResponsePacket(ulong accountId, byte slotCount) : LoginPacket(LCOffsets.ACAuthResponsePacket)
{
    private readonly string _wsk = "65CCBF5AF8DB8B633D3C03C5A8735601";

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(accountId);
        stream.Write(_wsk, true);
        stream.Write(slotCount);

        return stream;
    }
}
