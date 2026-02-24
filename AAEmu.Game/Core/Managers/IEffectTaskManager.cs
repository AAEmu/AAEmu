using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Managers;

public interface IEffectTaskManager
{
    void AddDispelTask(Buff buff, double interval);
}
