using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to request the list of available game worlds.
/// </summary>
public class CAListWorldPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAListWorldPacket;
    
    public ulong Flag { get; private set; }
    
    public override void Read(PacketStream stream)
    {
        Flag = stream.ReadUInt64();
    }
}
