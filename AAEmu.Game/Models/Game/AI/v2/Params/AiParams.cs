using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.AI.v2.Params.Archer;
using AAEmu.Game.Models.Game.AI.v2.Params.BigMonster;
using AAEmu.Game.Models.Game.AI.v2.Params.Flytrap;
using AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;

namespace AAEmu.Game.Models.Game.AI.v2.Params;

// TODO : Load this!!
public class AiParams
{
    // should be in float: AlertDuration, MeleeAttackRange, TimeRange, SkillDelay, Delay : found in npc_ai_params
    public float AlertDuration { get; set; } = 3f;
    public bool AlertToAttack { get; set; } = true;
    public float AlertSafeTargetRememberTime { get; set; } = 5f;
    public bool AlwaysTeleportOnReturn { get; set; } // true only for elect Npcs
    public int MaxMakeAGapCount { get; set; } = 3; // give the archers a chance to run off as often as possible
    public float MeleeAttackRange { get; set; } = 4f;
    public float PreferedCombatDist { get; set; } = 0f; // 5, 10, 15 Also found in entity
    public bool RestorationOnReturn { get; set; } = true; // false only for elect Npcs
    public bool GoReturnState { get; set; } = true; // allows returning to the spawn point or not

    // TODO: Msgs
    public static AiParams CreateByType(AiParamType type, string aiParamsString)
    {
        switch (type)
        {
            case AiParamType.AlmightyNpc:
                return new AlmightyNpcAiParams(aiParamsString);
            case AiParamType.ArcherHoldPosition:
            case AiParamType.ArcherRoaming:
                return new ArcherAiParams(aiParamsString);
            case AiParamType.BigMonsterHoldPosition:
            case AiParamType.BigMonsterRoaming:
                return new BigMonsterAiParams(aiParamsString);
            case AiParamType.Flytrap:
                return new FlytrapAiParams(aiParamsString);
            case AiParamType.WildBoarRoaming:
            case AiParamType.WildBoarHoldPosition:
                return new WildBoarAiParams(aiParamsString);
            case AiParamType.None:
            case AiParamType.SiegeWeaponPlace:
            case AiParamType.Bomb:
            case AiParamType.Roaming:
            case AiParamType.HoldPosition:
            case AiParamType.TowerDefenseAttacker:
            case AiParamType.Dummy:
            case AiParamType.Default:
            default:
                return null;
        }
    }
}
