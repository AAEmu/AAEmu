using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// H-window channel/index query. Client expects <see cref="SCSysIndunIndexPacket"/> in reply.
/// </summary>
/// <remarks>
/// Wire: u16 type, s32 zoneId (zone key), u32 instId (= instances.id).
/// </remarks>
public class CSRequestSysInstanceIndexPacket() : GamePacket(CSOffsets.CSRequestSysInstanceIndexPacket, 1)
{
    public short TypeValue { get; private set; }
    public uint ZoneKey { get; private set; }
    public uint CatalogInstId { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt16();
        ZoneKey = (uint)stream.ReadInt32();
        CatalogInstId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var dungeonZone = CatalogInstId != 0
            ? IndunGameData.Instance.GetDungeonZoneByCatalogId(CatalogInstId)
            : null;
        if (dungeonZone == null && ZoneKey != 0)
        {
            var zone = ZoneManager.Instance.GetZoneByKey(ZoneKey);
            if (zone != null)
                dungeonZone = IndunGameData.Instance.GetDungeonZone(zone.GroupId);
        }

        if (dungeonZone == null)
        {
            Logger.Warn(
                "CSRequestSysInstanceIndex: unknown catalogInstId={0} zoneKey={1} type={2}",
                CatalogInstId, ZoneKey, TypeValue);
            character.SendPacket(new SCSysIndunIndexPacket(ZoneKey, 0, 0));
            return;
        }

        var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        var reply = SysIndunIndexResolver.Resolve(
            ZoneKey,
            CatalogInstId,
            dungeonZone,
            zoneKeys,
            WorldManager.Instance.GetWorlds());

        Logger.Debug(
            "CSRequestSysInstanceIndex char={0} catalog={1} zoneKey={2} -> instanceId={3} index={4}",
            character.Name, CatalogInstId, reply.ZoneKey, reply.InstanceId, reply.InstanceIndex);

        character.SendPacket(new SCSysIndunIndexPacket(reply.ZoneKey, reply.InstanceId, reply.InstanceIndex));
    }
}
