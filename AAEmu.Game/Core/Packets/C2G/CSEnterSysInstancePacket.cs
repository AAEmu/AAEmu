using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// H-window / instance-list enter (greater dungeons, etc.). Body: instId = instances.id, bc = entrance doodad (optional).
/// Distinct from portal F-key <see cref="Models.Game.DoodadObj.Funcs.DoodadFuncEnterInstance"/>.
/// </summary>
public class CSEnterSysInstancePacket() : GamePacket(CSOffsets.CSEnterSysInstancePacket, 1)
{
    public uint InstId { get; private set; }
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        InstId = stream.ReadUInt32();
        Bc = stream.ReadBc();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var dungeonZone = IndunGameData.Instance.GetDungeonZoneByCatalogId(InstId);
        if (dungeonZone == null)
        {
            Logger.Warn("CSEnterSysInstance: no IndunZone for instances.id={0} (bc={1})", InstId, Bc);
            return;
        }

        var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        if (zoneKeys == null || zoneKeys.Count == 0)
        {
            Logger.Warn(
                "CSEnterSysInstance: zone group {0} has no zone keys (instances.id={1})",
                dungeonZone.ZoneGroupId, InstId);
            return;
        }

        // RequestDungeonInstance expects a zones.id (same as doodad enter funcs).
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKeys[0]);
        if (zone == null)
        {
            Logger.Warn("CSEnterSysInstance: missing zone for key {0}", zoneKeys[0]);
            return;
        }

        Logger.Info(
            "CSEnterSysInstance char={0} instances.id={1} zoneGroup={2} zoneId={3} bc={4}",
            character.Name, InstId, dungeonZone.ZoneGroupId, zone.Id, Bc);

        character.SendPacket(new SCProcessingInstancePacket((int)zone.ZoneKey));
        IndunManager.Instance.RequestDungeonInstance(character, zone.Id, 0);
    }
}
