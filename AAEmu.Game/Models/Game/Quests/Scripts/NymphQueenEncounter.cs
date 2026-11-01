using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Scripts;

/// <summary>
/// Owns the shared Lympheroda encounter, including its temporary dialogue NPC.
/// </summary>
internal sealed class NymphQueenEncounter : INpcEncounter
{
    internal const uint QuestId = 2008;
    internal const uint LympherodaId = 9385;
    internal const uint NepherodaId = 8993;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly object _sync;
    private readonly Npc _original;
    private readonly NpcSpawner _spawner;
    private readonly Character _owner;
    private readonly Dictionary<Character, Quest> _quests = [];
    private readonly IQuestManager _questManager;
    private readonly ITaskManager _taskManager;
    private readonly TimeProvider _clock;
    private readonly SharedQuestEncounter _state;
    private readonly EncounterTask _task;
    private readonly Action<Quest> _onQuestRemoving;
    private Npc _replacement;
    private bool _originalRemoved;
    private bool _resetApplied;
    private bool _restored;

    internal NymphQueenEncounter(Npc original, Character owner, IEnumerable<uint> allowedPlayers,
        IQuestManager questManager, ITaskManager taskManager, TimeProvider clock, NymphQueenEncounterConfig config)
    {
        _sync = original.EncounterSync;
        _original = original;
        _spawner = original.Spawner;
        _owner = owner;
        _questManager = questManager;
        _taskManager = taskManager;
        _clock = clock;
        _state = new SharedQuestEncounter(owner.Id, allowedPlayers, clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromSeconds(Math.Max(1, config.CombatInactivitySeconds)),
            TimeSpan.FromSeconds(Math.Max(1, config.DialogueSeconds)),
            TimeSpan.FromSeconds(Math.Max(1, config.ResetDelaySeconds)));
        _task = new EncounterTask(this);
        _onQuestRemoving = OnQuestRemoving;
        original.Encounter = this;
        _spawner.Encounter = this;
    }

    public static bool AcceptQuest(Character owner, Npc npc, Func<bool> accept,
        IQuestManager questManager, ITaskManager taskManager, ITeamManager teamManager,
        TimeProvider clock, NymphQueenEncounterConfig config)
    {
        if (npc.TemplateId != LympherodaId || npc.Spawner == null)
            return accept();

        lock (npc.EncounterSync)
        {
            if (npc.Encounter != null && npc.Encounter is not NymphQueenEncounter)
                return false;
            var encounter = npc.Encounter as NymphQueenEncounter;
            if (encounter == null)
            {
                var team = teamManager.GetTeamByObjId(owner.ObjId);
                var allowed = team?.Members.Where(m => m?.Character != null && m.Character.IsOnline &&
                    m.Character.ParentWorld == npc.ParentWorld &&
                    Vector3.Distance(m.Character.Transform.World.Position, npc.Transform.World.Position) <= 100)
                    .Select(m => m.Character.Id).ToArray() ?? [];
                encounter = new NymphQueenEncounter(npc, owner, allowed, questManager, taskManager, clock, config);
            }
            return encounter.TryAccept(owner, accept);
        }
    }

    internal static Npc FindQuestNpc(Character player, bool recoverMissing)
    {
        var world = player.ParentWorld;
        if (world == null)
            return null;
        if (player.CurrentTarget is Npc { TemplateId: LympherodaId } target &&
            ReferenceEquals(world.GetNpc(target.ObjId), target))
            return target;
        var npc = world.GetNpcByTemplateId(LympherodaId);
        if (npc != null || !recoverMissing)
            return npc;

        var spawner = world.SpawnManager.GetAllSpawners().Values.SelectMany(s => s)
            .Where(s => s.UnitId == LympherodaId && s.Template != null)
            .OrderBy(s => Vector3.DistanceSquared(s.Position.AsPositionVector(), player.Transform.World.Position))
            .FirstOrDefault();
        return spawner?.RestoreQuestNpc();
    }

