using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Tasks.Skills;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterCraft
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private int _count { get; set; }
    private Craft _craft { get; set; }
    private uint _doodadId { get; set; }
    private int _consumeLaborPower { get; set; }

    public Character Owner { get; set; }
    public bool IsCrafting { get; set; }

    public CharacterCraft(Character owner) => Owner = owner;

    public void Craft(Craft craft, int count, uint doodadId)
    {
        _craft = craft;
        _count = count;
        _doodadId = doodadId;

        // Check if we have enough materials
        var hasMaterials = craft.CraftMaterials.Any(craftMaterial => Owner.Inventory.GetItemsCount(craftMaterial.ItemId) < craftMaterial.Amount);

        // Check if we have permission to actually use the doodad (mostly sanity check since the client already checks this before you can craft)
        var hasPermission = true;
        var doodad = WorldManager.Instance.GetDoodad(doodadId);
        if ((doodad != null) && (doodad.FuncPermission != DoodadFuncPermission.Any && (Owner != null)))
        {
            switch (doodad.FuncPermission)
            {
                case DoodadFuncPermission.Any:
                case DoodadFuncPermission.Permission1:
                case DoodadFuncPermission.Permission2:
                case DoodadFuncPermission.OwnerOnly:
                case DoodadFuncPermission.Permission4:
                case DoodadFuncPermission.OwnerRaidMembers:
                    break;
                case DoodadFuncPermission.SameAccount:
                    if (doodad.OwnerType == DoodadOwnerType.Character)
                        hasPermission = WorldManager.Instance.GetCharacterById(doodad.OwnerId).AccountId == Owner.AccountId;
                    break;
                case DoodadFuncPermission.ZoneResidents:
                    hasPermission = false;
                    var zoneGroup = ZoneManager.Instance.GetZoneByKey(doodad.Transform.ZoneId)?.GroupId ?? 0;
                    var playerHouses = new Dictionary<uint, House>();
                    if (HousingManager.Instance.GetByAccountId(playerHouses, Owner.AccountId) > 0)
                    {
                        foreach (var (houseId, playerHouse) in playerHouses)
                        {
                            var houseZoneGroup = ZoneManager.Instance.GetZoneByKey(playerHouse.Transform.ZoneId)?.GroupId ?? 0;
                            if (houseZoneGroup == zoneGroup)
                            {
                                hasPermission = true;
                                break;
                            }
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Owner.SendMessage($"Crafting using @DOODAD_NAME({doodad.TemplateId}) - {doodad.TemplateId} (objId: {doodad.ObjId}) with current permission {doodad.FuncPermission} = {hasPermission}");
        }

        if (hasMaterials && hasPermission)
        {
            IsCrafting = true;

            var caster = SkillCaster.GetByType(SkillCasterType.Unit);
            caster.ObjId = Owner.ObjId;

            var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
            target.ObjId = doodadId;

            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(craft.SkillId));
            _consumeLaborPower = skill.Template.ConsumeLaborPower;
            skill.Use(Owner, caster, target);
        }
    }

    public void EndCraft()
    {
        _count--;
        IsCrafting = false;

        if (_craft == null)
        {
            CancelCraft();
            return;
        }

        if (Owner.LaborPower < _consumeLaborPower)
        {
            Owner.SendMessage("|cFFFFFF00[Craft] Not enough Labor Powers for crafting! Performing a fictitious crafting step...|r");
            CraftOrCancel();
            return;
        }

        if (Owner.Inventory.FreeSlotCount(SlotType.Inventory) < _craft.CraftProducts.Count)
        {
            CraftOrCancel();
            return;
        }

        foreach (var product in _craft.CraftProducts)
        {
            // Check if we're crafting a tradepack, if so, try to remove currently equipped backpack slot
            if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId) == false)
            {
                Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CraftActSaved, product.ItemId, product.Amount, -1, Owner.Id);
            }
            else
            {
                // положим на землю, если крафтим более одного пака
                // put it on the ground if we craft more than one pack
                var backpack = Owner.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
                if (backpack != null)
                {
                    PutDownBackpack(backpack);
                }
                if (!Owner.Inventory.TryEquipNewBackPack(ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount, -1, Owner.Id))
                {
                    CancelCraft();
                    return;
                }
            }
        }

        foreach (var material in _craft.CraftMaterials)
        {
            Owner.Inventory.Bag.ConsumeItem(ItemTaskType.CraftActSaved, material.ItemId, material.Amount, null);
        }

        //Owner.Quests.OnCraft(_craft); // TODO added for quest Id=6024
        // инициируем событие
        //Task.Run(() =>
        //{
        //    if (_craft != null)
        //    {
        //        QuestManager.Instance.DoOnCraftEvents(Owner, _craft.Id);
        //    }
        //});
        QuestManager.Instance.DoOnCraftEvents(Owner, _craft.Id);

        if (_count > 0)
        {
            ScheduleCrtaft();
            // Owner.SendMessage($"Continue craft: {_craft.Id} for {_count} more times TaskId: {newCraft.Id}, cooldown: {nextCraftDelay.TotalMilliseconds}ms");
        }
        else
        {
            CancelCraft();
        }
    }

    private void CraftOrCancel()
    {
        if (_count > 0)
        {
            ScheduleCrtaft();
        }
        else
            CancelCraft();
    }

    private void PutDownBackpack(Item backpackItem)
    {
        // Drop Backpack to the floor (spawn doodad)
        var putDownSkill = SkillManager.Instance.GetSkillTemplate(backpackItem.Template.UseSkillId);
        foreach (var skillEffect in putDownSkill.Effects)
        {
            if (skillEffect.Template is not PutDownBackpackEffect putDownBackpackEffectTemplate)
                continue;

            var newDoodadId = putDownBackpackEffectTemplate.BackpackDoodadId;

            if (PublicFarmManager.Instance.InPublicFarm(Owner.Transform.WorldId, Owner.Transform.World.Position))
            {
                Owner.SendErrorMessage(ErrorMessageType.CommonFarmNotAllowedType);
                return;
            }
            var previousGlider = Owner.Inventory.Bag.GetItemByItemId(Owner.Inventory.PreviousBackPackItemId);
            // If no longer valid, reset the value here
            if ((previousGlider == null) || (previousGlider.SlotType != SlotType.Inventory))
                Owner.Inventory.PreviousBackPackItemId = 0;

            using var pos = Owner.Transform.CloneDetached();
            pos.Local.SetRotation(0f, 0f, 0f); // Always faces north when placed
            pos.Local.Translate((Random.Shared.NextSingle() * 2f) - 1f, (Random.Shared.NextSingle() * 2f) - 1f, 0);

            var targetHouse = HousingManager.Instance.GetHouseAtLocation(pos.World.Position.X, pos.World.Position.Y);
            if (targetHouse != null)
            {
                // Trying to put on a house location, we need to do some checks
                if (!targetHouse.AllowedToInteract(Owner))
                {
                    Owner.SendErrorMessage(ErrorMessageType.Backpack);
                    return;
                }
            }

            if (Owner.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DropBackpack, backpackItem))
            {
                // Create the Doodad at location on the floor if it's close to it
                var doodad = DoodadManager.Instance.Create(0, newDoodadId, Owner, true);
                if (doodad == null)
                {
                    Logger.Warn("Doodad {0}, from BackpackDoodadId could not be created", newDoodadId);
                    return;
                }
                doodad.IsPersistent = true;
                doodad.Transform = pos.CloneDetached(doodad);
                doodad.AttachPoint = AttachPointKind.None;
                doodad.ItemId = backpackItem.Id;
                doodad.ItemTemplateId = backpackItem.Template.Id;
                doodad.UccId = backpackItem.UccId; // Not sure if it's needed, but let's copy the Ucc for completeness' sake
                doodad.SetScale(1f);
                doodad.PlantTime = DateTime.UtcNow;
                if (targetHouse != null)
                {
                    doodad.OwnerDbId = targetHouse.Id;
                    doodad.OwnerType = DoodadOwnerType.Housing;
                    doodad.ParentObj = targetHouse;
                    doodad.ParentObjId = targetHouse.ObjId;
                    doodad.Transform.Parent = targetHouse.Transform; // Does not work as intended yet
                }

                doodad.InitDoodad();
                doodad.Spawn();
                doodad.Save();

                Owner.BroadcastPacket(new SCUnitEquipmentsChangedPacket(Owner.ObjId, (byte)EquipmentItemSlot.Backpack, null), false);
                if ((previousGlider != null) && Owner.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack) == null)
                    Owner.Inventory.SplitOrMoveItem(ItemTaskType.SwapItems, previousGlider.Id, previousGlider.SlotType, (byte)previousGlider.Slot, 0, SlotType.Equipment, (int)EquipmentItemSlot.Backpack);
            }
        }
    }

    private void ScheduleCrtaft()
    {
        var newCraft = new CraftTask(Owner, _craft.Id, _doodadId, _count);
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(_craft.SkillId);
        var timeToGlobalCooldown = Owner.GlobalCooldown - DateTime.UtcNow;
        var nextCraftDelay = timeToGlobalCooldown.TotalMilliseconds > skillTemplate.CooldownTime
            ? timeToGlobalCooldown
            : TimeSpan.FromMilliseconds(skillTemplate.CooldownTime);
        TaskManager.Instance.Schedule(newCraft, nextCraftDelay);
    }

    public void CancelCraft()
    {
        IsCrafting = false;
        _craft = null;
        _count = 0;
        _doodadId = 0;

        // Also cancel the related skill ? I don't think this really does anything for crafts, but can't hurt I guess
        if (Owner != null)
        {
            if (Owner.SkillTask != null)
                Owner.SkillTask.Skill.Cancelled = true;
            Owner.InterruptSkills();
        }

        // Might want to send a packet here, I think there is a packet when crafting fails. Not sure yet.
    }
}
