using System.Data.Common;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

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
        ArgumentNullException.ThrowIfNull(write.Inventory);
        if (write.CharacterId == 0 || write.AccountId == 0 ||
            write.BankMoney < 0 || write.Inventory.OwnerId != write.CharacterId ||
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
                ApplyCharacterWrite(connection, transaction, write);
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

    internal static void ApplyCharacterWrite(DbConnection connection, DbTransaction transaction, SpecialtyPurchaseWrite write)
    {
        // Reconcile pending moves/deletions before checking occupancy. All of these writes roll
        // back together with the cargo and market stock if any of the claims fail.
        write.Inventory.Apply(connection, transaction);
        UpdateMoney(connection, transaction, write);
        UpdateLabor(connection, transaction, write);
        ClaimSlot(connection, transaction, write.CargoItem._holdingContainer.ContainerId,
            SlotType.Equipment, (int)EquipmentItemSlot.Backpack, write.PreviousBackpack?.Id ?? 0);
        if (write.PreviousBackpack != null)
        {
            ClaimSlot(connection, transaction, write.BagContainerId, SlotType.Inventory, write.BagSlot, 0);
            MovePreviousBackpack(connection, transaction, write);
        }
        ItemPersistence.Insert(connection, transaction, write.CargoItem);
    }

    private static void UpdateMoney(DbConnection connection, DbTransaction transaction, SpecialtyPurchaseWrite write)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Pocket and bank form one balance state: ChangeWallets may have transferred between
        // them since autosave. Persisting only the pocket would recreate the withdrawn bank money.
        command.CommandText = "UPDATE characters SET money = @new_money, money2 = @bank_money " +
            "WHERE id = @character_id AND account_id = @account_id";
        command.AddParameter("@new_money", write.NewMoney);
        command.AddParameter("@bank_money", write.BankMoney);
        command.AddParameter("@character_id", write.CharacterId);
        command.AddParameter("@account_id", write.AccountId);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtyPurchaseConflictException();
    }

    private static void ClaimSlot(
        DbConnection connection,
        DbTransaction transaction,
        ulong containerId,
        SlotType slotType,
        int slot,
        ulong expectedItemId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM items WHERE container_id = @container_id " +
            "AND slot_type = @slot_type AND slot = @slot FOR UPDATE";
        command.AddParameter("@container_id", containerId);
        command.AddParameter("@slot_type", (int)slotType);
        command.AddParameter("@slot", slot);
        command.Prepare();
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            if (expectedItemId != 0)
                throw new SpecialtyPurchaseConflictException();
            return;
        }
        if (expectedItemId == 0 || Convert.ToUInt64(reader.GetValue(0)) != expectedItemId)
            throw new SpecialtyPurchaseConflictException();
        if (reader.Read())
            throw new SpecialtyPurchaseConflictException();
    }

    private static void UpdateLabor(DbConnection connection, DbTransaction transaction, SpecialtyPurchaseWrite write)
    {
        if (write.ExpectedLabor == write.NewLabor && write.ExpectedLocalLabor == write.NewLocalLabor)
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE accounts SET labor = @new_labor, local_labor = @new_local_labor " +
            "WHERE account_id = @account_id AND labor = @expected_labor AND local_labor = @expected_local_labor";
        command.AddParameter("@new_labor", write.NewLabor);
        command.AddParameter("@new_local_labor", write.NewLocalLabor);
        command.AddParameter("@account_id", write.AccountId);
        command.AddParameter("@expected_labor", write.ExpectedLabor);
        command.AddParameter("@expected_local_labor", write.ExpectedLocalLabor);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtyPurchaseConflictException();
    }

    private static void MovePreviousBackpack(DbConnection connection, DbTransaction transaction, SpecialtyPurchaseWrite write)
    {
        var previous = write.PreviousBackpack;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Keep the live glider equipped until the caller publishes the committed purchase.
        command.CommandText = "UPDATE items SET container_id = @bag_container_id, slot_type = @bag_slot_type, slot = @bag_slot " +
            "WHERE id = @id AND template_id = @template_id AND owner = @owner AND container_id = @container_id " +
            "AND slot_type = @slot_type AND slot = @slot AND count = @count";
        command.AddParameter("@bag_container_id", write.BagContainerId);
        command.AddParameter("@bag_slot_type", (int)SlotType.Inventory);
        command.AddParameter("@bag_slot", write.BagSlot);
        command.AddParameter("@id", previous.Id);
        command.AddParameter("@template_id", previous.TemplateId);
        command.AddParameter("@owner", previous.OwnerId);
        command.AddParameter("@container_id", previous._holdingContainer.ContainerId);
        command.AddParameter("@slot_type", (int)previous.SlotType);
        command.AddParameter("@slot", previous.Slot);
        command.AddParameter("@count", previous.Count);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtyPurchaseConflictException();
    }
}

internal sealed class SpecialtyPurchaseConflictException : Exception;
