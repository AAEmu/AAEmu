using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Milestones;
using AAEmu.Game.Models.Game.Sagas;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The character side of milestone progression: loads and saves the <see cref="MilestoneProgressState"/>
/// rows, evaluates reversed quest-completion triggers against the shipped milestone catalog, and
/// synchronizes the chronicle (saga book) update family on every status edge — once per edge,
/// never on replay. Progress records live in character_milestones; the grant-once ledger is
/// GF-W13's character_saga_reward_grants, shared through <see cref="CharacterSagaProgress"/>.
/// </summary>
public class CharacterMilestoneProgress
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Character Owner { get; }

    /// <summary>Changes discovered during <see cref="Reconcile"/> whose sync is owed to the client
    /// but must not be sent from load — flushed by <see cref="SendInitialState"/>.</summary>
    private List<MilestoneProgressChange> _pendingSync = [];

    public MilestoneProgressState State { get; }

    /// <param name="owner">The character this state belongs to.</param>
    /// <param name="grantLedger">GF-W13's shared grant-once ledger (CharacterSagaProgress.State);
    /// milestone grants ride it keyed by milestone id under GrantScope, never a private ledger.</param>
    public CharacterMilestoneProgress(Character owner, SagaProgressState grantLedger)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        State = new MilestoneProgressState(grantLedger);
    }

    /// <summary>
    /// Restores milestone records from MySQL. A missing update is reported loudly and leaves the
    /// state empty — it must not block the login, same as the pending saga restore in
    /// <see cref="CharacterSagaProgress"/>. Records naming milestones the content no longer carries
    /// are dropped with an error instead of converging later against a ghost id.
    /// </summary>
    public void Load(MySqlConnection connection)
    {
        try
        {
            var catalog = MilestoneGameData.Instance.Catalog;
            List<MilestoneProgressRow> rows = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `milestone_id`,`status`,`completed_count` FROM character_milestones WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var milestoneId = reader.GetUInt32("milestone_id");
                    if (!catalog.Contains(milestoneId))
                    {
                        Logger.Error(
                            "Milestones: row for milestone {0} on {1} has no content row, dropped",
                            milestoneId, Owner.Name);
                        continue;
                    }

                    rows.Add(new MilestoneProgressRow(
                        milestoneId,
                        (MilestoneStatus)Convert.ToSByte(reader["status"]),
                        reader.GetUInt16("completed_count")));
                }
            }

            State.Import(rows);
        }
        catch (MySqlException ex)
        {
            // The character must still log in when the milestone update has not been applied yet.
            Logger.Error(
                ex,
                "Milestone progress load skipped for {0} — is the character_milestones update applied?",
                Owner.Name);
        }
    }

    /// <summary>
    /// Rewrites this character's milestone rows inside the caller's character-save transaction, so
    /// the state lands with the quest state it was derived from.
    /// </summary>
    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        try
        {
            var rows = State.ExportRows();

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_milestones WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }

            if (rows.Count == 0)
                return;

            using var insert = connection.CreateCommand();
            insert.Connection = connection;
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO character_milestones(`owner`,`milestone_id`,`status`,`completed_count`) " +
                "VALUES(@owner,@milestone_id,@status,@completed_count)";
            insert.Parameters.AddWithValue("@owner", Owner.Id);
            foreach (var row in rows)
            {
                insert.Parameters.AddWithValue("@milestone_id", row.MilestoneId);
                insert.Parameters.AddWithValue("@status", (sbyte)row.Status);
                insert.Parameters.AddWithValue("@completed_count", row.CompletedCount);
                insert.ExecuteNonQuery();
                insert.Parameters.Clear();
            }
        }
        catch (MySqlException ex)
        {
            // The character save must still succeed when the update has not been applied yet.
            Logger.Error(
                ex,
                "Milestone progress save skipped for {0} — is the character_milestones update applied?",
                Owner.Name);
        }
    }

    /// <summary>
    /// Re-derives every recorded milestone from the completed-quest bits after the quests loaded.
    /// Characters that finished milestone quests before this state existed converge here; status
    /// edges found during load are queued, not sent — <see cref="SendInitialState"/> pushes them
    /// with the world-entry burst.
    /// </summary>
    public void Reconcile()
    {
        var changes = State.Reconcile(
            MilestoneGameData.Instance.Catalog,
            questId => Owner.Quests != null && Owner.Quests.HasQuestCompleted(questId),
            ServerCalendar.UtcNow);

        foreach (var change in changes)
        {
            if (change.StatusChanged)
                _pendingSync.Add(change);
            ReportChange(change, "reconcile");
        }
    }

    /// <summary>Quest-completion trigger: advances the milestone the finished quest names,
    /// syncs and records the grant on the completion edge — once.</summary>
    public void OnQuestCompleted(uint questId)
    {
        var change = State.OnQuestCompleted(
            MilestoneGameData.Instance.Catalog,
            questId,
            questIdToCheck => Owner.Quests != null && Owner.Quests.HasQuestCompleted(questIdToCheck),
            ServerCalendar.UtcNow);

        if (change is null)
            return;

        if (change.StatusChanged)
            Sync(change, "quest completion");

        ReportChange(change, "quest completion");
    }

    /// <summary>
    /// World-entry push: flushes the sync owed by load-time reconciliation. There is no milestone
    /// list packet family in the 10.0.2.13 corpus, so this is per-edge updates only, sent once —
    /// the pending set is cleared with it.
    /// </summary>
    public void SendInitialState()
    {
        if (_pendingSync.Count == 0)
            return;

        foreach (var change in _pendingSync)
            Sync(change, "login catch-up");

        _pendingSync.Clear();
    }

    private void Sync(MilestoneProgressChange change, string reason)
    {
        if (!MilestoneSyncRules.CanSyncChronicleType(
                change.MilestoneId, SagaQuestGameData.Instance.Catalog))
        {
            // The chronicle type space belongs to saga group ids; sending this milestone's id
            // would move that group's story entry. No milestone-specific family exists to carry
            // it, so the edge is logged instead of mis-synced.
            Logger.Warn(
                "Milestones: milestone {0} status edge for {1} ({2}) shares its id with a saga " +
                "group type — chronicle sync suppressed",
                change.MilestoneId, Owner.Name, reason);
            return;
        }

        Owner.SendPacket(new SCChronicleInfoUpdatePacket(
            (sbyte)change.PreviousStatus,
            (sbyte)change.CurrentStatus,
            unchecked((int)change.MilestoneId)));
    }

    private void ReportChange(MilestoneProgressChange change, string reason)
    {
        if (change.Granted)
        {
            Logger.Info(
                "Milestones: milestone {0} completed, grant taken (scope {1}) for {2} ({3})",
                change.MilestoneId, MilestoneProgressState.GrantScope, Owner.Name, reason);
        }

        if (change.StatusChanged)
        {
            Logger.Info(
                "Milestones: milestone {0} {1} -> {2} for {3} ({4} quests complete, {5})",
                change.MilestoneId, change.PreviousStatus, change.CurrentStatus,
                Owner.Name, change.CompletedCount, reason);
        }
    }
}
