namespace AAEmu.World.Core.Packets.Zw;

public static class ZwOpcodes
{
    public const ushort Join = 0x0000;
    public const ushort CommandResponse = 0x0001;
    public const ushort SpawnNpc = 0x0002;
    public const ushort RemoveNpc = 0x0003;
    public const ushort NpcSaid = 0x0006;
    public const ushort RemoveHouse = 0x0007;
    public const ushort UnitMovements = 0x0008;
    public const ushort UnitModelPostureChanged = 0x000F;
    public const ushort UnitFell = 0x0010;
    public const ushort UnitCollision = 0x0011;
    public const ushort UnitCollisionResult = 0x0012;
    public const ushort ErrorMsgToPlayer = 0x0013;
    public const ushort NpcInteractionEnded = 0x0014;
    public const ushort ChangeTarget = 0x0017;
    public const ushort ChangeUnitFlyState = 0x001C;
    public const ushort TimeOfDay = 0x001D;
    public const ushort DetailedTimeOfDay = 0x001E;
    public const ushort ZoneLoaded = 0x001F;
    public const ushort EnterArea = 0x0023;
    public const ushort LeaveArea = 0x0024;
    public const ushort GimmickMovement = 0x0025;
    public const ushort RequestSpawnStaticGimmick = 0x0026;
    public const ushort GimmickJointBroken = 0x0027;
    public const ushort GimmickResetJoints = 0x0028;
    public const ushort GimmickRemoveParts = 0x0029;
    public const ushort GimmickCollided = 0x002A;
    public const ushort NpcInteractionStatusChanged = 0x002C;
    public const ushort ResponseCombatUnits = 0x002D;
    public const ushort AggroTargetChanged = 0x002E;
    public const ushort RayCastingResult = 0x0030;
    public const ushort TowerDefReportPlayability = 0x0032;
    public const ushort AiAggro = 0x0034;
    public const ushort UnitLocation = 0x0035;
    public const ushort BackpackDropped = 0x0036;
    public const ushort ReportMoleMiner = 0x0037;
    public const ushort ReportMoleTrader = 0x0038;
    public const ushort ReportMoveHack = 0x0039;
    public const ushort ReportDoodadSurfaceHack = 0x003A;
    public const ushort DoodadCollideUnit = 0x003B;
    public const ushort Heartbeat = 0x003C;
}
