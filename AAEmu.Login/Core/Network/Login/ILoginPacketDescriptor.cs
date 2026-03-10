using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Network.Login;

public interface ILoginPacketDescriptor
{
    ushort TypeId { get; }

    /// <summary>
    /// Reads the packet from the stream and dispatches it to the appropriate handler.
    /// </summary>
    /// <param name="stream">The stream containing the packet data.</param>
    /// <param name="session">The session where the packet was received.</param>
    /// <param name="cancellationToken">A token that can be used to observe cancellation requests.</param>
    Task Dispatch(PacketStream stream, ILoginSession session, CancellationToken cancellationToken);
}
