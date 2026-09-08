using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Trading;

public sealed class MySqlSpecialtyPurchaseStore(
    ISaveManager saveManager,
    ISpecialtyMarketStore marketStore) : ISpecialtyPurchaseStore
{
    public bool Commit(SpecialtyPurchaseWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.CargoItem);
        ArgumentNullException.ThrowIfNull(write.Market);
        if (write.CharacterId == 0 || write.AccountId == 0 ||
            write.NewMoney < 0 || write.NewMoney >= write.ExpectedMoney)
            throw new ArgumentException("Purchase must decrease a valid character's money without overdrawing it.", nameof(write));
        if (write.NewLabor < 0 || write.NewLabor > write.ExpectedLabor ||
            write.NewLocalLabor < 0 || write.NewLocalLabor > write.ExpectedLocalLabor)
            throw new ArgumentException("Purchase labor must not increase or overdraw either labor balance.", nameof(write));

        var cargo = write.CargoItem;
        var equipment = cargo._holdingContainer;
        if (cargo.Id == 0 || cargo.TemplateId == 0 ||
            cargo.Template is not BackpackTemplate { BackpackType: BackpackType.TradeGoods } ||
            cargo.Template.Id != cargo.TemplateId || cargo.Count != 1 || cargo.OwnerId != write.CharacterId ||
            cargo.SlotType != SlotType.Equipment || cargo.Slot != (int)EquipmentItemSlot.Backpack ||
            equipment == null || equipment.ContainerId == 0 || equipment.OwnerId != write.CharacterId ||
            equipment.ContainerType != SlotType.Equipment || equipment.Items.Any(item => item?.Id == cargo.Id))
            throw new ArgumentException("Cargo must be an untracked single trade good prepared for the character's backpack slot.", nameof(write));

        var previous = write.PreviousBackpack;
        if (previous != null &&
            (previous.Id == 0 || previous.Id == cargo.Id || previous.TemplateId == 0 || previous.Count != 1 ||
             previous.Template is not BackpackTemplate { BackpackType: BackpackType.Glider } ||
             previous.Template.Id != previous.TemplateId ||
             previous.OwnerId != write.CharacterId || previous._holdingContainer != equipment ||
             previous.SlotType != SlotType.Equipment || previous.Slot != (int)EquipmentItemSlot.Backpack ||
             !equipment.Items.Contains(previous) || write.BagContainerId == 0 ||
             write.BagContainerId == equipment.ContainerId || write.BagSlot < 0))
            throw new ArgumentException("Previous backpack must be a live equipped glider with a valid bag destination.", nameof(write));
        if (equipment.Items.Any(item => item?.Slot == cargo.Slot && item != previous))
            throw new ArgumentException("The backpack slot contains an unexpected item.", nameof(write));

        try
        {
            return saveManager.ExecuteOperation((connection, transaction) =>
            {
                marketStore.Apply(connection, transaction, write.Market);
                UpdateMoney(connection, transaction, write);
                UpdateLabor(connection, transaction, write);
                ClaimSlot(connection, transaction, cargo._holdingContainer.ContainerId,
                    SlotType.Equipment, (int)EquipmentItemSlot.Backpack, previous?.Id ?? 0);
                if (previous != null)
                {
                    ClaimSlot(connection, transaction, write.BagContainerId,
                        SlotType.Inventory, write.BagSlot, 0);
                    MovePreviousBackpack(connection, transaction, write);
                }
                ItemPersistence.Insert(connection, transaction, cargo);
                return true;
            });
        }
        catch (SpecialtyPurchaseConflictException)
        {
            return false;
        }
        catch (SpecialtyMarketConflictException)
        {
            return false;
        }
    }

    private static void UpdateMoney(MySqlConnection connection, MySqlTransaction transaction, SpecialtyPurchaseWrite write)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Character money is normally persisted by autosave, so the database value may
        // legitimately lag the live balance protected by the caller's character lock.
        command.CommandText = "UPDATE characters SET money = @new_money " +
            "WHERE id = @character_id AND account_id = @account_id";
        command.Parameters.AddWithValue("@new_money", write.NewMoney);
        command.Parameters.AddWithValue("@character_id", write.CharacterId);
        command.Parameters.AddWithValue("@account_id", write.AccountId);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtyPurchaseConflictException();
    }

    private static void ClaimSlot(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ulong containerId,
        SlotType slotType,
        int slot,
        ulong expectedItemId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM items WHERE container_id = @container_id " +
            "AND slot_type = @slot_type AND slot = @slot FOR UPDATE";
        command.Parameters.AddWithValue("@container_id", containerId);
        command.Parameters.AddWithValue("@slot_type", (int)slotType);
        command.Parameters.AddWithValue("@slot", slot);
        command.Prepare();
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            if (expectedItemId != 0)
                throw new SpecialtyPurchaseConflictException();
            return;
        }
        if (expectedItemId == 0 || reader.GetUInt64(0) != expectedItemId)
            throw new SpecialtyPurchaseConflictException();
        if (reader.Read())
            throw new SpecialtyPurchaseConflictException();
    }

    private static void UpdateLabor(MySqlConnection connection, MySqlTransaction transaction, SpecialtyPurchaseWrite write)
    {
        if (write.ExpectedLabor == write.NewLabor && write.ExpectedLocalLabor == write.NewLocalLabor)
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE accounts SET labor = @new_labor, local_labor = @new_local_labor " +
            "WHERE account_id = @account_id AND labor = @expected_labor AND local_labor = @expected_local_labor";
        command.Parameters.AddWithValue("@new_labor", write.NewLabor);
        command.Parameters.AddWithValue("@new_local_labor", write.NewLocalLabor);
        command.Parameters.AddWithValue("@account_id", write.AccountId);
        command.Parameters.AddWithValue("@expected_labor", write.ExpectedLabor);
        command.Parameters.AddWithValue("@expected_local_labor", write.ExpectedLocalLabor);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtyPurchaseConflictException();
    }

    private static void MovePreviousBackpack(MySqlConnection connection, MySqlTransaction transaction, SpecialtyPurchaseWrite write)
    {
        var previous = write.PreviousBackpack;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Keep the live glider equipped until the caller publishes the committed purchase.
        command.CommandText = "UPDATE items SET container_id = @bag_container_id, slot_type = @bag_slot_type, slot = @bag_slot " +
            "WHERE id = @id AND template_id = @template_id AND owner = @owner AND container_id = @container_id " +
            "AND slot_type = @slot_type AND slot = @slot AND count = @count";
        command.Parameters.AddWithValue("@bag_container_id", write.BagContainerId);
        command.Parameters.AddWithValue("@bag_slot_type", (int)SlotType.Inventory);
        command.Parameters.AddWithValue("@bag_slot", write.BagSlot);
        command.Parameters.AddWithValue("@id", previous.Id);
        command.Parameters.AddWithValue("@template_id", previous.TemplateId);
        command.Parameters.AddWithValue("@owner", previous.OwnerId);
        command.Parameters.AddWithValue("@container_id", previous._holdingContainer.ContainerId);
        command.Parameters.AddWithValue("@slot_type", (int)previous.SlotType);
        command.Parameters.AddWithValue("@slot", previous.Slot);
        command.Parameters.AddWithValue("@count", previous.Count);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtyPurchaseConflictException();
    }
}

internal sealed class SpecialtyPurchaseConflictException : Exception;
