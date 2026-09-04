using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Direct teleport / matching enter for an indun zone key (often from the H-window path when
/// <c>instances.direct_matching</c> is set). Body: indunZoneKey = zone_group_id, type = unused catalog hint.
/// </summary>
public class CSIndunDirectTelPacket() : GamePacket(CSOffsets.CSIndunDirectTelPacket, 1)
{
    public uint IndunZoneKey { get; private set; }
    public ulong TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        IndunZoneKey = stream.ReadUInt32();
        TypeValue = stream.ReadUInt64();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(IndunZoneKey);
        if (dungeonZone == null)
        {
            Logger.Warn("CSIndunDirectTel: no IndunZone for zone_group={0}", IndunZoneKey);
            return;
        }

        var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        if (zoneKeys == null || zoneKeys.Count == 0)
        {
            Logger.Warn("CSIndunDirectTel: no zone keys for group {0}", dungeonZone.ZoneGroupId);
            return;
        }

        var zone = ZoneManager.Instance.GetZoneByKey(zoneKeys[0]);
        if (zone == null)
            return;

        Logger.Info(
            "CSIndunDirectTel char={0} zoneGroup={1} zoneId={2} type={3}",
            character.Name, IndunZoneKey, zone.Id, TypeValue);

        character.SendPacket(new SCProcessingInstancePacket((int)zone.ZoneKey));
        IndunManager.Instance.RequestDungeonInstance(character, zone.Id, 0);
    }
}
