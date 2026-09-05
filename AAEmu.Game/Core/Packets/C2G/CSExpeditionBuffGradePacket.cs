using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Requests purchasing/upgrading one prestige-shop buff to a specific grade. Wire: expeditionId,
/// buffId, grade (all int32/uint32) - the old code mixed up expeditionId and buffId, so every
/// purchase failed the grade lookup.
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
