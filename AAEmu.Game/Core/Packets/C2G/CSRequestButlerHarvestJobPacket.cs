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
/// Registers or cancels a farmhand harvest job.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSRequestButlerHarvestJobPacket() : GamePacket(CSOffsets.CSRequestButlerHarvestJobPacket, 1)
{
    private const int BodySize = sizeof(sbyte) + sizeof(long) + sizeof(int) + sizeof(short);

    public sbyte JobKind { get; private set; }
    public long DbHarvestId { get; private set; }
    public int HarvestId { get; private set; }
    public short Amount { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler harvest-job body, got {stream.LeftBytes} bytes.");

        JobKind = stream.ReadSByte();
        DbHarvestId = stream.ReadInt64();
        HarvestId = stream.ReadInt32();
        Amount = stream.ReadInt16();

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
        switch (Classify(JobKind, DbHarvestId, HarvestId, Amount))
        {
            // 10.0.2.13 FUN_391899A0 sends kind 1, database id 0, static harvest id, amount.
            case ButlerHarvestRequestOperation.Register:
            {
                var result = service.RegisterHarvest(character, (uint)HarvestId, Amount);
                if (result.Success)
                    return;
                failure = result.Failure;
                break;
            }
            // The unregister sender uses kind 3 with the existing database job id. Its harvest-id
            // tail is a client global whose value has not been established, so ownership of the
            // database id is the authoritative cancellation check.
            case ButlerHarvestRequestOperation.Cancel:
            {
                var result = service.CancelHarvest(character, DbHarvestId);
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

    internal static ButlerHarvestRequestOperation Classify(
        sbyte jobKind, long dbHarvestId, int harvestId, short amount) => jobKind switch
    {
        1 when dbHarvestId == 0 && harvestId > 0 && amount > 0 => ButlerHarvestRequestOperation.Register,
        3 when dbHarvestId > 0 => ButlerHarvestRequestOperation.Cancel,
        _ => ButlerHarvestRequestOperation.Invalid
    };

    private void SendFailure(ErrorMessageType error) =>
        Connection?.ActiveChar?.SendPacket(new SCButlerHarvestUpdatedPacket(
            unchecked((byte)JobKind),
            (short)error,
            DbHarvestId,
            new ButlerHarvestDataWire(0, 0, 0, 0, 0)));
}

internal enum ButlerHarvestRequestOperation
{
    Invalid,
    Register,
    Cancel
}

internal static class ButlerPacketErrorMap
{
    public static ErrorMessageType From(ButlerFarmingOperationFailure failure) => failure switch
    {
        ButlerFarmingOperationFailure.UnsupportedHarvestGrade => ErrorMessageType.ButlerHarvestGradeInsufficient,
        ButlerFarmingOperationFailure.NotEnoughGardenSize => ErrorMessageType.ButlerGardenSizeInsufficient,
        ButlerFarmingOperationFailure.NotEnoughProductionCost => ErrorMessageType.ButlerProductionCostInsufficient,
        ButlerFarmingOperationFailure.NotEnoughLaborPower => ErrorMessageType.NotEnoughLaborPower,
        _ => ErrorMessageType.InternalError
    };
}
