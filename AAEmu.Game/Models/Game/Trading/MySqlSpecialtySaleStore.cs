using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Trading;

public sealed class MySqlSpecialtySaleStore(ISaveManager saveManager) : ISpecialtySaleStore
{
    public SpecialtySaleCommitResult Commit(SpecialtySaleWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        try
        {
            return saveManager.ExecuteOperation((connection, transaction) =>
            {
                DeleteClaimedPack(connection, transaction, write);
                UpdateLabor(connection, transaction, write);

                foreach (var mail in write.PayoutMails)
                {
                    foreach (var attachment in mail.Body.Attachments)
                        InsertItem(connection, transaction, attachment);
                    InsertMail(connection, transaction, mail);
                }

                return SpecialtySaleCommitResult.Committed;
            });
        }
        catch (SpecialtySaleConflictException exception)
        {
            return exception.Result;
        }
    }

    private static void DeleteClaimedPack(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SpecialtySaleWrite write)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM items WHERE id = @id AND template_id = @template_id AND owner = @owner " +
            "AND container_id = @container_id AND slot_type = @slot_type AND slot = @slot AND count = @count";
        command.Parameters.AddWithValue("@id", write.PackItemId);
        command.Parameters.AddWithValue("@template_id", write.PackTemplateId);
        command.Parameters.AddWithValue("@owner", write.PackOwnerId);
        command.Parameters.AddWithValue("@container_id", write.PackContainerId);
        command.Parameters.AddWithValue("@slot_type", (int)write.PackSlotType);
        command.Parameters.AddWithValue("@slot", write.PackSlot);
        command.Parameters.AddWithValue("@count", write.PackCount);
        command.Prepare();

        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtySaleConflictException(SpecialtySaleCommitResult.PackNotPersisted);
    }

    private static void UpdateLabor(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SpecialtySaleWrite write)
    {
        if (write.ExpectedLabor == write.NewLabor && write.ExpectedLocalLabor == write.NewLocalLabor)
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE accounts SET labor = @new_labor, local_labor = @new_local_labor " +
            "WHERE account_id = @account_id AND labor = @expected_labor AND local_labor = @expected_local_labor";
        command.Parameters.AddWithValue("@new_labor", write.NewLabor);
        command.Parameters.AddWithValue("@new_local_labor", write.NewLocalLabor);
        command.Parameters.AddWithValue("@account_id", write.AccountId);
        command.Parameters.AddWithValue("@expected_labor", write.ExpectedLabor);
        command.Parameters.AddWithValue("@expected_local_labor", write.ExpectedLocalLabor);
        command.Prepare();

        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtySaleConflictException(SpecialtySaleCommitResult.LaborConflict);
    }

    private static void InsertItem(MySqlConnection connection, MySqlTransaction transaction, Item item)
    {
        var details = new PacketStream();
        item.WriteDetails(details);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO items (" +
            "`id`,`type`,`template_id`,`container_id`,`slot_type`,`slot`,`count`,`detail_type`,`details`,`lifespan_mins`,`made_unit_id`," +
            "`unsecure_time`,`unpack_time`,`owner`,`created_at`,`grade`,`flags`,`ucc`," +
            "`expire_time`,`expire_online_minutes`,`charge_time`,`charge_count`) VALUES (" +
            "@id,@type,@template_id,@container_id,@slot_type,@slot,@count,@detail_type,@details,@lifespan_mins,@made_unit_id," +
            "@unsecure_time,@unpack_time,@owner,@created_at,@grade,@flags,@ucc," +
            "@expire_time,@expire_online_minutes,@charge_time,@charge_count)";
        command.Parameters.AddWithValue("@id", item.Id);
        command.Parameters.AddWithValue("@type", item.GetType().ToString());
        command.Parameters.AddWithValue("@template_id", item.TemplateId);
        command.Parameters.AddWithValue("@container_id", item._holdingContainer?.ContainerId ?? 0);
        command.Parameters.AddWithValue("@slot_type", (int)item.SlotType);
        command.Parameters.AddWithValue("@slot", item.Slot);
        command.Parameters.AddWithValue("@count", item.Count);
        command.Parameters.AddWithValue("@detail_type", (byte)item.DetailType);
        command.Parameters.AddWithValue("@details", details.GetBytes());
        command.Parameters.AddWithValue("@lifespan_mins", item.LifespanMins);
        command.Parameters.AddWithValue("@made_unit_id", item.MadeUnitId);
        command.Parameters.AddWithValue("@unsecure_time", item.UnsecureTime);
        command.Parameters.AddWithValue("@unpack_time", item.UnpackTime);
        command.Parameters.AddWithValue("@owner", item.OwnerId);
        command.Parameters.AddWithValue("@created_at", item.CreateTime);
        command.Parameters.AddWithValue("@grade", item.Grade);
        command.Parameters.AddWithValue("@flags", (byte)item.ItemFlags);
        command.Parameters.AddWithValue("@ucc", item.UccId);
        command.Parameters.AddWithValue("@expire_time", item.ExpirationTime);
        command.Parameters.AddWithValue("@expire_online_minutes", item.ExpirationOnlineMinutesLeft);
        command.Parameters.AddWithValue("@charge_time", item.ChargeStartTime);
        command.Parameters.AddWithValue("@charge_count", item.ChargeCount);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    private static void InsertMail(MySqlConnection connection, MySqlTransaction transaction, BaseMail mail)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO mails (" +
            "`id`,`type`,`status`,`title`,`text`,`sender_id`,`sender_name`," +
            "`attachment_count`,`receiver_id`,`receiver_name`,`open_date`,`send_date`,`received_date`," +
            "`returned`,`extra`,`money_amount_1`,`money_amount_2`,`money_amount_3`," +
            "`attachment0`,`attachment1`,`attachment2`,`attachment3`,`attachment4`,`attachment5`," +
            "`attachment6`,`attachment7`,`attachment8`,`attachment9`) VALUES (" +
            "@id,@type,@status,@title,@text,@sender_id,@sender_name," +
            "@attachment_count,@receiver_id,@receiver_name,@open_date,@send_date,@received_date," +
            "@returned,@extra,@money_1,@money_2,@money_3," +
            "@attachment0,@attachment1,@attachment2,@attachment3,@attachment4,@attachment5," +
            "@attachment6,@attachment7,@attachment8,@attachment9)";
        command.Parameters.AddWithValue("@id", mail.Id);
        command.Parameters.AddWithValue("@type", (byte)mail.MailType);
        command.Parameters.AddWithValue("@status", mail.Header.Status);
        command.Parameters.AddWithValue("@title", mail.Title);
        command.Parameters.AddWithValue("@text", mail.Body.Text);
        command.Parameters.AddWithValue("@sender_id", mail.Header.SenderId);
        command.Parameters.AddWithValue("@sender_name", mail.Header.SenderName);
        command.Parameters.AddWithValue("@attachment_count", mail.Header.Attachments);
        command.Parameters.AddWithValue("@receiver_id", mail.Header.ReceiverId);
        command.Parameters.AddWithValue("@receiver_name", mail.ReceiverName);
        command.Parameters.AddWithValue("@open_date", mail.OpenDate);
        command.Parameters.AddWithValue("@send_date", mail.Body.SendDate);
        command.Parameters.AddWithValue("@received_date", mail.Body.RecvDate);
        command.Parameters.AddWithValue("@returned", mail.Header.Returned ? 1 : 0);
        command.Parameters.AddWithValue("@extra", mail.Header.Extra);
        command.Parameters.AddWithValue("@money_1", mail.Body.CopperCoins);
        command.Parameters.AddWithValue("@money_2", mail.Body.BillingAmount);
        command.Parameters.AddWithValue("@money_3", mail.Body.MoneyAmount2);

        for (var i = 0; i < MailBody.MaxMailAttachments; i++)
        {
            var attachmentId = i < mail.Body.Attachments.Count ? mail.Body.Attachments[i].Id : 0;
            command.Parameters.AddWithValue("@attachment" + i, attachmentId);
        }

        command.Prepare();
        command.ExecuteNonQuery();
    }
}

internal sealed class SpecialtySaleConflictException(SpecialtySaleCommitResult result) : Exception
{
    public SpecialtySaleCommitResult Result { get; } = result;
}
