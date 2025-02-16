using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.Shipyard;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class ShipyardManager : Singleton<ShipyardManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Dictionary<uint, ShipyardsTemplate> _shipyardsTemplate;
    private Dictionary<uint, Shipyard> _shipyard;
    private List<uint> _removedShipyards;

    public void Initialize()
    {
        _shipyardsTemplate = new Dictionary<uint, ShipyardsTemplate>();
        _shipyard = new Dictionary<uint, Shipyard>();
        _removedShipyards = new List<uint>();
        Logger.Info("Initialising Shipyard Manager...");
        ShipyardTickStart();
    }

    private static void ShipyardTickStart()
    {
        Logger.Warn("ShipyardUpdateInfoTick: Started");

        var shipyardTickStartTask = new ShipyardTickTask();
        TaskManager.Instance.Schedule(shipyardTickStartTask, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public Shipyard Create(Character owner, ShipyardData shipyardData)
    {
        if (!_shipyardsTemplate.ContainsKey(shipyardData.TemplateId))
            return null;

        var pos = owner.Transform.CloneAsSpawnPosition();
        pos.X = shipyardData.X;
        pos.Y = shipyardData.Y;
        pos.Z = shipyardData.Z;
        pos.Yaw = shipyardData.zRot;

        var objId = ObjectIdManager.Instance.GetNextId();
        var shipId = ShipyardIdManager.Instance.GetNextId();

        var template = _shipyardsTemplate[shipyardData.TemplateId];
        var shipyard = new Shipyard();
        shipyard.TemplateId = shipyardData.TemplateId; // duplicate Id
        shipyard.Id = shipyardData.TemplateId;
        shipyard.ObjId = objId;
        shipyard.Template = template;
        shipyard.Faction = owner.Faction;
        shipyard.Level = 30;
        shipyard.Hp = shipyard.MaxHp;
        shipyard.Name = owner.Name;
        shipyard.ModelId = template.ShipyardSteps[shipyardData.Step].ModelId;
        shipyard.Transform.ApplyWorldSpawnPosition(pos);

        shipyard.ShipyardData = new ShipyardData();
        shipyard.ShipyardData.Id = shipId;
        shipyard.ShipyardData.TemplateId = template.Id;
        shipyard.ShipyardData.X = pos.X;
        shipyard.ShipyardData.Y = pos.Y;
        shipyard.ShipyardData.Z = pos.Z;
        shipyard.ShipyardData.zRot = pos.Yaw;
        shipyard.ShipyardData.MoneyAmount = 0;
        shipyard.ShipyardData.Actions = shipyardData.Step;
        shipyard.ShipyardData.OriginItemId = template.OriginItemId;
        shipyard.ShipyardData.OwnerName = owner.Name;
        shipyard.ShipyardData.OwnerId = owner.Id;
        shipyard.ShipyardData.FactionId = owner.Faction.Id;
        shipyard.ShipyardData.Spawned = DateTime.UtcNow;
        shipyard.ShipyardData.ObjId = objId;
        shipyard.ShipyardData.Hp = template.ShipyardSteps[shipyardData.Step].MaxHp * 100;
        shipyard.ShipyardData.Step = shipyardData.Step;

        // we will make checks for the availability of money and items to create a shipyard
        // and remove from the inventory items and money necessary for the construction of the shipyard
        if (!RemoveRequiredItems(shipyard))
        {
            owner.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return null;
        }

        _shipyard.Add(shipId, shipyard);
        shipyard.Spawn();

        return shipyard;
    }

    private static bool RemoveRequiredItems(Shipyard shipyard)
    {
        var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
        var designId = shipyard.Template.OriginItemId;
        var moneyOwed = TaxationsManager.Instance.taxations[(uint)shipyard.Template.TaxationId].Tax;

        if (!character.Inventory.CheckItems(SlotType.Bag, designId, 1))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            Logger.Error("Not enough item Id={0}", designId);
            return false;
        }

        if (character.Money < moneyOwed)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return false;
        }

        var found = character.Inventory.Bag.GetAllItemsByTemplate(designId, -1, out var foundItems, out _);
        if (!found)
        {
            return false;
        }
        var reagents = SkillManager.Instance.GetSkillReagentsBySkillId(foundItems[0].Template.UseSkillId);
        var skillProducts = SkillManager.Instance.GetSkillProductsBySkillId(foundItems[0].Template.UseSkillId);
        if (reagents != null && skillProducts != null)
        {
            if (reagents.Count > 0)
            {
                // first check only
                var enough = true;
                foreach (var reagent in reagents)
                {
                    if (character.Inventory.CheckItems(SlotType.Bag, reagent.ItemId, reagent.Amount))
                    {
                        continue;
                    }

                    enough = false;
                    Logger.Error("Not enough reagents Id={0}, Amount={1}", reagent.ItemId, reagent.Amount);
                }
                if (!enough)
                {
                    return false;
                }
                foreach (var reagent in reagents)
                {
                    character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, reagent.ItemId, reagent.Amount, null);
                }
            }
            // maybe not needed
            if (skillProducts.Count > 0)
            {
                foreach (var product in skillProducts)
                {
                    character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, product.ItemId, product.Amount);
                }
            }
        }
        else
        {
            Logger.Error("Could not find Reagents/Products for Template[{0}]", foundItems[0].Template.UseSkillId);
            return false;
        }

        character.Inventory.Bag.ConsumeItem(ItemTaskType.Shipyard, designId, 1, null);
        character.SubtractMoney(SlotType.Bag, (int)moneyOwed, ItemTaskType.Shipyard);

        return true;
    }

    public void RemoveShipyard(Shipyard shipyard)
    {
        var shipId = (uint)shipyard.ShipyardData.Id;
        // Remove Shipyard from Shipyard tables
        _removedShipyards.Add(shipId);
        _shipyard.Remove(shipId);
        ShipyardIdManager.Instance.ReleaseId(shipId);
        ObjectIdManager.Instance.ReleaseId(shipyard.ObjId);
        shipyard.Delete();
    }

    public void ShipyardCompleted(Shipyard shipyard)
    {
        var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
        var found = character.Inventory.Bag.GetAllItemsByTemplate(shipyard.Template.ItemId, -1, out var foundItems, out _);
        if (found)
        {
            // calculate skillData
            var skillData = (SkillItem)SkillCaster.GetByType(SkillCasterType.Item);
            skillData.ItemId = foundItems[0].Id;
            SlaveManager.Instance.Create(character, skillData, false, shipyard.Transform);
        }
        RemoveShipyard(shipyard);
    }

    public static void ShipyardCompletedTask(Shipyard shipyard)
    {
        var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
        character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Shipyard, shipyard.Template.ItemId, 1, 0);
        var shipyardCompleteTask = new ShipyardCompleteTask();
        shipyardCompleteTask._shipyard = shipyard;

        shipyard.ShipyardData.Step = 1000; // last step, the ceremony of launching the ship
        character.BroadcastPacket(new SCShipyardStatePacket(shipyard.ShipyardData), true);

        var animTime = shipyard.Template.CeremonyAnimTime;
        TaskManager.Instance.Schedule(shipyardCompleteTask, TimeSpan.FromMilliseconds(animTime));
    }

    public void ShipyardTick()
    {
        foreach (var shipyard in _shipyard)
        {
            UpdateShipyardInfo(shipyard.Value);
        }
    }

    private static void UpdateShipyardInfo(Shipyard shipyard)
    {
        var isDecaying = (DateTime.UtcNow >= shipyard.ShipyardData.Spawned.AddDays(3));

        SetProtectionBuff(shipyard, isDecaying);
        SetDecayBuff(shipyard, isDecaying);
    }

    private static void SetProtectionBuff(Shipyard shipyard, bool isDecay)
    {
        if (!isDecay)
        {
            var duration = shipyard.ShipyardData.Spawned - DateTime.UtcNow;
            var mins = Math.Round(duration.TotalMinutes) * 60000;

            var timeleft = shipyard.Template.TaxDuration + mins;

            if (shipyard.Buffs.CheckBuff((uint)BuffConstants.TaxProtection))
                return;

            var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.TaxProtection);
            if (protectionBuffTemplate != null)
            {
                var casterObj = new SkillCasterUnit(shipyard.ObjId);
                shipyard.Buffs.AddBuff(new Buff(shipyard, shipyard, casterObj, protectionBuffTemplate, null, DateTime.UtcNow), 0, (int)timeleft);
            }
            else
            {
                Logger.Error("Unable to find Protection Buff template");
            }
        }
        else
        {
            if (shipyard.Buffs.CheckBuff((uint)BuffConstants.TaxProtection))
                shipyard.Buffs.RemoveBuff((uint)BuffConstants.TaxProtection);
        }
    }

    private static void SetDecayBuff(Shipyard shipyard, bool isDecay)
    {
        if (isDecay)
        {
            if (shipyard.Buffs.CheckBuff((uint)BuffConstants.Deterioration))
            {
                shipyard.ReduceCurrentHp(shipyard, 7);
                var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
                character.SendPacket(new SCUnitStatePacket(shipyard));
                character.SendPacket(new SCShipyardStatePacket(shipyard.ShipyardData));

                return;
            }

            var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Deterioration);
            if (protectionBuffTemplate != null)
            {
                var casterObj = new SkillCasterUnit(shipyard.ObjId);
                shipyard.Buffs.AddBuff(new Buff(shipyard, shipyard, casterObj, protectionBuffTemplate, null, DateTime.UtcNow));
            }
            else
            {
                Logger.Error("Unable to find Deterioration Debuff template");
            }
        }
        else
        {
            if (shipyard.Buffs.CheckBuff((uint)BuffConstants.Deterioration))
                shipyard.Buffs.RemoveBuff((uint)BuffConstants.Deterioration);
        }
    }

    public void Load()
    {
        Logger.Info("Loading Shipyards...");
        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM shipyards";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ShipyardsTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name"),
                            MainModelId = reader.GetUInt32("main_model_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            CeremonyAnimTime = reader.GetInt32("ceremony_anim_time"),
                            SpawnOffsetFront = reader.GetFloat("spawn_offset_front"),
                            SpawnOffsetZ = reader.GetFloat("spawn_offset_z"),
                            BuildRadius = reader.GetInt32("build_radius"),
                            TaxDuration = reader.GetInt32("tax_duration", 0),
                            OriginItemId = reader.GetUInt32("origin_item_id", 0),
                            TaxationId = reader.GetInt32("taxation_id")
                        };
                        _shipyardsTemplate.Add(template.Id, template);
                    }
                }
            }
            Logger.Info("Loaded {0} shipyards", _shipyardsTemplate.Count);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM shipyard_steps";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ShipyardSteps()
                        {
                            Id = reader.GetUInt32("id"),
                            ShipyardId = reader.GetUInt32("shipyard_id"),
                            Step = reader.GetInt32("step"),
                            ModelId = reader.GetUInt32("model_id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            NumActions = reader.GetInt32("num_actions"),
                            MaxHp = reader.GetInt32("max_hp")
                        };
                        if (_shipyardsTemplate.ContainsKey(template.ShipyardId))
                        {
                            _shipyardsTemplate[template.ShipyardId].ShipyardSteps.Add(template.Step, template);
                        }
                    }
                }
            }
        }
    }
}
