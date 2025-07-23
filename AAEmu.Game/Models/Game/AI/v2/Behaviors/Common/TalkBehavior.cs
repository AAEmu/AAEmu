using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

/// <summary>
/// Behavior that greets nearby players once and unconditionally exits after 5 min.
/// </summary>
public class TalkBehavior : BaseCombatBehavior
{
    // -------------------- configurable --------------------
    private const float GreetTimer = 5f;     // minutes
    private const float GreetRange = 5f;     // metres
    private const float SpyglassDist = 1.5f; // metres
    private const double GreetFov = 0.6667;  // 120° / 180°
    private const uint VehicleNickId = 22;
    private const uint SpyglassId = 6129;
    // ------------------------------------------------------
    private static readonly TimeSpan GreetCooldown = TimeSpan.FromMinutes(GreetTimer);
    private readonly Dictionary<uint, DateTime> _greeted = new();
    // ------------------------------------------------------
    private bool _isInitialized;

    public override void Enter()
    {
        if (!Validate()) return;

        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        _isInitialized = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!ValidateTickState()) return;
        if (!Validate() || !Throttle()) return;

        var now = DateTime.UtcNow;
        var playersInRange = GetPlayersInRange(Ai.Owner, GreetRange, GreetFov, _greeted, GreetCooldown);

        foreach (var player in playersInRange)
        {
            if (!_greeted.TryGetValue(player.ObjId, out var last) || now - last >= GreetCooldown)
            {
                SendGreeting(player);
                SpawnSpyglassIfNeeded();
                _greeted[player.ObjId] = now;
            }
        }

        // clean-up offline / too old entries
        var toRemove = _greeted.Keys
            .Where(id => playersInRange.All(p => p.ObjId != id) && now - _greeted[id] >= GreetCooldown)
            .ToList();
        toRemove.ForEach(id => _greeted.Remove(id));

        if (playersInRange.Count == 0)
            Ai.GoToDefaultBehavior();
    }

    public override void Exit()
    {
        if (!_isInitialized || Ai?.Owner == null) return;
        _isInitialized = false;
    }

    private bool ValidateTickState()
    {
        if (!_isInitialized)
        {
            Logger.Warn($"TalkBehavior.Tick called before initialization for unit {Ai?.Owner?.ObjId}");
            return false;
        }
        return true;
    }

    private void SendGreeting(Character player)
    {
        var npcTemplateId = (int)Ai.Owner.TemplateId;
        var (eventType, weight) = DetermineEventType(player);
        if (eventType == null) return;

        var selected = AiGameData.Instance.GetEvent(npcTemplateId, eventType, weight);
        if (selected == null) return;

        if (!AiGameData.Instance.TryGet(selected.Id, out var bubble))
            return;

        var msgPacket = new SCNpcChatMessagePacket(
            ChatType.White,
            Ai.Owner,
            player,
            kind: 1,
            type: (uint)bubble.Id,
            message: "");

        var sndPacket = new SCPlaySoundPacket(
            kind: 2,
            bubbleId: (uint)bubble.Id,
            npcObjId: Ai.Owner.ObjId);

        player.BroadcastPacket(msgPacket, true);
        player.BroadcastPacket(sndPacket, true);

        if (selected.SkillId > 0)
            Ai.Owner.UseSkill((uint)selected.SkillId, player);

        Logger.Debug($"NPC {Ai.Owner.TemplateId} sent {eventType}:{weight} #{bubble.Id} to {player.Name}:{player.Level}");
    }

    /// <summary>
    /// Determines the event type for a given player.
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
    private (string EventName, float Weight) DetermineEventType(Character player)
    {
        var npcId = (int)Ai.Owner.TemplateId;
        int lvl   = player.Level;
                
        // 1. Enemy check
        if (Ai.Owner.CanAttack(player) && !IsFriendly(player) && AiGameData.Instance.GetEvents(npcId, "OnEnemySeen").Count > 0)
        {
            return ("OnEnemySeen", 500f);
        }

        // 2. All records for NPC
        var records = new List<(string Name, float Weight)>();
        foreach (var name in new[] { "OnClientGreeting", "OnFriendNearSeen" })
        {
            var events = AiGameData.Instance.GetEvents(npcId, name);
            foreach (var ev in events)
                records.Add((name, ev.Weight));
        }

        // 3. Determine maximum allowed weight based on level
        var maxWeight = lvl switch
        {
            < 15 => 500f,
            < 30 => 300f,
            < 45 => 200f,
            _    => 60f
        };

        // 4. Get the entry with the highest weight not exceeding maxWeight
        var best = records
            .Where(r => r.Weight <= maxWeight)
            .OrderByDescending(r => r.Weight) // pick the maximum allowed
            .FirstOrDefault();

        return best != default
            ? best
            : (string.Empty, 0f);
    }

    private bool IsFriendly(Character player)
    {
        if (player == null || Ai?.Owner == null) return false;
        var relation = Ai.Owner.GetRelationStateTo(player);
        return relation is RelationState.Friendly or RelationState.Neutral;
    }

    private void SpawnSpyglassIfNeeded()
    {
        if (Ai.Owner.Template.NpcNicknameId != VehicleNickId) return;

        var pos = Ai.Owner.Transform.CloneDetached();
        pos.Local.AddDistanceToFront(SpyglassDist);
        var yaw = (float)MathUtil.CalculateAngleFrom(pos, Ai.Owner.Transform);

        var spawner = new DoodadSpawner
        {
            Id = 0,
            UnitId = SpyglassId,
            Position = pos.CloneAsSpawnPosition()
        };
        spawner.ParentWorld = Ai.Owner.ParentWorld;
        spawner.Position.Yaw = yaw;
        spawner.Position.Pitch = 0;
        spawner.Position.Roll = 0;
        _ = spawner.Spawn(0);
    }
}
