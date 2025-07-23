using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

/// <summary>
/// NPC stays in place, greets nearby players and may follow default NPC if configured.
/// Transitions to TalkBehavior on first close player, or to combat/alert when threatened.
/// </summary>
public class HoldPositionBehavior : BaseCombatBehavior
{
    // -------------------- configurable --------------------
    private const float SkillCheckInterval = 1.0f; // 1 s between idle-skill tries
    private const float GreetTimer = 5f;           // minutes
    private const float GreetRange = 5f;           // metres
    private const double GreetFovScale = 0.6667;   // 120.0 / 180.0 for IsFront
    // ------------------------------------------------------
    private static readonly TimeSpan GreetCooldown = TimeSpan.FromMinutes(GreetTimer);
    private readonly Dictionary<uint, DateTime> _greeted = new();
    // ------------------------------------------------------

    private DateTime _lastSkillCheck;
    private bool _isInitialized;
    private bool _isFollowingNpc;

    public override void Enter()
    {
        if (!Validate())
            return;

        InitializeHoldPosition();
        _isInitialized = true;
    }

    private void InitializeHoldPosition()
    {
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;

        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.CurrentTarget = Ai.Owner;

        //_lastTick        = DateTime.UtcNow;
        _lastSkillCheck = DateTime.UtcNow;
        _isFollowingNpc = false;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!ValidateTickState())
            return;

        if (!Validate() || !Throttle())
            return;

        // 1. Combat states
        if (CheckCombatStates())
            return;

        // 2. Try to use idle self-skills
        ProcessSkillUsage();

        // 3. Optional follow default NPC
        if (!_isFollowingNpc)
            ProcessNpcFollowing();

        // 4. Talk has low priority
        CheckForTalk();
    }

    private bool ValidateTickState()
    {
        if (!_isInitialized)
        {
            Logger.Warn($"HoldPositionBehavior.Tick called before initialization for unit {Ai?.Owner?.ObjId}");
            return false;
        }
        return true;
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

    private bool CheckCombatStates()
    {
        if (CheckAggression())
        {
            _isFollowingNpc = false;
            return true;
        }

        if (CheckAlert())
        {
            _isFollowingNpc = false;
            return true;
        }

        return false;
    }

    private void ProcessSkillUsage()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSkillCheck).TotalSeconds < SkillCheckInterval)
            return;

        _lastSkillCheck = now;

        if (Ai.Owner.CurrentTarget != null)
        {
            var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
            PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner, targetDist);
        }
    }

    private void ProcessNpcFollowing()
    {
        if (Ai.DoFollowDefaultNearestNpc())
            _isFollowingNpc = true;
    }

    public override void Exit()
    {
        if (!_isInitialized || Ai?.Owner == null)
            return;

        _isInitialized = false;
        _isFollowingNpc = false;
    }
}
