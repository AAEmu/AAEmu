using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Gimmicks;

public abstract class GimmickTask(BaseUnit caster, Gimmick owner, uint skillId) : Task
{
    protected BaseUnit _caster = caster;
    protected Gimmick _owner = owner;
    protected uint _skillId = skillId;
}
