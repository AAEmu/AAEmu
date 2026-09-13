using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Effects;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>Forced physical backpack placement, independent of voluntary skill/land permissions.</summary>
internal class CharacterBackpackDrop(
    Character owner,
    ISkillManager skillManager,
    IDoodadManager doodadManager,
    INonUnitObjectIdManager objectIdManager,
    IMailManager mailManager)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public CharacterBackpackDrop(Character owner) : this(owner, SkillManager.Instance, DoodadManager.Instance,
        NonUnitObjectIdManager.Instance, MailManager.Instance)
    {
    }

    internal static bool TryResolveGroundDoodad(Item item, ISkillManager skills, out uint doodadId)
    {
        doodadId = 0;
        if (item?.Template is not BackpackTemplate backpack || backpack.BackpackType == BackpackType.Glider)
            return false;

        var skill = skills.GetSkillTemplate(backpack.UseSkillId);
        if (skill == null)
        {
            Logger.Warn("Cannot resolve death drop for backpack {0}: missing use skill {1}", item.TemplateId, backpack.UseSkillId);
            return false;
        }

        // IsDropableBackpack selects non-glider equipment-linked actions. The resolved item's
        // own use skill normally has that flag unset. Its physical effect supplies the doodad.
        var effects = skill.Effects?.Select(effect => effect.Template).OfType<PutDownBackpackEffect>().ToArray() ?? [];
        if (effects.Length == 0)
            return false; // Discard-only/consumable equipment has no physical pack to create.

        var destinations = effects.Select(effect => effect.BackpackDoodadId).Distinct().ToArray();
        if (destinations.Length != 1 || destinations[0] == 0)
        {
            Logger.Warn("Cannot resolve death drop for backpack {0}: invalid or ambiguous ground doodad", item.TemplateId);
            return false;
        }

        doodadId = destinations[0];
        return true;
    }

    public bool TryDropOnDeath()
    {
        using var persistence = mailManager.DeferPersist();
        lock (owner.StateSyncRoot)
        {
            if (!owner.IsDead || owner.ParentWorld == null || owner.Inventory?.Equipment == null)
                return false;

            var equipment = owner.Inventory.Equipment;
            var system = owner.Inventory.SystemContainer;
            var item = equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
            if (system == null || !TryResolveGroundDoodad(item, skillManager, out var doodadId))
                return false;
            if (item.Count != 1 || item._holdingContainer != equipment || item.SlotType != SlotType.Equipment)
                return false;

            var destinationSlot = system.GetUnusedSlot(-1);
            if (destinationSlot < 0 || !system.CanAccept(item, destinationSlot))
                return false;

            var originalSlot = item.Slot;
            var originalIndex = equipment.Items.IndexOf(item);
            Doodad doodad = null;
            var moved = false;
            var committed = false;
            try
            {
                doodad = doodadManager.Create(owner.ParentWorld, 0, doodadId, owner, true);
                if (doodad == null)
                {
                    Logger.Warn("Cannot create death-drop doodad {0} for backpack {1}", doodadId, item.Id);
                    return false;
                }

                // Stage membership without callbacks or packets. The placement transaction also
                // snapshots ingredient consumption; no visible drop exists before it commits.
                equipment.Items.RemoveAt(originalIndex);
                system.Items.Add(item);
                item._holdingContainer = system;
                item.SlotType = SlotType.System;
                item.Slot = destinationSlot;
                moved = true;
                equipment.UpdateFreeSlotCount();
                system.UpdateFreeSlotCount();

                doodad.IsPlacementPending = true;
                doodad.IsPersistent = true;
                doodad.Transform = owner.Transform.CloneDetached(doodad);
                doodad.Transform.Local.SetHeight(GetGroundHeight(doodad));
                doodad.AttachPoint = AttachPointKind.None;
                doodad.ItemId = item.Id;
                doodad.ItemTemplateId = item.TemplateId;
                doodad.UccId = item.UccId;
                doodad.SetScale(1f);
                doodad.PlantTime = DateTime.UtcNow;
                Initialize(doodad);

                if (!Persist(item, doodad))
                {
                    Logger.Warn("Death-drop persistence failed for backpack {0}, character {1}; restoring equipment", item.Id, owner.Id);
                    return false;
                }
                committed = true;
                doodad.IsPlacementPending = false;

                // Once committed, notification failures must never re-equip the durable ground pack.
                Publish(() => equipment.OnLeaveContainer(item, system, checked((byte)originalSlot)), item.Id, "equipment cleanup");
                Publish(() => system.OnEnterContainer(item, equipment, checked((byte)originalSlot)), item.Id, "container notification");
                Publish(() => owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DropBackpack,
                    [new ItemRemoveSlot(item.Id, SlotType.Equipment, checked((byte)originalSlot))], [])), item.Id, "inventory notification");
                Publish(() => owner.BroadcastPacket(new SCUnitEquipmentsChangedPacket(owner.ObjId,
                    (byte)EquipmentItemSlot.Backpack, null), true), item.Id, "equipment notification");
                Publish(() => Spawn(doodad), item.Id, "ground spawn");
                Publish(() => RelayToZone(item, doodad), item.Id, "Zone notification");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to drop backpack {0} on death of character {1}", item.Id, owner.Id);
                return committed;
            }
            finally
            {
                if (!committed)
                {
                    if (moved)
                    {
                        system.Items.Remove(item);
                        equipment.Items.Insert(originalIndex, item);
                        item._holdingContainer = equipment;
                        item.SlotType = SlotType.Equipment;
                        item.Slot = originalSlot;
                        equipment.UpdateFreeSlotCount();
                        system.UpdateFreeSlotCount();
                    }
                    if (doodad != null)
                    {
                        // Initialization can arm a phase timer. An abandoned doodad must not act on
                        // the restored pack, persist itself later, or retain its allocated object ID.
                        doodad.IsPersistent = false;
                        doodad.ItemId = 0;
                        doodad.ItemTemplateId = 0;
                        doodad.FuncTask?.Cancel();
                        doodad.FuncTask = null;
                        objectIdManager.ReleaseId(doodad.ObjId);
                    }
                }
            }
        }
    }

    protected virtual float GetGroundHeight(Doodad doodad) =>
        doodad.ParentWorld.Template.GeoData?.GetHeight(doodad.Transform.World.Position) ?? doodad.Transform.World.Position.Z;

    protected virtual void Initialize(Doodad doodad) => doodad.InitDoodad();
    protected virtual bool Persist(Item item, Doodad doodad) => DoodadItemPersistence.TrySavePlacement(item, doodad);
    protected virtual void Spawn(Doodad doodad) => doodad.Spawn();

    private void RelayToZone(Item item, Doodad doodad)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;
        var position = doodad.Transform.World.Position;
        WorldIntegration.RelayDropBackpackToZone?.Invoke(owner.ObjId, item, doodad.TemplateId,
            doodad.Transform.ZoneId, position.X, position.Y, position.Z, true, false, false);
    }

    private static void Publish(Action action, ulong itemId, string stage)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed {0} for committed death-drop backpack {1}", stage, itemId);
        }
    }
}
