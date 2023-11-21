﻿using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Models.Game.AI.AStar;
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
    public Transform IdlePosition { get; set; }
    public AiParams Param { get; set; }
    public PathNode PathNode { get; set; }

    private Dictionary<BehaviorKind, Behavior> _behaviors;
    private Dictionary<Behavior, List<Transition>> _transitions;
    private Behavior _currentBehavior;

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
        foreach (var transition in _transitions.Values.SelectMany(transitions => transitions.Where(transition => !_behaviors.ContainsKey(transition.Kind))))
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
        //Logger.Trace($"Npc {Owner.Name}:{Owner.ObjId} leaving behavior {_currentBehavior?.GetType().Name ?? "none"}, Entering behavior {behavior?.GetType().Name ?? "none"}");
        _currentBehavior?.Exit();
        _currentBehavior = behavior;
        _currentBehavior?.Enter();
    }

    protected void SetCurrentBehavior(BehaviorKind kind)
    {
        if (!_behaviors.ContainsKey(kind))
        {
            Logger.Trace($"Trying to set Npc {Owner.Name}:{Owner.ObjId} current behavior, but it is not valid. Missing behavior: {kind}");
            return;
        }

        //Logger.Trace($"Set Npc {Owner.Name}:{Owner.ObjId} current behavior: {kind}");
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
}
