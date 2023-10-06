namespace AAEmu.Game.Models.Game.AI.v2.Framework;

public enum BehaviorKind
{
    // Common
    Alert,
    AlmightyAttack,
    Attack,
    Dead,
    Despawning,
    DoNothing,
    Dummy,
    FollowPath,
    FollowUnit,
    HoldPosition,
    Idle,
    ReturnState,
    Roaming,
    RunCommandSet,
    Spawning,
    Talk,

    // Archer
    ArcherAttack,

    // BigMonster
    BigMonsterAttack,

    // Flytrap
    FlytrapAlert,
    FlytrapAttack,

    // WildBoar
    WildBoarAttack
}
