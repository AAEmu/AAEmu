using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Charges the bound farmhand's labor power or free weekly production cost.</summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSChargeButlerWorldResourcePacket() : GamePacket(CSOffsets.CSChargeButlerWorldResourcePacket, 1)
{
    private const sbyte LaborPowerChargeKind = 0;
    private const sbyte FreeProductionCostChargeKind = 1;
    private const int BodySize = sizeof(sbyte) + sizeof(uint);

    public sbyte ChargeKind { get; private set; }
    public uint Amount { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler resource-charge body, got {stream.LeftBytes} bytes.");

        ChargeKind = stream.ReadSByte();
        Amount = stream.ReadUInt32();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;
        var service = SingletonContainer.ServiceProvider?.GetService<IButlerChargeService>();
        if (service == null)
        {
            SendFailure(character, ErrorMessageType.InternalError);
            return;
        }

        switch (ChargeKind)
        {
            case LaborPowerChargeKind:
            {
                var result = service.ChargeLaborPower(character, Amount);
                if (!result.Success)
                {
                    SendFailure(character, LaborError(result));
                    return;
                }
                break;
            }
            case FreeProductionCostChargeKind when Amount == 0:
            {
                var result = service.ChargeFreeProductionCost(character);
                if (!result.Success)
                {
                    SendFailure(character, ErrorMessageType.InternalError);
                    return;
                }
                break;
            }
            default:
                SendFailure(character, ErrorMessageType.InternalError);
                break;
        }
    }

    private static ErrorMessageType LaborError(ButlerLaborPowerChargeResult result) =>
        result.Failure == ButlerChargeOperationFailure.NotEnoughPlayerLaborPower
            ? ErrorMessageType.NotEnoughLaborPower
            : ErrorMessageType.InternalError;

    private static void SendFailure(Models.Game.Char.Character character, ErrorMessageType error) =>
        character.SendPacket(new SCButlerInfoUpdatedPacket(
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
