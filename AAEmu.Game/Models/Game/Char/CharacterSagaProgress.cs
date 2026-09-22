using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Sagas;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The character side of saga-group progression: loads and saves the
/// <see cref="SagaProgressState"/> rows, evaluates eligibility and completion against the shipped
/// saga catalog, and synchronizes the chronicle (saga book) packets on login / zone-in and on every
/// status change. The pure state machine and its grant ledger live in <see cref="SagaProgressState"/>
/// so GF-W14's milestone work can drive the same triggers.
/// </summary>
public class CharacterSagaProgress(Character owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Character Owner { get; } = owner;

    public SagaProgressState State { get; } = new();

    /// <summary>Start gate for a quest: ChronicleInfoNeed until the group's record exists.</summary>
    public SagaStartGate EvaluateStartGate(uint questId) =>
        State.EvaluateStartGate(SagaQuestGameData.Instance.Catalog, questId);

    /// <summary>
    /// Restores group records and the reward ledger from MySQL. A missing update is reported loudly
    /// and leaves the state empty — it must not block the login, same as the pending quest-effect
    /// restore in <see cref="CharacterQuests"/>.
    /// </summary>
    public void Load(MySqlConnection connection)
    {
        try
        {
            var catalog = SagaQuestGameData.Instance.Catalog;
            List<SagaGroupProgressRow> groups = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `saga_quest_group_id`,`status`,`completed_count` FROM character_saga_groups WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var groupId = reader.GetUInt32("saga_quest_group_id");
                    if (!catalog.TryGetGroup(groupId, out _))
                    {
                        Logger.Error(
                            "Saga: row for group {0} on {1} has no content row, dropped",
                            groupId, Owner.Name);
                        continue;
                    }

                    groups.Add(new SagaGroupProgressRow(
                        groupId,
                        (SagaGroupStatus)Convert.ToSByte(reader["status"]),
                        reader.GetUInt16("completed_count")));
                }
            }

            List<SagaRewardGrantRow> grants = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `saga_quest_group_id`,`grant_key` FROM character_saga_reward_grants WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var groupId = reader.GetUInt32("saga_quest_group_id");
                    if (!catalog.TryGetGroup(groupId, out _))
                    {
                        Logger.Error(
                            "Saga: reward grant for group {0} on {1} has no content row, dropped",
                            groupId, Owner.Name);
                        continue;
                    }

                    grants.Add(new SagaRewardGrantRow(groupId, reader.GetUInt32("grant_key")));
                }
            }

            State.Import(groups, grants);
        }
        catch (MySqlException ex)
        {
            // The character must still log in when the saga update has not been applied yet.
            Logger.Error(
                ex,
                "Saga progress load skipped for {0} — is the character_saga_groups update applied?",
                Owner.Name);
        }
    }

    /// <summary>
    /// Rewrites this character's saga rows inside the caller's character-save transaction, so the
    /// state lands with the quest state it was derived from.
    /// </summary>
    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var groups = State.ExportGroups();
            var grants = State.ExportGrants();

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_saga_groups WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }

            if (groups.Count > 0)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO character_saga_groups(`owner`,`saga_quest_group_id`,`status`,`completed_count`) " +
                    "VALUES(@owner,@group_id,@status,@completed_count)";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                foreach (var row in groups)
                {
                    command.Parameters.AddWithValue("@group_id", row.GroupId);
                    command.Parameters.AddWithValue("@status", (sbyte)row.Status);
                    command.Parameters.AddWithValue("@completed_count", row.CompletedCount);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_saga_reward_grants WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }

            if (grants.Count > 0)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO character_saga_reward_grants(`owner`,`saga_quest_group_id`,`grant_key`) " +
                    "VALUES(@owner,@group_id,@grant_key)";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                foreach (var row in grants)
                {
                    command.Parameters.AddWithValue("@group_id", row.GroupId);
                    command.Parameters.AddWithValue("@grant_key", row.GrantKey);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }
        }
        catch (MySqlException ex)
        {
            // The character save must still succeed when the update has not been applied yet.
            Logger.Error(
                ex,
                "Saga progress save skipped for {0} — is the character_saga_groups update applied?",
                Owner.Name);
        }
    }

    /// <summary>
    /// Re-derives every unlocked group from the completed-quest bits after the quests loaded.
    /// Characters that finished saga quests before this state existed (or across a failed save)
    /// converge here; no packets are sent — <see cref="SendInitialState"/> pushes the result.
    /// </summary>
    public void Reconcile()
    {
        var changes = State.Reconcile(
            SagaQuestGameData.Instance.Catalog,
            questId => Owner.Quests != null && Owner.Quests.HasQuestCompleted(questId));

        foreach (var change in changes)
            ReportChange(change, "reconcile");
    }

    /// <summary>Quest-completion trigger: advances the owning group, syncs and records the grant.</summary>
    public void OnQuestCompleted(uint questId)
    {
        var change = State.OnQuestCompleted(
            SagaQuestGameData.Instance.Catalog,
            questId,
            questIdToCheck => Owner.Quests != null && Owner.Quests.HasQuestCompleted(questIdToCheck));

        if (change is null)
            return;

        if (change.StatusChanged)
        {
            Owner.SendPacket(new SCChronicleInfoUpdatePacket(
                (sbyte)change.PreviousStatus,
                (sbyte)change.CurrentStatus,
                unchecked((int)change.GroupId)));
        }

        ReportChange(change, "quest completion");
    }

    /// <summary>
    /// Chronicle book "buy episode": creates the record that makes the group's quests startable.
    /// Unknown ids and configured-but-chargeable requirements refuse loudly instead of guessing a
    /// price; an already-held record answers with the current status (idempotent).
    /// </summary>
    public void HandleChronicleBuy(int typeId)
    {
        var groupId = unchecked((uint)typeId);
        var catalog = SagaQuestGameData.Instance.Catalog;

        if (!catalog.TryGetGroup(groupId, out var group))
        {
            Logger.Error(
                "Saga: {0} ({1}) asked to buy unknown saga group {2}", Owner.Name, Owner.Id, typeId);
            ReplyBuy(typeId, false, ErrorMessageType.Invalid, SagaGroupStatus.Active);
            return;
        }

        if (group.CurrencyValue > 0 || group.ItemSetId > 0)
        {
            // The purchase requirement is priced content we cannot charge: there is no shipped
            // wallet mapping for an arbitrary saga currency id, and inventing one would move the
            // player's balance on a guess. Refuse loudly instead.
            Logger.Error(
                "Saga: group {0} purchase requirement (currency {1} x{2}, item set {3}) can not be charged; buy refused for {4}",
                group.Id, group.CurrencyId, group.CurrencyValue, group.ItemSetId, Owner.Name);
            var held = State.TryGetRecord(group.Id, out var heldRecord)
                ? heldRecord.Status
                : SagaGroupStatus.Active;
            ReplyBuy(typeId, false, ErrorMessageType.InternalError, held);
            return;
        }

        var result = State.Unlock(catalog, group.Id);
        var status = State.TryGetRecord(group.Id, out var record)
            ? record.Status
            : SagaGroupStatus.Active;

        ReplyBuy(typeId, result != SagaUnlockResult.UnknownGroup, ErrorMessageType.NoErrorMessage, status);

        if (result == SagaUnlockResult.Ok)
            Logger.Info("Saga: group {0} unlocked for {1} ({2})", group.Id, Owner.Name, Owner.Id);
    }

    /// <summary>
    /// Full chronicle info list in group order — the login / world-entry sync the story tab opens
    /// from. Sent even when empty so a fresh character starts from a clean, server-authoritative
    /// list.
    /// </summary>
    public void SendInitialState()
    {
        var catalog = SagaQuestGameData.Instance.Catalog;
        List<(int Type, sbyte Status)> entries = [];
        foreach (var group in catalog.Groups)
        {
            if (State.TryGetRecord(group.Id, out var record))
                entries.Add(((int)group.Id, (sbyte)record.Status));
        }

        Owner.SendPacket(new SCChronicleInfoListPacket(isFirst: true, endList: true, entries));
    }

    private void ReplyBuy(int typeId, bool result, ErrorMessageType errorMessage, SagaGroupStatus status)
    {
        Owner.SendPacket(new SCChronicleInfoBuyPacket(
            result, errorMessage, typeId, (sbyte)status));
    }

    private void ReportChange(SagaProgressChange change, string reason)
    {
        if (change.RewardGranted)
        {
            Logger.Info(
                "Saga: group {0} completion reward granted (milestone key {1}) for {2} ({3})",
                change.GroupId, change.RewardGrantKey, Owner.Name, reason);
        }

        if (change.StatusChanged)
        {
            Logger.Info(
                "Saga: group {0} {1} -> {2} for {3} ({4} quests complete)",
                change.GroupId, change.PreviousStatus, change.CurrentStatus,
                Owner.Name, change.CompletedCount);
        }
    }
}
