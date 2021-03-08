using System.Collections.Generic;
using AAEmu.Game.Models.Game.AI.v2.Params.Archer;
using AAEmu.Game.Models.Game.AI.v2.Params.BigMonster;
using AAEmu.Game.Models.Game.AI.V2.Params;
using AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;

namespace AAEmu.Game.Models.Game.AI.v2.Params
{
    // TODO : Load this!!
    public class AiParams
    {
        public enum AiParamType : uint
        {
            None = 0,
            SiegeWeaponPlace = 11,
            Bomb = 12,
            Roaming = 13,
            HoldPosition = 15,
            TowerDefenseAttacker = 17,
            Flytrap = 18,
            BigMonsterHoldPosition = 19,
            BigMonsterRoaming = 20,
            ArcherHoldPosition = 21,
            ArcherRoaming = 22,
            WildBoarHoldPosition = 23,
            WildBoarRoaming = 24,
            Dummy = 25,
            Default = 26,
            AlmightyNpc = 27
        }
        // TODO: Msgs
        public static AiParams CreateByType(AiParamType type, string aiParamsString)
        {
            switch (type)
            {
                //case AiParamType.HoldPosition:
                    //return new HoldPositionAiParams();
                case AiParamType.AlmightyNpc:
                    return new AlmightyNpcAiParams(aiParamsString);
                case AiParamType.BigMonsterHoldPosition:
                case AiParamType.BigMonsterRoaming:
                    return new BigMonsterAiParams(aiParamsString);
                case AiParamType.ArcherHoldPosition:
                case AiParamType.ArcherRoaming:
                    return new ArcherAiParams(aiParamsString);
                case AiParamType.WildBoarRoaming:
                case AiParamType.WildBoarHoldPosition:
                    return new WildBoarAiParams(aiParamsString);
                default:
                    return null;
            }
        }
    }
}
