using AAEmu.Game.Models.Game.Animation;

namespace AAEmu.Game.Core.Managers;

public interface IAnimationManager : ILoadable
{
    Anim GetAnimation(uint id);
    Anim GetAnimation(string name);
}
