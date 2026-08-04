using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Housing;

using NLog;

namespace AAEmu.Game;

/// <summary>
/// </summary>
public static class HousingZoneBridge
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void NotifyZoneHouseCreated(House house)
    {
        if (!WorldIntegration.ZoneAuthority || house == null)
            return;

        try
        {
            var zoneId = house.Transform?.ZoneId ?? 0;
            var unitStateBody = WorldIntegration.BuildWzUnitStateBody(house);
            var houseStateBody = BuildHouseStateBody(house);
            WorldIntegration.RelayHouseStateToZone?.Invoke(zoneId, unitStateBody, houseStateBody);
            NotifyZoneHouseBuildState(house);

            Logger.Debug(
                "WZUnitState/WZHouseState queued for house db={0} obj={1} zoneId={2}",
                house.Id, house.ObjId, zoneId);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to relay house {0} to Zone", house.Id);
        }
    }

    public static void NotifyZoneHouseBuildState(House house)
    {
        if (!WorldIntegration.ZoneAuthority || house == null)
            return;

        var zoneId = house.Transform?.ZoneId ?? 0;
        if (house.CurrentStep >= 0)
        {
            WorldIntegration.RelayHouseBuildProgressToZone?.Invoke(
                zoneId, house.TlId, house.ModelId, house.AllAction, house.CurrentAction);
        }
        else
        {
            WorldIntegration.RelayHouseBuildDoneToZone?.Invoke(zoneId, house.TlId);
        }
    }

    /// <summary>
    /// Removes a World-deleted house from the Zone simulation after its attached doodads have
    /// been torn down. WZUnitRemoved is keyed by the house's unit object ID, while routing must
    /// </summary>
    public static void NotifyZoneHouseRemoved(uint zoneId, uint houseObjId)
    {
        if (!WorldIntegration.ZoneAuthority || zoneId == 0 || houseObjId == 0)
            return;

        WorldIntegration.RelayUnitRemovedToZoneId?.Invoke(zoneId, houseObjId);
        Logger.Debug("WZUnitRemoved queued for house obj={0} zoneId={1}", houseObjId, zoneId);
    }

    /// <summary>
    /// </summary>
    public static byte[] BuildHouseStateBody(House house)
    {
        var stream = new PacketStream();
        var ownerName = NameManager.Instance.GetCharacterName(house.OwnerId) ?? "";
        var sellToName = NameManager.Instance.GetCharacterName(house.SellToPlayerId) ?? "";
        var currentAction = house.CurrentStep == -1 ? house.AllAction : house.CurrentAction;
        var payMoneyAmount = house.Template?.Taxation?.Tax ?? 0;
        var pos = house.Transform.World.Position;

        stream.Write((ushort)(house.TlId != 0 ? house.TlId : (ushort)(house.Id & 0xFFFF)));
        stream.Write(house.Id);
        stream.WriteBc(house.ObjId);
        stream.WritePisc(house.TemplateId, (uint)house.AllAction, (uint)currentAction);
        stream.Write((long)payMoneyAmount);
        stream.Write(0); // ht
        stream.Write((long)house.CoOwnerId);
        stream.Write((long)house.OwnerId);
        stream.Write(ownerName);
        stream.Write((ulong)house.AccountId);
        stream.Write((byte)house.Permission);
        WriteWorldPosition(stream, pos.X, pos.Y, pos.Z);
        stream.Write(house.Name ?? "");
        stream.Write(house.AllowRecover);
        stream.Write((long)house.SellPrice);
        stream.Write(sellToName);
        stream.Write(0); // expandedDecoLimit
        stream.Write(house.SellToPlayerId);
        stream.Write(false); // isPublic
        stream.Write(false); // isBoundButler
        stream.Write(0u);

        for (var i = 0; i < 5; i++)
        {
            stream.Write(0u);
            stream.Write(0L);
            stream.Write(0);
            stream.Write(0);
        }

        // HousingManager::Create does not consume them, so preserve their zero defaults.
        WriteWorldPosition(stream, 0, 0, 0);
        WriteWorldPosition(stream, 0, 0, 0);

        return stream.GetBytes();
    }

    private static void WriteWorldPosition(PacketStream stream, float x, float y, float z)
    {
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
    }
}
