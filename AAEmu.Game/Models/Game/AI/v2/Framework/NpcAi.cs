using System;
using System.Collections.Generic;

using System.Linq;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
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
    public bool AlreadyTargeted { get; set; }

    public Npc Owner { get; init; }
    public Vector3 IdlePosition { get; set; }
    public Vector3 HomePosition { get; set; }
    public AiParams Param { get; set; }
    public PathNode PathNode { get; set; }

    private readonly Dictionary<BehaviorKind, Behavior> _behaviors;
    private readonly Dictionary<Behavior, List<Transition>> _transitions;
    private Behavior _currentBehavior;
    private Behavior _defaultBehavior;
    public DateTime _nextAlertCheckTime = DateTime.MinValue;
    public DateTime _alertEndTime = DateTime.MinValue;

    #region ai_commands
    /// <summary>
    /// A list of AiCommands that should take priority over any other behavior
    /// </summary>
    public Queue<AiCommands> AiCommandsQueue { get; set; } = new();

    /// <summary>
    /// Currently executing command
    /// </summary>
    public AiCommands AiCurrentCommand { get; set; }

    /// <summary>
    /// Time that AiCurrentCommand started
    /// </summary>
    public DateTime AiCurrentCommandStartTime { get; set; } = DateTime.MinValue;
    public TimeSpan AiCurrentCommandRunTime { get; set; } = TimeSpan.Zero;
    #endregion ai_commands

    public AiPathHandler PathHandler { get; set; }

    public Unit AiFollowUnitObj { get; set; }

    // Persistent arguments for AiCommands queue
    public string AiFileName { get; set; } = string.Empty;
    public string AiFileName2 { get; set; } = string.Empty;
    public uint AiSkillId { get; set; }
    public uint AiTimeOut { get; set; }

    protected NpcAi()
    {
        _behaviors = [];
        _transitions = [];
        PathNode = new PathNode();
        PathHandler = new AiPathHandler(this);
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
            Logger.Error($"Transition is invalid. Type {transition.Kind.GetType().Name} missing, while used in transition on {transition.On}");
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
        return _behaviors.GetValueOrDefault(kind);
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
        if (!_behaviors.TryGetValue(kind, out var value))
        {
            Logger.Trace(
                $"Trying to set Npc {Owner.TemplateId}:{Owner.ObjId} current behavior, but it is not valid. Missing behavior: {kind}");
            return;
        }

        Logger.Trace($"Set Npc {Owner.TemplateId}:{Owner.ObjId} current behavior: {kind}");
        SetCurrentBehavior(value);
    }

    public Behavior AddTransition(Behavior source, Transition target)
    {
        if (!_transitions.ContainsKey(source))
            _transitions.Add(source, []);
        _transitions[source].Add(target);
        return source;
    }

    public void Tick(TimeSpan delta)
    {
        /*if ((!Owner?.Region?.IsEmpty() ?? false)
            || (Owner?.Region?.AreNeighborsEmpty() ?? false))*/
        if (HasPersistentAi() || (Owner?.Region?.HasPlayerActivity() ?? false))
        {
            _currentBehavior?.Tick(delta);

            // If aggro table is populated, check if current aggro targets need to be cleared
            if (Owner?.AggroTable.Count <= 0)
            {
                if (Owner.IsDead || GetCurrentBehavior() is DeadBehavior)
                    return;

                OnNoAggroTarget();
                return;
            }

            if (Owner != null)
            {
                var toRemove = new List<Unit>();
                foreach (var (_, aggro) in Owner.AggroTable)
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
    }

    private bool HasPersistentAi()
    {
        return PathHandler.AiPathPoints.Count > 0 ||
               PathHandler.AiPathPointsRemaining.Count > 0 ||
               AiFollowUnitObj != null ||
               AiCommandsQueue.Count > 0;
    }

    private void Transition(TransitionEvent on)
    {
        if (_currentBehavior == null) return;
        if (!_transitions.TryGetValue(_currentBehavior, out var transitionList)) return;
        var transition = transitionList.SingleOrDefault(t => t.On == on);
        if (transition == null) return;
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

    public virtual void GoToDefaultBehavior()
    {
        if (_defaultBehavior != null)
            SetCurrentBehavior(_defaultBehavior);
    }

    #endregion

    /// <summary>
    /// Adds a list of AI commands to the execution Queue and goes to the RunCommandSet behavior if there are items in the queue
    /// </summary>
    /// <param name="aiCommandsList">List of commands</param>
    /// <param name="addOnly">If true, will not go to the RunCommandSet behavior</param>
    public void EnqueueAiCommands(IEnumerable<AiCommands> aiCommandsList, bool addOnly = false)
    {
        foreach (var aiCommand in aiCommandsList)
            AiCommandsQueue.Enqueue(aiCommand);
        if (addOnly)
            return;
        if (AiCommandsQueue.Count > 0)
            GoToRunCommandSet();
    }

    public virtual void GoToDummy()
    {
        SetCurrentBehavior(BehaviorKind.Dummy);
    }

    public Dictionary<BehaviorKind, Behavior> GetAiBehaviorList()
    {
        return _behaviors;
    }

    public void SetDefaultBehavior(Behavior behavior)
    {
        _defaultBehavior = behavior;
    }

    public bool LoadAiPathPoints(string aiPathFileName, bool addToQueueOnly)
    {
        var points = AiPathsManager.Instance.LoadAiPathPoints(aiPathFileName);
        if (points.Count <= 0)
            return false;

        if (!addToQueueOnly)
        {
            PathHandler.AiPathPoints.Clear();
            PathHandler.AiPathLooping = true; // Set to true to trigger initial loading, need to enable it again in the .path file
            PathHandler.AiPathPoints.AddRange(points);
        }
        else
        {
            foreach (var point in points)
            {
                PathHandler.AiPathPointsRemaining.Enqueue(point);
            }
        }

        return true;
    }

    public bool DoFollowDefaultNearestNpc()
    {
        if (Owner.Spawner?.FollowNpc > 0)
        {
            return DoFollowNearestNpc(Owner.Spawner.FollowNpc, 100f);
        }
        return false;
    }

    public bool DoFollowNearestNpc(uint followNpc, float maxRange)
    {
        var nearbyNpcs = WorldManager.GetAround<Npc>(Owner, maxRange, true).ToList();
        Npc nearestNpc = null;
        var closestDistance = maxRange;
        foreach (var n in nearbyNpcs)
        {
            if (n.TemplateId != followNpc)
                continue;
            var dist = n.GetDistanceTo(Owner);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                nearestNpc = n;
            }
        }

        if (nearestNpc != null)
        {
            AiFollowUnitObj = nearestNpc;
            GoToFollowUnit();
            return true;
        }

        return false;
    }

    public double GetRealMovementSpeed(double baseMoveSpeed)
    {
        var speedMul = (Owner.CalculateWithBonuses(0, UnitAttribute.MoveSpeedMul) / 1000.0) + 1.0;
        if (Math.Abs(speedMul - 1.0) > double.Epsilon)
            baseMoveSpeed *= speedMul;

        return baseMoveSpeed;
    }

    public byte GetRealMovementFlags(double moveSpeed)
    {
        // 3 = Stand still
        // 4 = Run
        // 5 = Walk
        return (byte)(moveSpeed < 0.1 ? 3 : moveSpeed < 2.0 ? 5 : 4);
    }
}
