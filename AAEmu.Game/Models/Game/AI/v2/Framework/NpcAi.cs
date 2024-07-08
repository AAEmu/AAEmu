using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// This is the basics of a unit's AI: The state machine. It also carries data about which unit owns it
/// </summary>
public abstract class NpcAi
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Test
    public bool ShouldTick { get; set; }
    public bool AlreadyTargetted { get; set; }

    public Npc Owner { get; set; }
    public Transform IdlePosition { get; set; }
    public AiParams Param { get; set; }
    public PathNode PathNode { get; set; }

    private Dictionary<BehaviorKind, Behavior> _behaviors;
    private Dictionary<Behavior, List<Transition>> _transitions;
    private Behavior _currentBehavior;
    public DateTime _nextAlertCheckTime = DateTime.MinValue;
    public DateTime _alertEndTime = DateTime.MinValue;

    /// <summary>
    /// A list of AiCommands that should take priority over any other behavior
    /// </summary>
    private Queue<AiCommands> AiCommandsQueue { get; set; } = new();

    /// <summary>
    /// Currently executing command
    /// </summary>
    private AiCommands AiCurrentCommand { get; set; } = null;

    /// <summary>
    /// Time that AiCurrentCommand started
    /// </summary>
    public DateTime AiCurrentCommandStartTime { get; set; } = DateTime.MinValue;
    // Persistent arguments for AiCommands queue
    private string AiFileName { get; set; } = string.Empty;
    private string AiFileName2 { get; set; } = string.Empty;
    private uint AiSkillId { get; set; }
    private uint AiTimeOut { get; set; }
    private TimeSpan AiCurrentCommandRunTime { get; set; } = TimeSpan.Zero;

    public NpcAi()
    {
        _behaviors = new Dictionary<BehaviorKind, Behavior>();
        _transitions = new Dictionary<Behavior, List<Transition>>();
        PathNode = new PathNode();
    }

    public void Start()
    {
        Build();
        CheckValid();
        // GoToSpawn();
    }

    protected abstract void Build();

    private void CheckValid()
    {
        foreach (var transition in _transitions.Values.SelectMany(transitions =>
                     transitions.Where(transition => !_behaviors.ContainsKey(transition.Kind))))
        {
            Logger.Error("Transition is invalid. Type {0} missing, while used in transition on {1}",
                transition.Kind.GetType().Name, transition.On);
        }
    }

    protected Behavior AddBehavior(BehaviorKind kind, Behavior behavior)
    {
        behavior.Ai = this;
        _behaviors.Add(kind, behavior);
        return behavior;
    }

    public Behavior GetCurrentBehavior()
    {
        return _currentBehavior;
    }

    private Behavior GetBehavior(BehaviorKind kind)
    {
        return !_behaviors.ContainsKey(kind) ? null : _behaviors[kind];
    }

    private void SetCurrentBehavior(Behavior behavior)
    {
        Logger.Trace(
            $"Npc {Owner.TemplateId}:{Owner.ObjId} leaving behavior {_currentBehavior?.GetType().Name ?? "none"}, Entering behavior {behavior?.GetType().Name ?? "none"}");
        _currentBehavior?.Exit();
        _currentBehavior = behavior;
        _currentBehavior?.Enter();
    }

    protected void SetCurrentBehavior(BehaviorKind kind)
    {
        if (!_behaviors.ContainsKey(kind))
        {
            Logger.Trace(
                $"Trying to set Npc {Owner.TemplateId}:{Owner.ObjId} current behavior, but it is not valid. Missing behavior: {kind}");
            return;
        }

        Logger.Trace($"Set Npc {Owner.TemplateId}:{Owner.ObjId} current behavior: {kind}");
        SetCurrentBehavior(_behaviors[kind]);
    }

    public Behavior AddTransition(Behavior source, Transition target)
    {
        if (!_transitions.ContainsKey(source))
            _transitions.Add(source, new List<Transition>());
        _transitions[source].Add(target);
        return source;
    }

    public void Tick(TimeSpan delta)
    {
        /*if ((!Owner?.Region?.IsEmpty() ?? false)
            || (Owner?.Region?.AreNeighborsEmpty() ?? false))*/
        if (Owner?.Region?.HasPlayerActivity() ?? false)
        {
            // If there are commands in the AI Command queue, execute those first
            if ((AiCurrentCommand != null) || (AiCommandsQueue.Count > 0))
            {
                if (AiCurrentCommand == null)
                {
                    AiCurrentCommand = AiCommandsQueue.Dequeue();
                    AiCurrentCommandStartTime = DateTime.UtcNow;
                }

                TickCurrentAiCommand(AiCurrentCommand, delta);
                return;
            }

            _currentBehavior?.Tick(delta);

            // If aggro table is populated, check if current aggro targets need to be cleared
            if (Owner?.AggroTable.Count <= 0)
            {
                if (Owner.IsDead || GetCurrentBehavior() is DeadBehavior)
                    return;

                OnNoAggroTarget();
                return;
            }

            var toRemove = new List<Unit>();
            foreach (var (id, aggro) in Owner.AggroTable)
                if (aggro.Owner.Buffs.CheckBuffTag((uint)TagsEnum.NoFight) ||
                    aggro.Owner.Buffs.CheckBuffTag((uint)TagsEnum.Returning) ||
                    !Owner.CanAttack(aggro.Owner))
                    toRemove.Add(aggro.Owner);

            if (toRemove.Count <= 0)
                return;

            foreach (var unitToRemove in toRemove)
                Owner.ClearAggroOfUnit(unitToRemove);
        }
    }

    private void Transition(TransitionEvent on)
    {
        if (!_transitions.ContainsKey(_currentBehavior))
            return;
        var transition = _transitions[_currentBehavior].SingleOrDefault(t => t.On == on);
        if (transition == null)
            return;

        var newBehavior = GetBehavior(transition.Kind);
        SetCurrentBehavior(newBehavior);
    }

    #region Events

    public void OnNoAggroTarget()
    {
        Transition(TransitionEvent.OnNoAggroTarget);
    }

    public void OnAggroTargetChanged()
    {
        Transition(TransitionEvent.OnAggroTargetChanged);
    }

    #endregion

    /// <summary>
    /// These appear to be ways to force a state change, ignoring existing transitions. 
    /// </summary>

    #region Go to X

    public virtual void GoToSpawn()
    {
        SetCurrentBehavior(BehaviorKind.Spawning);
    }

    public virtual void GoToIdle()
    {
        SetCurrentBehavior(BehaviorKind.Idle);
    }

    public virtual void GoToRunCommandSet()
    {
        SetCurrentBehavior(BehaviorKind.RunCommandSet);
    }

    public virtual void GoToTalk()
    {
        SetCurrentBehavior(BehaviorKind.Talk);
    }

    public virtual void GoToAlert()
    {
        SetCurrentBehavior(BehaviorKind.Alert);
    }

    public virtual void GoToCombat()
    {
        SetCurrentBehavior(BehaviorKind.Attack);
    }

    public virtual void GoToFollowPath()
    {
        SetCurrentBehavior(BehaviorKind.FollowPath);
    }

    public virtual void GoToFollowUnit()
    {
        SetCurrentBehavior(BehaviorKind.FollowUnit);
    }

    public virtual void GoToReturn()
    {
        SetCurrentBehavior(BehaviorKind.ReturnState);
    }

    public virtual void GoToDead()
    {
        SetCurrentBehavior(BehaviorKind.Dead);
    }

    public virtual void GoToDespawn()
    {
        SetCurrentBehavior(BehaviorKind.Despawning);
    }

    #endregion

    public void EnqueueAiCommands(List<AiCommands> aiCommandsList)
    {
        foreach (var aiCommand in aiCommandsList)
            AiCommandsQueue.Enqueue(aiCommand);
    }

    private void TickCurrentAiCommand(AiCommands aiCommand, TimeSpan delta)
    {
        if (AiCurrentCommandRunTime < TimeSpan.Zero)
        {
            AiCurrentCommand = null;
            AiCurrentCommandRunTime = TimeSpan.Zero;
            return;
        }


        // Check if we're still waiting
        if (AiCurrentCommandRunTime > TimeSpan.Zero)
        {
            AiCurrentCommandRunTime -= delta;
            return;
        }

        switch (aiCommand.CmdId)
        {
            case AiCommandCategory.FollowUnit:
                break;
            case AiCommandCategory.FollowPath:
                if (string.IsNullOrEmpty(AiFileName))
                {
                    AiFileName = aiCommand.Param2;
                }
                else
                {
                    AiFileName2 = aiCommand.Param2;
                }

                break;
            case AiCommandCategory.UseSkill:
                AiSkillId = aiCommand.Param1;
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(AiSkillId);
                if (skillTemplate != null && Owner.UseSkill(AiSkillId, Owner.CurrentTarget as Unit ?? Owner) == SkillResult.Success)
                {
                    AiCurrentCommandRunTime = TimeSpan.FromMilliseconds(skillTemplate.CooldownTime);
                }
                break;
            case AiCommandCategory.Timeout:
                AiTimeOut = aiCommand.Param1;
                AiCurrentCommandRunTime = TimeSpan.FromMilliseconds(AiTimeOut);
                break;
            default:
                throw new NotSupportedException(nameof(aiCommand.CmdId));
        }

        if (!string.IsNullOrEmpty(AiFileName))
        {
            if (Owner.IsInPatrol) { return; }

            Owner.IsInPatrol = true;
            Owner.Simulation.RunningMode = false;
            Owner.Simulation.Cycle = false;
            Owner.Simulation.MoveToPathEnabled = false;
            Owner.Simulation.MoveFileName = AiFileName;
            Owner.Simulation.MoveFileName2 = AiFileName2;
            Owner.Simulation.GoToPath(Owner, true, AiSkillId, AiTimeOut);
        }

        if (AiCurrentCommandRunTime == TimeSpan.Zero)
            AiCurrentCommandRunTime = TimeSpan.FromSeconds(-1);
    }

}
