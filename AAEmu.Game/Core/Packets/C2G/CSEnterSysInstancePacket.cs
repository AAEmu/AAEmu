using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Indun;

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
            character.SendErrorMessage(ErrorMessageType.InvalidStateInstance);
            return;
        }

        var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        if (zoneKeys == null || zoneKeys.Count == 0)
        {
            Logger.Warn(
                "CSEnterSysInstance: zone group {0} has no zone keys (instances.id={1})",
                dungeonZone.ZoneGroupId, InstId);
            character.SendErrorMessage(ErrorMessageType.InvalidStateInstance);
            return;
        }

        // RequestDungeonInstance expects a zones.id (same as doodad enter funcs).
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKeys[0]);
        if (zone == null)
        {
            Logger.Warn("CSEnterSysInstance: missing zone for key {0}", zoneKeys[0]);
            character.SendErrorMessage(ErrorMessageType.InvalidStateInstance);
            return;
        }

        // The request names the instance but not the dimension: the row the player picked in the channel list
        // (resolved by CS 0x199) is what decides which copy they land in. It only counts for the instance it was
        // listed for, and only while it names a copy — otherwise a stale pick for another instance, or one that
        // resolved to no copy at all, would decide this entry.
        var remembered = IndunManager.Instance.GetInstancePick(character.Id);
        var pick = remembered is { WorldId: not 0 } candidate && candidate.ZoneKey == zone.ZoneKey
            ? candidate
            : (SysIndunPick?)null;
        var channel = pick?.ChannelId ?? 0;

        Logger.Info(
            "CSEnterSysInstance char={0} instances.id={1} zoneGroup={2} zoneId={3} bc={4} channel={5} copy={6}",
            character.Name, InstId, dungeonZone.ZoneGroupId, zone.Id, Bc, channel,
            pick?.WorldId.ToString() ?? "none");

        // The entry is the only consumer of a pick, so it does not outlive this request.
        IndunManager.Instance.ClearInstancePick(character.Id);

        character.SendPacket(new SCProcessingInstancePacket((int)zone.ZoneKey));
        IndunManager.Instance.RequestDungeonInstance(character, zone.Id, (uint)channel, pick?.WorldId);
    }
}
