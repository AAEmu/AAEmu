using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Requests purchasing/upgrading one prestige-shop buff to a specific grade. Wire format confirmed
/// against the client dump (both construction sites, console `FUN_396be580` and UI `FUN_399857c0`):
/// the packet carries three int32 fields, set via `FUN_39c4f500` at offsets +0x10/+0x14/+0x18 as
/// [expeditionId][buffType][grade] - field 1 is `DAT_3b4f531c`, which is literally the client's
/// `MyExpeditionId` cache slot (`DAT_3b4f4fb0 + 0x36c`). The old code passed field 1 (the expedition
/// id) into TryPurchaseBuffGrade as the buff id, so every purchase failed the
/// ExpeditionBuffGameData.GetGrade lookup with ErrorMessageType.Invalid.
/// 2026-08-27: was fully parsed but never wired to anything, and never even registered in
/// GameNetwork's dispatch table - same bug class as CSExpeditionLevelUpPacket.
/// </summary>
public class CSExpeditionBuffGradePacket() : GamePacket(CSOffsets.CSExpeditionBuffGradePacket, 1)
{
    public int ExpeditionId { get; private set; }
    public int BuffId { get; private set; }
    public uint Grade { get; private set; }

    public override void Read(PacketStream stream)
    {
        ExpeditionId = stream.ReadInt32();
        BuffId = stream.ReadInt32();
        Grade = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character?.Expedition == null || BuffId <= 0 || Grade is 0 or > byte.MaxValue)
        {
            Logger.Warn(
                "ExpeditionBuffGrade: rejected before dispatch - expeditionId={0}, buffId={1}, grade={2}, character={3}, hasExpedition={4}",
                ExpeditionId, BuffId, Grade, character?.Name ?? "<none>", character?.Expedition != null);
            return;
        }

        if (ExpeditionId != 0 && ExpeditionId != (int)character.Expedition.Id)
            Logger.Warn(
                "ExpeditionBuffGrade: client sent expeditionId {0} but character {1} is in expedition {2} - using server-side truth",
                ExpeditionId, character.Name, character.Expedition.Id);

        ExpeditionManager.Instance.TryPurchaseBuffGrade(character, (uint)BuffId, (byte)Grade);
    }
}
