using AAEmu.Game.Models.Game.AI.Params.BigMonster;

namespace AAEmu.Game.Models.Game.AI.Params;

public class AiParamsOld
{
    public virtual AiParamType Type { get; set; } = AiParamType.None;
    public virtual void Parse(string data) { }

    public static AiParamsOld GetByType(AiParamType type)
    {
        switch (type)
        {
            case AiParamType.HoldPosition:
                return new HoldPositionAiParams();
            case AiParamType.AlmightyNpc:
                return new AlmightyNpcParams();
            case AiParamType.BigMonsterRoaming:
                return new BigMonsterRoamingAiParams();
            default:
                return null;
        }
    }
}
