using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to enter the game world.
/// </summary>
public class CAEnterWorldPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAEnterWorldPacket;

    /// <summary>
    /// Gets a set of flags associated with the entry request.
    /// </summary>
    public ulong Flag { get; private set; }

    /// <summary>
    /// Gets the identifier of the game server the client wishes to connect to.
    /// </summary>
    public GameServerId GsId { get; private set; }

    public override void Read(PacketStream stream)
    {
        Flag = stream.ReadUInt64();
        GsId = new GameServerId(stream.ReadByte());
    }
}
