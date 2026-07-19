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
        var laborCost = 0;

        // Verify actual item
        var item = Connection.ActiveChar.Inventory.GetItemById(itemId);
        if (item is null)
        {
            Logger.Warn($"CreateDoodad, {Connection.ActiveChar.Name} provided an invalid ItemId: {itemId}");
            return;
        }

        // Get cost from related skill
        var useSkill = SkillManager.Instance.GetSkillTemplate(item.Template.UseSkillId);
        if (useSkill is not null)
        {
            laborCost = useSkill.ConsumeLaborPower;
        }

        var inPublicFarm = PublicFarmManager.Instance.InPublicFarm(Connection.ActiveChar.ParentWorld.Template, pos);
        var farmType = inPublicFarm
            ? PublicFarmManager.Instance.GetFarmType(Connection.ActiveChar.ParentWorld, pos)
            : FarmType.Invalid;

        // Validate public farm
        if (farmType != FarmType.Invalid)
        {
            if (!PublicFarmManager.Instance.CanPlace(Connection.ActiveChar, farmType, id))
            {
                // Invalid public farm
                Logger.Warn($"CreateDoodad, ItemId: {itemId}, Invalid FarmType: {farmType}");
                return;
            }
            laborCost = 0;
        }

        // Check if labor cost can be skipped by planting on "owned" land (or with permission)
        // Also blocks if not owned
        var house = HousingManager.Instance.GetHouseAtLocation(x, y);
        if (house is not null)
        {
            if (!house.AllowedToInteract(Connection.ActiveChar))
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.NoPerm);
                return;
            }

            laborCost = 0;
        }

        // Check labor cost
        if (Connection.ActiveChar.LaborPower < laborCost)
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.LaborPowerNeeded);
            return;
        }

        // Subtract labor
        if (laborCost != 0)
        {
            Connection.ActiveChar.ChangeLabor((short)-laborCost, 0);
        }

        // Actually place the doodad
        DoodadManager.Instance.CreatePlayerDoodad(Connection.ActiveChar, id, x, y, z, zRot, scale, itemId, farmType);
    }
}
