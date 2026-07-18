using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateDoodadPacket() : GamePacket(CSOffsets.CSCreateDoodadPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32();
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        var zRot = stream.ReadSingle();
        var scale = stream.ReadSingle();
        var itemId = stream.ReadUInt64();

        Logger.Warn($"CreateDoodad, Id: {id}, X: {x}, Y: {y}, Z: {z}, zRot: {zRot}  ItemId: {itemId}");

        var pos = new Vector3(x, y, z);
        var inPublicFarm = PublicFarmManager.Instance.InPublicFarm(Connection.ActiveChar.ParentWorld.Template, pos);
        var farmType = inPublicFarm
            ? PublicFarmManager.Instance.GetFarmType(Connection.ActiveChar.ParentWorld, pos)
            : FarmType.Invalid;
        if (farmType != FarmType.Invalid)
        {
            if (!PublicFarmManager.Instance.CanPlace(Connection.ActiveChar, farmType, id))
            {
                // Invalid public farm
                Logger.Warn($"CreateDoodad, ItemId: {itemId}, FarmType: {farmType}");
                return;
            }

            Logger.Warn($"CreateDoodad, ItemId: {itemId}, FarmType: {farmType}");
        }

        DoodadManager.Instance.CreatePlayerDoodad(Connection.ActiveChar, id, x, y, z, zRot, scale, itemId, farmType);
    }
}
