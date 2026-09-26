using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Tasks.Butlers;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Completes persisted Farmhand harvest intervals and asks the farmhand specialty-trade processor to settle due jobs.
/// Each harvest interval's idempotence marker, job mutation, reward mail attachments, and optional final XP award
/// commit in one caller-owned transaction.
/// </summary>
public sealed class ButlerHarvestCompletionService : Singleton<ButlerHarvestCompletionService>, ILoadable
{
    private const uint CurrentHarvestGrowthTimeModifier = 0;
    private const uint CurrentHarvestBonusRatioModifier = 0;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly IButlerManager _butlerManager;
    private readonly IButlerRepository _repository;
    private readonly IMailManager _mailManager;
    private readonly IItemManager _itemManager;
    private readonly INameManager _nameManager;
    private readonly IWorldManager _worldManager;
    private readonly INeutralLootPackRoller _lootPackRoller;
    private readonly ButlerHarvestRewardPolicy _rewardPolicy;
    private readonly ITaskManager _taskManager;
    private readonly IButlerSpecialtyTradeJobProcessor _specialtyTradeProcessor;
    private readonly Func<MySqlConnection> _openConnection;
    private readonly Func<DateTime> _utcNow;
    private bool _loaded;

    public ButlerHarvestCompletionService(
        IButlerManager butlerManager,
        IButlerRepository repository,
        IMailManager mailManager,
        IItemManager itemManager,
        INameManager nameManager,
        IWorldManager worldManager,
        INeutralLootPackRoller lootPackRoller,
        ButlerHarvestRewardPolicy rewardPolicy,
        ITaskManager taskManager)
        : this(butlerManager, repository, mailManager, itemManager, nameManager, worldManager,
            lootPackRoller, rewardPolicy, taskManager, null)
    {
    }

    public ButlerHarvestCompletionService(
        IButlerManager butlerManager,
        IButlerRepository repository,
        IMailManager mailManager,
        IItemManager itemManager,
        INameManager nameManager,
        IWorldManager worldManager,
        INeutralLootPackRoller lootPackRoller,
        ButlerHarvestRewardPolicy rewardPolicy,
        ITaskManager taskManager,
        IButlerSpecialtyTradeJobProcessor specialtyTradeProcessor)
        : this(butlerManager, repository, mailManager, itemManager, nameManager, worldManager,
            lootPackRoller, rewardPolicy, taskManager, specialtyTradeProcessor,
            MySQL.CreateConnection, () => DateTime.UtcNow)
    {
    }

    internal ButlerHarvestCompletionService(
        IButlerManager butlerManager,
        IButlerRepository repository,
        IMailManager mailManager,
        IItemManager itemManager,
        INameManager nameManager,
        IWorldManager worldManager,
        INeutralLootPackRoller lootPackRoller,
        ButlerHarvestRewardPolicy rewardPolicy,
        ITaskManager taskManager,
        Func<MySqlConnection> openConnection,
        Func<DateTime> utcNow)
        : this(butlerManager, repository, mailManager, itemManager, nameManager, worldManager,
            lootPackRoller, rewardPolicy, taskManager, null, openConnection, utcNow)
    {
    }

    internal ButlerHarvestCompletionService(
        IButlerManager butlerManager,
        IButlerRepository repository,
        IMailManager mailManager,
        IItemManager itemManager,
        INameManager nameManager,
        IWorldManager worldManager,
        INeutralLootPackRoller lootPackRoller,
        ButlerHarvestRewardPolicy rewardPolicy,
        ITaskManager taskManager,
        IButlerSpecialtyTradeJobProcessor specialtyTradeProcessor,
        Func<MySqlConnection> openConnection,
        Func<DateTime> utcNow)
    {
        _butlerManager = butlerManager ?? throw new ArgumentNullException(nameof(butlerManager));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mailManager = mailManager ?? throw new ArgumentNullException(nameof(mailManager));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _nameManager = nameManager ?? throw new ArgumentNullException(nameof(nameManager));
        _worldManager = worldManager ?? throw new ArgumentNullException(nameof(worldManager));
        _lootPackRoller = lootPackRoller ?? throw new ArgumentNullException(nameof(lootPackRoller));
        _rewardPolicy = rewardPolicy ?? throw new ArgumentNullException(nameof(rewardPolicy));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _specialtyTradeProcessor = specialtyTradeProcessor;
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public void Load()
    {
        if (_loaded)
            return;

        var config = AppConfiguration.Instance.Butler?.HarvestCompletion;
        if (config is not { } || !config.IsValid())
            throw new InvalidDataException("Butler.HarvestCompletion configuration is invalid.");

        var interval = TimeSpan.FromSeconds(config.ScanIntervalSeconds);
        _taskManager.Schedule(new ButlerHarvestCompletionTask(this), interval, interval);
        _loaded = true;
    }

    public void ProcessDueJobs()
    {
        var config = AppConfiguration.Instance.Butler?.HarvestCompletion;
        if (config is not { } || !config.IsValid())
        {
            Logger.Error("Skipping Farmhand harvest completion: Butler.HarvestCompletion is invalid");
            return;
        }

        var now = _utcNow();
        var nowUnix = Helpers.UnixTime(now);
        foreach (var butler in _butlerManager.SnapshotAll())
        {
            try
            {
                ProcessButler(butler, config, now, nowUnix);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Farmhand harvest completion failed after commit for character {0}",
                    butler?.CharacterId);
            }
        }

        try
        {
            _specialtyTradeProcessor?.ProcessDueSpecialtyTradeJobs();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Farmhand specialty-trade completion pass failed");
        }
    }

