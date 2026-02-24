using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

/// <summary>
/// Represents the idle state for an NPC. Handles skill usage, state transitions, and default behaviors.
/// </summary>
public class IdleBehavior : BaseCombatBehavior
{
    // -------------------- configurable --------------------
    private const float GreetTimer = 5f;         // minutes
    private const float GreetRange = 5f;         // metres
    private const double GreetFovScale = 0.6667; // 120.0 / 180.0 for IsFront
    // ------------------------------------------------------
    private static readonly TimeSpan GreetCooldown = TimeSpan.FromMinutes(GreetTimer);
    private readonly Dictionary<uint, DateTime> _greeted = new();
    // ------------------------------------------------------

    private bool _isInitialized;

    public override void Enter()
    {
        if (!Validate())
            return;

        InitializeIdleState();
        _isInitialized = true;
        //Logger.Debug($"Unit {Ai.Owner.ObjId}:{Ai.Owner.TemplateId} entered idle state");
    }

    private void InitializeIdleState()
    {
        // Stop all actions and reset state
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.SetTarget(null);

        if (Ai.Owner.CurrentTarget != null)
            Ai.Owner.SendPacketToPlayers([Ai.Owner.CurrentTarget], new SCAggroTargetChangedPacket(Ai.Owner.ObjId, 0));

        if (Ai.Owner is { } npc)
            npc.Events.InIdle(this, new InIdleArgs { Owner = npc });
    }

    public override void Tick(TimeSpan delta)
    {
        if (!ValidateTickState())
            return;

        if (!Validate() || !Throttle())
            return;

        ProcessTickActions();
    }

    private bool ValidateTickState()
    {
        if (!_isInitialized)
        {
            Logger.Warn($"IdleBehavior.Tick called before initialization for unit {Ai?.Owner?.ObjId}");
            return false;
        }

        return true;
    }

    private void ProcessTickActions()
    {
        // Try to use a skill if possible
        TryUseIdleSkill();

        // Check for state transitions
        if (CheckAggression())
        {
            Ai.GoToCombat();
        }
        else if (CheckAlert())
        {
            Ai.GoToAlert();
        }
        else if (CheckFollowPath())
        {
            Ai.GoToFollowPath();
        }
        else
        {
            // Try to follow the nearest NPC, otherwise go to default behavior
            if (Ai.DoFollowDefaultNearestNpc())
                return;

            Ai.GoToDefaultBehavior();
        }

        // Talk has low priority
        CheckForTalk();
    }

    private void TryUseIdleSkill()
    {
        if (Ai.Owner.CurrentTarget != null)
        {
            var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
            PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner, targetDist);
        }
    }

    private void CheckForTalk()
    {
        if (Ai.GetCurrentBehavior() is TalkBehavior) return;

        var now = DateTime.UtcNow;
        var playersInRange = GetPlayersInRange(Ai.Owner, GreetRange, GreetFovScale, _greeted, GreetCooldown);

        // greet new / cooled-down players
        foreach (var player in playersInRange)
        {
            if (!_greeted.TryGetValue(player.ObjId, out var lastTime) || now - lastTime >= GreetCooldown)
                _greeted[player.ObjId] = now;
        }

        // remove players that left or already greeted long ago
        var toRemove = _greeted.Keys
            .Where(id => playersInRange.All(p => p.ObjId != id) && now - _greeted[id] >= GreetCooldown)
            .ToList();
        toRemove.ForEach(id => _greeted.Remove(id));

        if (playersInRange.Count != 0)
            Ai.GoToTalk();
    }

    public override void Exit()
    {
        if (!_isInitialized || Ai?.Owner == null)
            return;

        _isInitialized = false;
    }
}
