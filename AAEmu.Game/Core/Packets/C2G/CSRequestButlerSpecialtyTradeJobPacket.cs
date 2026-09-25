using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Packets.C2G;

public sealed class CSRequestButlerSpecialtyTradeJobPacket()
    : GamePacket(CSOffsets.CSRequestButlerSpecialtyTradeJobPacket, 1)
{
    private const int BodySize = sizeof(sbyte) + sizeof(long) + sizeof(int) + sizeof(short);

    public sbyte JobKind { get; private set; }
    public long DbSpecialtyTradeId { get; private set; }
    public int SpecialtyTradeType { get; private set; }
    public short ToZoneGroupType { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler specialty-job body, got {stream.LeftBytes} bytes.");

        JobKind = stream.ReadSByte();
        DbSpecialtyTradeId = stream.ReadInt64();
        SpecialtyTradeType = stream.ReadInt32();
        ToZoneGroupType = stream.ReadInt16();

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
        switch (Classify(JobKind, DbSpecialtyTradeId, SpecialtyTradeType, ToZoneGroupType))
        {
            case ButlerSpecialtyTradeRequestOperation.Register:
            {
                var result = service.RegisterSpecialtyTrade(character, checked((uint)SpecialtyTradeType),
                    ToZoneGroupType);
                if (result.Success)
                    return;
                failure = result.Failure;
                break;
            }
            case ButlerSpecialtyTradeRequestOperation.Cancel:
            {
                var result = service.CancelSpecialtyTrade(character, DbSpecialtyTradeId);
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

    internal static ButlerSpecialtyTradeRequestOperation Classify(
        sbyte jobKind, long dbSpecialtyTradeId, int specialtyTradeType, short toZoneGroupType) => jobKind switch
    {
        1 when dbSpecialtyTradeId == 0 && specialtyTradeType > 0 && specialtyTradeType <= int.MaxValue &&
                 toZoneGroupType > 0 => ButlerSpecialtyTradeRequestOperation.Register,
        3 when dbSpecialtyTradeId > 0 => ButlerSpecialtyTradeRequestOperation.Cancel,
        _ => ButlerSpecialtyTradeRequestOperation.Invalid
    };

    private void SendFailure(ErrorMessageType error) =>
        Connection?.ActiveChar?.SendPacket(new SCButlerSpecialtyTradeUpdatedPacket(
            unchecked((byte)JobKind),
            (short)error,
            DbSpecialtyTradeId,
            new ButlerSpecialtyTradeDataWire(0, 0, 0, 0)));
}

internal enum ButlerSpecialtyTradeRequestOperation
{
    Invalid,
    Register,
    Cancel
}
