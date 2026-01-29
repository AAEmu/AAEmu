using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAOtpNumberPacket"/> which is sent by the client to provide the OTP (One-Time Password)
/// number for two-factor authentication.
/// </summary>
public class CAOtpNumberPacketHandler : ILoginPacketHandler<CAOtpNumberPacket>
{
    public Task Execute(CAOtpNumberPacket packet, ILoginConnection connection, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