    private void ProcessButler(CharacterButler butler, ButlerHarvestCompletionConfig config,
        DateTime now, long nowUnix)
    {
        if (butler == null)
            return;

        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (butler.IsDeleted || butler.HouseId == 0)
                    return;

                foreach (var capturedJob in butler.SnapshotHarvestJobs())
                {
                    if (!butler.HarvestJobs.TryGetValue(capturedJob.JobId, out var currentJob) ||
                        currentJob != capturedJob)
                        continue;

                    ProcessJobLocked(butler, currentJob, config, now, nowUnix);
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    private void ProcessJobLocked(CharacterButler butler, ButlerHarvestJob job,
        ButlerHarvestCompletionConfig config, DateTime now, long nowUnix)
    {
        if (!ButlerGameData.Instance.TryGetHarvest(job.StaticHarvestId, out var harvest) ||
            harvest.GrowthTime is null || harvest.RepeatCount is null || harvest.ItemId is null ||
            harvest.LootPackId is null || harvest.BonusRatio is null ||
            (config.BonusRatioScale != 0 && harvest.BonusRatio > 0 && harvest.BonusLootPackId is not > 0) ||
            !ButlerFarmingRules.TryCalculateCycleSeconds(
                harvest, CurrentHarvestGrowthTimeModifier, out var cycleSeconds, out _))
        {
            Logger.Error("Farmhand job {0} references invalid harvest content {1}", job.JobId, job.StaticHarvestId);
            return;
        }

        var currentJob = job;
        for (var completed = 0; completed < config.MaxCatchUpCyclesPerPass; completed++)
        {
            if (!ButlerHarvestCompletionRules.TryPlanNextCycle(
                    currentJob, harvest.RepeatCount.Value, cycleSeconds, nowUnix, out var plan))
                return;

            if (!TryCommitCycle(butler, currentJob, harvest, plan, config, now))
                return;
            if (plan.IsFinal)
                return;
            currentJob = plan.AdvancedJob;
        }
    }

    private bool TryCommitCycle(
        CharacterButler butler,
        ButlerHarvestJob expectedJob,
        ButlerHarvest harvest,
        ButlerHarvestCyclePlan plan,
        ButlerHarvestCompletionConfig config,
        DateTime now)
    {
        if (!TryRollRewards(harvest, expectedJob.RequestedAmount, config, out var rolledRewards,
                out var withBonus) ||
            !TryCreateRewardItems(rolledRewards, out var freshItems))
            return false;

        using var rewardScope = new FreshHarvestRewardScope(
            _mailManager.DiscardUnpersisted,
            _itemManager.GetItemByItemId,
            _itemManager.ReleaseId,
            freshItems);
        var receiverName = _nameManager.GetCharacterName(butler.CharacterId);
        if (string.IsNullOrWhiteSpace(receiverName))
        {
            Logger.Error("Cannot deliver Farmhand job {0}: character {1} has no registered name",
                expectedJob.JobId, butler.CharacterId);
            return false;
        }

        var mails = CreateRewardMails(
            butler.CharacterId, receiverName, harvest.ItemId!.Value, expectedJob.RequestedAmount,
            withBonus, freshItems, now);

        var currentExperience = butler.PermanentDatas.GetValueOrDefault(
            ButlerProgression.CumulativeExperiencePermanentDataKey);
        var resultingExperience = currentExperience;
        var experienceChanged = false;
        if (plan.IsFinal && !TryCalculateFinalExperience(
                harvest, expectedJob, config, currentExperience, out resultingExperience, out experienceChanged))
            return false;

        var committed = false;
        try
        {
            using var connection = _openConnection();
            using var transaction = connection.BeginTransaction();
            if (!_repository.TryInsertHarvestCompletion(
                    expectedJob.JobId, plan.CycleNumber, plan.AdvancedJob.UpdateTime, connection, transaction))
                return false;

            foreach (var mail in mails)
            {
                if (!_mailManager.TryDeliverOn(mail, connection, transaction))
                    throw new InvalidOperationException($"Could not stage Farmhand reward mail for job {expectedJob.JobId}.");
                rewardScope.MarkStaged(mail);
            }

            var changed = plan.IsFinal
                ? _repository.DeleteHarvestJob(butler.CharacterId, expectedJob.JobId, connection, transaction)
                : _repository.UpdateHarvestJob(
                    butler.CharacterId, plan.AdvancedJob, expectedJob.RemainingRepeatCount,
                    expectedJob.UpdateTime, connection, transaction);
            if (!changed)
                throw new InvalidOperationException($"Farmhand job {expectedJob.JobId} changed concurrently.");

            if (experienceChanged)
                _repository.SavePermanentData(
                    butler.CharacterId, ButlerProgression.CumulativeExperiencePermanentDataKey,
                    resultingExperience, connection, transaction);

            transaction.Commit();
            committed = true;
            rewardScope.MarkCommitted();

            if (plan.IsFinal)
            {
                if (!butler.RemoveHarvestJob(expectedJob.JobId))
                    throw new InvalidOperationException(
                        $"Committed Farmhand job {expectedJob.JobId} vanished from live state.");
            }
            else
            {
                butler.ApplyHarvestJob(plan.AdvancedJob);
            }

            if (experienceChanged)
                butler.ApplyPermanentData(
                    ButlerProgression.CumulativeExperiencePermanentDataKey, resultingExperience);

            PublishStatePackets(butler, harvest, plan, experienceChanged, resultingExperience);
            foreach (var mail in mails)
                _mailManager.PublishDelivered(mail);
            return true;
        }
        catch (Exception ex) when (!committed)
        {
            Logger.Error(ex, "Could not commit Farmhand harvest cycle {0} for job {1}",
                plan.CycleNumber, expectedJob.JobId);
            return false;
        }
    }

    private bool TryRollRewards(
        ButlerHarvest harvest,
        ushort requestedAmount,
        ButlerHarvestCompletionConfig config,
        out IReadOnlyList<LootPackReward> rewards,
        out bool withBonus)
    {
        var rolled = new List<LootPackReward>();
        withBonus = false;
        for (var unit = 0; unit < requestedAmount; unit++)
        {
            if (!_lootPackRoller.TryRoll(harvest.LootPackId!.Value, out var baseRewards))
            {
                rewards = [];
                return false;
            }
            rolled.AddRange(baseRewards);

            if (harvest.BonusLootPackId is not > 0 ||
                !_rewardPolicy.GrantsBonus(
                    harvest.BonusRatio.GetValueOrDefault(), CurrentHarvestBonusRatioModifier,
                    config.BonusRatioScale))
                continue;

            if (!_lootPackRoller.TryRoll(harvest.BonusLootPackId.Value, out var bonusRewards))
            {
                rewards = [];
                return false;
            }
            rolled.AddRange(bonusRewards);
            withBonus = true;
        }

        rewards = rolled;
        return true;
    }

    private bool TryCreateRewardItems(IReadOnlyList<LootPackReward> rewards, out IReadOnlyList<Item> items)
    {
        var created = new List<Item>();
        items = created;
        try
        {
            var totals = new Dictionary<(uint TemplateId, byte Grade), long>();
            foreach (var reward in rewards)
            {
                if (reward.ItemTemplateId == 0 || reward.Count <= 0)
                    throw new InvalidDataException("Farmhand loot pack returned an invalid reward.");
                var key = (reward.ItemTemplateId, reward.Grade);
                totals[key] = checked(totals.GetValueOrDefault(key) + reward.Count);
            }

            foreach (var ((templateId, grade), total) in totals)
            {
                var template = _itemManager.GetTemplate(templateId);
                if (template is not { MaxCount: > 0 })
                    throw new InvalidDataException($"Farmhand reward item {templateId} has no valid stack size.");

                var remaining = total;
                while (remaining > 0)
                {
                    var stackCount = (int)Math.Min(remaining, template.MaxCount);
                    var item = _itemManager.Create(templateId, stackCount, grade);
                    if (item == null)
                        throw new InvalidOperationException($"Could not create Farmhand reward item {templateId}.");
                    item.ExcludeFromWorldSave = true;
                    created.Add(item);
                    remaining -= stackCount;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not create Farmhand harvest rewards");
            foreach (var item in created)
                if (ReferenceEquals(_itemManager.GetItemByItemId(item.Id), item))
                    _itemManager.ReleaseId(item.Id);
            items = [];
            return false;
        }
    }

    private bool TryCalculateFinalExperience(
        ButlerHarvest harvest,
        ButlerHarvestJob job,
        ButlerHarvestCompletionConfig config,
        ulong currentExperience,
        out ulong resultingExperience,
        out bool changed)
    {
        resultingExperience = currentExperience;
        changed = false;
        if (config.ExperienceRate == 0d || job.LaborPowerForExperience == 0)
            return true;

        if (!ButlerGameData.Instance.TryGetUniqueTemplate(out var template) ||
            !ButlerGameData.Instance.TryGetLevelForCumulativeExperience(
                template.Id, currentExperience, out var currentLevel) ||
            !_rewardPolicy.TryCalculateExperienceAward(
                job.LaborPowerForExperience, currentLevel.Level, config.ExperienceRate,
                currentExperience, out _, out resultingExperience))
        {
            Logger.Error("Cannot calculate Farmhand XP for completed job {0}, harvest {1}",
                job.JobId, harvest.Id);
            return false;
        }

        changed = resultingExperience != currentExperience;
        return true;
    }

    private static IReadOnlyList<BaseMail> CreateRewardMails(
        uint characterId,
        string receiverName,
        uint itemType,
        ushort requestedAmount,
        bool withBonus,
        IReadOnlyList<Item> items,
        DateTime now)
    {
        var mails = new List<BaseMail>();
        if (items.Count == 0)
        {
            mails.Add(CreateMail(characterId, receiverName, itemType, requestedAmount, withBonus, [], now));
            return mails;
        }

        foreach (var batch in items.Chunk(MailBody.MaxMailAttachments))
            mails.Add(CreateMail(characterId, receiverName, itemType, requestedAmount, withBonus, batch, now));
        return mails;
    }

    internal static BaseMail CreateMail(
        uint characterId,
        string receiverName,
        uint itemType,
        ushort requestedAmount,
        bool withBonus,
        IReadOnlyList<Item> attachments,
        DateTime now)
    {
        var mail = new BaseMail
        {
            MailType = MailType.Butler,
            ReceiverName = receiverName,
            Title = "title",
            Header =
            {
                Status = MailStatus.Unread,
                SenderId = 0,
                SenderName = ".butlerHarvest",
                ReceiverId = characterId
            },
            Body =
            {
                Text = $"body({itemType}, {requestedAmount}, {withBonus.ToString().ToLowerInvariant()})",
                SendDate = now,
                RecvDate = now
            }
        };
        mail.Body.Attachments.AddRange(attachments);
        return mail;
    }

    private void PublishStatePackets(
        CharacterButler butler,
        ButlerHarvest harvest,
        ButlerHarvestCyclePlan plan,
        bool experienceChanged,
        ulong resultingExperience)
    {
        var owner = _worldManager.GetCharacterById(butler.CharacterId);
        if (owner == null)
            return;

        var advanced = plan.AdvancedJob;
        owner.SendPacket(new SCButlerHarvestUpdatedPacket(
            plan.IsFinal ? (byte)3 : (byte)2,
            0,
            advanced.JobId,
            new ButlerHarvestDataWire(
                harvest.Id,
                checked((short)advanced.RemainingRepeatCount),
                checked((short)advanced.RequestedAmount),
                advanced.LaborPowerForExperience,
                advanced.UpdateTime)));

        if (!experienceChanged)
            return;
        owner.SendPacket(new SCButlerInfoUpdatedPacket(
            0, 0x02, false, false, [],
            new Dictionary<sbyte, ulong>
            {
                [ButlerProgression.CumulativeExperiencePermanentDataKey] = resultingExperience
            },
            0, 0, 0, string.Empty, new Dictionary<uint, uint>()));
    }

    /// <summary>
    /// Owns only the fresh item IDs created for one cycle. Before commit it removes staged mail metadata
    /// and releases any still-live fresh IDs exactly once. After commit it deliberately performs no cleanup.
    /// </summary>
    internal sealed class FreshHarvestRewardScope(
        Action<BaseMail> discardUnpersisted,
        Func<ulong, Item> getLiveItem,
        Action<ulong> releaseItemId,
        IReadOnlyList<Item> freshItems) : IDisposable
    {
        private readonly List<BaseMail> _stagedMails = [];
        private bool _committed;
        private bool _disposed;

        public void MarkStaged(BaseMail mail) => _stagedMails.Add(mail);

        public void MarkCommitted() => _committed = true;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_committed)
                return;

            foreach (var mail in _stagedMails)
            {
                try
                {
                    discardUnpersisted(mail);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Could not discard staged Farmhand reward mail {0}", mail?.Id);
                }
            }
            foreach (var item in freshItems)
            {
                try
                {
                    if (item?.Id > 0 && ReferenceEquals(getLiveItem(item.Id), item))
                        releaseItemId(item.Id);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Could not release fresh Farmhand reward item {0}", item?.Id);
                }
            }
        }
    }
}
