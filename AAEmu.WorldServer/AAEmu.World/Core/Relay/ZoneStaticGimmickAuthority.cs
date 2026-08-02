using System.Numerics;

using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Tracks static gimmicks discovered by the native Zone so World can authorize client grasp state.
/// </summary>
internal static class ZoneStaticGimmickAuthority
{
    private sealed class State(uint ownerZoneId, Vector3 position)
    {
        public uint OwnerZoneId { get; } = ownerZoneId;
        public Vector3 Position { get; } = position;
        public uint GrasperUnitId { get; set; }
    }

    // X2::GameClient initializes both current-gimmick and grasper IDs to zero.
    private const uint NoObjectId = 0;
    private static readonly Lock SyncRoot = new();
    private static readonly Dictionary<uint, State> Gimmicks = [];

    public static void Register(uint gimmickObjId, uint ownerZoneId, float x, float y, float z)
    {
        lock (SyncRoot)
            Gimmicks[gimmickObjId] = new State(ownerZoneId, new Vector3(x, y, z));
    }

    public static bool Interact(Character character, uint gimmickObjId)
    {
        if (character?.ParentWorld == null)
            return false;

        State? target;
        List<(uint GimmickObjId, State State, bool Grasped)> changes = [];
        lock (SyncRoot)
        {
            if (!Gimmicks.TryGetValue(gimmickObjId, out target) || target == null ||
                character.Transform.ZoneId != target.OwnerZoneId ||
                !WorldManager.IsInNeighborhood(character, target.Position))
                return false;

            if (target.GrasperUnitId == character.ObjId)
            {
                target.GrasperUnitId = NoObjectId;
                changes.Add((gimmickObjId, target, false));
            }
            else
            {
                if (target.GrasperUnitId != NoObjectId)
                    return false;

                foreach (var (heldObjId, heldState) in Gimmicks
                             .Where(pair => pair.Value.GrasperUnitId == character.ObjId))
                {
                    heldState.GrasperUnitId = NoObjectId;
                    changes.Add((heldObjId, heldState, false));
                }

                target.GrasperUnitId = character.ObjId;
                changes.Add((gimmickObjId, target, true));
            }
        }

        if (changes.Count > 0 && changes[^1].Grasped)
            character.ParentWorld.GimmickManager?.ReleaseGrasps(character.ObjId);

        foreach (var change in changes)
            Publish(character, change.GimmickObjId, change.State, character.ObjId, change.Grasped);
        return true;
    }

    public static void Release(Character character)
    {
        if (character == null)
            return;

        List<(uint GimmickObjId, State State)> released = [];
        lock (SyncRoot)
        {
            foreach (var (gimmickObjId, state) in Gimmicks
                         .Where(pair => pair.Value.GrasperUnitId == character.ObjId))
            {
                state.GrasperUnitId = NoObjectId;
                released.Add((gimmickObjId, state));
            }
        }

        foreach (var release in released)
            Publish(character, release.GimmickObjId, release.State, character.ObjId, false);
    }

    public static void Clear()
    {
        lock (SyncRoot)
            Gimmicks.Clear();
    }

    private static void Publish(
        Character character,
        uint gimmickObjId,
        State state,
        uint grasperUnitId,
        bool grasped)
    {
        character.BroadcastPacket(
            new SCGimmickGraspedPacket((int)gimmickObjId, (int)grasperUnitId, grasped), true);
        WorldIntegration.RelayGimmickGraspedToZone?.Invoke(
            state.OwnerZoneId, gimmickObjId, grasperUnitId, grasped);
    }
}