    public void QuestRemoved(Character player, Quest quest)
    {
        lock (_sync)
        {
            if (!_quests.TryGetValue(player, out var current) || !ReferenceEquals(current, quest))
                return;
            UpdateCompletions();
            _quests.Remove(player);
            _state.RemoveParticipant(player.Id);
            Unsubscribe(quest);
            if (player == _owner && !_state.OwnerCompleted)
                BeginReset();
        }
    }

    internal bool TryAccept(Character player, Func<bool> accept)
    {
        lock (_sync)
        {
            if (!_state.TryJoin(player.Id))
            {
                player.SendMessage("The Nymph Queen is already facing another challenger.");
                return false;
            }

            if (!accept())
            {
                _state.RemoveParticipant(player.Id);
                if (player == _owner)
                    Release();
                return false;
            }

            _quests.Add(player, player.Quests.ActiveQuests[QuestId]);
            _quests[player].Removing += _onQuestRemoving;
            if (player == _owner)
                _taskManager.Schedule(_task, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Logger.Info($"Nymph Queen encounter accepted: owner={_owner.Id}, player={player.Id}, npc={_original.ObjId}");
            return true;
        }
    }

    public bool CanDamage(BaseUnit attacker)
    {
        var player = attacker as Character ?? attacker?.GetOwnerCharacter();
        lock (_sync)
            return player != null && _state.CanDamage(player.Id);
    }

    public int AdjustDamage(BaseUnit attacker, int amount)
    {
        lock (_sync)
        {
            if (!CanDamage(attacker))
                return 0;
            // Surrender at half health, including hits that would otherwise be lethal.
            amount = Math.Min(amount, Math.Max(0, _original.Hp - Math.Max(1, _original.MaxHp / 2)));
            RecordDamage(attacker, amount);
            return amount;
        }
    }

    public void RecordDamage(BaseUnit attacker, int amount)
    {
        if (amount <= 0)
            return;
        var player = attacker as Character ?? attacker?.GetOwnerCharacter();
        lock (_sync)
        {
            if (player != null)
                _state.RecordDamage(player.Id, _clock.GetUtcNow().UtcDateTime);
        }
    }

    public bool CanTalk(uint sourceId, uint recipientId)
    {
        lock (_sync)
            return _state.CanTalk(sourceId) && _state.CanTalk(recipientId);
    }

    public void AttachReplacement(Npc npc)
    {
        lock (_sync)
        {
            if (npc.TemplateId != NepherodaId || !_state.BeginDialogue(_clock.GetUtcNow().UtcDateTime))
                return;
            _replacement = npc;
            npc.Encounter = this;
            Logger.Info($"Nymph Queen dialogue started: owner={_owner.Id}, npc={npc.ObjId}");
        }
    }

    public void Retreat(Npc npc)
    {
        lock (_sync)
        {
            if (npc != _original || _originalRemoved)
                return;
            if (_replacement == null)
            {
                BeginReset();
                return;
            }
            foreach (var (player, quest) in _quests.ToArray())
            {
                if (IsCurrentQuest(player, quest) && player.IsOnline && player.ParentWorld == _original.ParentWorld &&
                    Vector3.Distance(player.Transform.World.Position, _original.Transform.World.Position) <= 100)
                    _questManager.DoOnMonsterHuntEvents(player, npc);
            }
            RemoveOriginal();
        }
    }

    public bool AbandonCombat()
    {
        lock (_sync)
        {
            if (_state.CurrentStage == SharedQuestEncounter.Stage.Combat)
            {
                BeginReset();
                return true;
            }
            return false;
        }
    }

    internal void Tick()
    {
        lock (_sync)
        {
            if (_restored)
                return;
            UpdateCompletions();
            var ownerAvailable = !_owner.IsDead &&
                _owner.ParentWorld == _original.ParentWorld &&
                _quests.TryGetValue(_owner, out var quest) && IsCurrentQuest(_owner, quest);
            _state.Tick(_clock.GetUtcNow().UtcDateTime, ownerAvailable);
            if (_state.CurrentStage == SharedQuestEncounter.Stage.Resetting)
                ApplyReset();
            if (_state.CurrentStage == SharedQuestEncounter.Stage.Finished)
            {
                _restored = true;
                Release(true);
                Logger.Info($"Nymph Queen encounter restored: owner={_owner.Id}, spawner={_spawner.SpawnerId}");
            }
        }
    }

    private static bool IsCurrentQuest(Character player, Quest quest) =>
        player.Quests.ActiveQuests.TryGetValue(QuestId, out var active) && ReferenceEquals(active, quest);

    private void UpdateCompletions()
    {
        foreach (var (player, quest) in _quests)
        {
            if (HasCompletedConversation(quest))
                _state.Complete(player.Id);
        }
    }

    private static bool HasCompletedConversation(Quest quest)
    {
        if (quest.Step is QuestComponentKind.Ready or QuestComponentKind.Reward)
            return true;
        if (!quest.QuestSteps.TryGetValue(QuestComponentKind.Progress, out var progress))
            return false;
        foreach (var component in progress.Components.Values)
        {
            foreach (var act in component.Acts)
            {
                if (act.Template is QuestActObjTalk { NpcId: NepherodaId } && act.GetObjective(quest) > 0)
                    return true;
            }
        }
        return false;
    }

    internal static void RecoverInterruptedQuest(Quest quest)
    {
        if (quest.TemplateId != QuestId || HasCompletedConversation(quest))
            return;
        if (quest.Step is QuestComponentKind.Start or QuestComponentKind.Supply or QuestComponentKind.Progress)
        {
            quest.Step = QuestComponentKind.Fail;
            quest.Status = QuestStatus.Failed;
        }
    }

    private void BeginReset()
    {
        _state.BeginReset(_clock.GetUtcNow().UtcDateTime);
        ApplyReset();
    }

    private void ApplyReset()
    {
        if (_resetApplied)
            return;
        _resetApplied = true;
        var failed = _state.UnfinishedParticipants.ToHashSet();
        foreach (var (player, quest) in _quests.ToArray())
        {
            if (failed.Contains(player.Id) && IsCurrentQuest(player, quest))
                FailParticipant(player, quest);
        }
        RemoveOriginal();
        if (_replacement != null)
        {
            _replacement.Encounter = null;
            _replacement.Spawner.Despawn(_replacement);
            _replacement = null;
        }
        Logger.Info($"Nymph Queen encounter resetting: owner={_owner.Id}, failed={string.Join(",", failed)}");
    }

    private void FailParticipant(Character player, Quest quest)
    {
        _questManager.FailQuest(player, QuestId);
        quest.Status = QuestStatus.Failed;
        player.SendPacket(new SCQuestContextUpdatedPacket(quest, 0));
    }

    private void RemoveOriginal()
    {
        if (_originalRemoved)
            return;
        _originalRemoved = true;
        _original.Buffs?.RemoveAllEffects();
        _spawner.Despawn(_original);
    }

    private void Release(bool respawn = false)
    {
        _taskManager.Cancel(_task);
        _original.Encounter = null;
        _spawner.CompleteEncounter(this, respawn);
        foreach (var (player, quest) in _quests)
            Unsubscribe(quest);
        _quests.Clear();
    }

    private void OnQuestRemoving(Quest quest)
    {
        if (quest.Owner is Character player)
            QuestRemoved(player, quest);
    }

    private void Unsubscribe(Quest quest)
    {
        quest.Removing -= _onQuestRemoving;
    }

    private sealed class EncounterTask(NymphQueenEncounter encounter) : AAEmu.Game.Models.Tasks.Task
    {
        public override void Execute() => encounter.Tick();
    }
}
