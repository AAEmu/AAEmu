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
/// Expands a farmhand job slot capacity.
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

        ButlerFarmingOperationFailure failure;
        switch (Kind)
        {
            case 2:
            {
                var result = service.ExpandGardenSlots(character);
                if (result.Success)
                    return;
                failure = result.Failure;
                break;
            }
            case 3:
            {
                var result = service.ExpandSpecialtyTradeSlots(character);
                if (result.Success)
                    return;
                failure = result.Failure;
                break;
            }
            default:
                failure = ButlerFarmingOperationFailure.InvalidContent;
                break;
        }

        SendFailure(ButlerPacketErrorMap.From(failure));
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
