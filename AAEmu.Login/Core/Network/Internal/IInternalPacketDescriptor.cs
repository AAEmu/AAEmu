using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Network.Internal;

public interface IInternalPacketDescriptor
{
    ushort TypeId { get; }
    
    /// <summary>
    /// Reads the packet from the stream and dispatches it to the appropriate handler.
    /// </summary>
    /// <param name="stream">The stream containing the packet data.</param>
    /// <param name="connection">The connection where the packet was received.</param>
    void Dispatch(PacketStream stream, InternalConnection connection);
}
