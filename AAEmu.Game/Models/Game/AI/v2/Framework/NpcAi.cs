using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.NPChar;
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
    public AiCommands AiCurrentCommand { get; set; } = null;

    /// <summary>
    /// Time that AiCurrentCommand started
    /// </summary>
    public DateTime AiCurrentCommandStartTime { get; set; } = DateTime.MinValue;
    public TimeSpan AiCurrentCommandRunTime { get; set; } = TimeSpan.Zero;
    #endregion ai_commands

    /// <summary>
    /// Loaded AI Path Points
    /// </summary>
    public List<AiPathPoint> AiPathPoints { get; set; } = new();

    /// <summary>
    /// Queue of locations to go to next
    /// </summary>
    public Queue<AiPathPoint> AiPathPointsRemaining { get; set; } = new();

    public bool AiPathLooping { get; set; } = true; // Needs to be set to true to trigger initial loading into queue
    /// <summary>
    /// Speed multiplier when moving on the Path
    /// </summary>
    public float AiPathSpeed { get; set; } = 1f;

    /// <summary>
    /// Stance to use when moving on the Path
    /// </summary>
    public byte AiPathStanceFlags { get; set; } = 4;

    // Persistent arguments for AiCommands queue
    public string AiFileName { get; set; } = string.Empty;
    public string AiFileName2 { get; set; } = string.Empty;
    public uint AiSkillId { get; set; }
    public uint AiTimeOut { get; set; }

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

    public bool LoadAiPathPoints(string aiPathFileName)
    {
        try
        {
            var fullPathFileName = Path.Combine("Data", "Path", aiPathFileName + ".path");
            if (!File.Exists(fullPathFileName))
                return false;

            var lines = File.ReadAllLines(fullPathFileName);

            AiPathPoints.Clear();
            AiPathLooping = true; // Set to true to trigger initial loading, need to enable it again in the .path file
            foreach (var line in lines)
            {
                var columns = line.Split('|');
                if (columns.Length != 5)
                    continue;
                if (!float.TryParse(columns[1], out var X))
                    X = 0f;
                if (!float.TryParse(columns[2], out var Y))
                    Y = 0f;
                if (!float.TryParse(columns[3], out var Z))
                    Z = 0f;
                var param = columns[4];

                if (!Enum.TryParse<AiPathPointAction>(columns[0], true, out var action))
                    action = AiPathPointAction.None;
                
                AiPathPoints.Add(new AiPathPoint()
                {
                    Position = new Vector3(X, Y, Z),
                    Action = action,
                    Param = param
                });
            }
        }
        catch (Exception e)
        {
            Logger.Error($"LoadAiPathPoint({aiPathFileName}), Exception: {e.Message}");
            return false;
        }

        return true;
    }
}
