using AAEmu.Game.Models.Game.AI.Params.BigMonster;

namespace AAEmu.Game.Models.Game.AI.Params
{
    public enum AiParamType
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
    
    public class AiParamsOld
    {
        public virtual AiParamType Type { get; set; } = AiParamType.None;
        public virtual void Parse(string data) {}

        public static AiParamsOld GetByType(AiParamType type)
        {
            switch (type)
            {
                case AiParamType.HoldPosition:
                    return new HoldPositionAiParams();
                case AiParamType.AlmightyNpc:
                    return new AlmightyNpcAiParams();
                case AiParamType.BigMonsterRoaming:
                    return new BigMonsterRoamingAiParams();
                default:
                    return null;
            }
        }
    }
}
