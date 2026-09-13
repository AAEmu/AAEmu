using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Expands the farmhand's garden-job slot count.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte kind
/// </remarks>
public class CSExpandButlerUsableSlotPacket() : GamePacket(CSOffsets.CSExpandButlerUsableSlotPacket, 1)
{
    public sbyte Kind { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != sizeof(sbyte))
            throw new InvalidDataException($"Expected a 1-byte farmhand slot-expansion body, got {stream.LeftBytes} bytes.");
        Kind = stream.ReadSByte();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;
        var service = SingletonContainer.ServiceProvider?.GetService<ButlerFarmingService>();
        if (service == null)
        {
            SendFailure(ErrorMessageType.InternalError);
            return;
        }

        // The 10.0.2.13 garden expansion sender uses kind 2. Specialty expansion is a distinct
        // content path and remains disabled until its server lifecycle is implemented.
        if (Kind != 2)
        {
            SendFailure(ErrorMessageType.InternalError);
            return;
        }

        var result = service.ExpandGardenSlots(character);
        if (!result.Success)
            SendFailure(ButlerPacketErrorMap.From(result.Failure));
    }

    private void SendFailure(ErrorMessageType error) =>
        Connection?.ActiveChar?.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)error,
            0,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>(),
            0,
            0,
            0,
            string.Empty,
            new Dictionary<uint, uint>()));
}
