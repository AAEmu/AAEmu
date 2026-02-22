namespace AAEmu.Game.Core.Managers;

public interface IExpressTextManager : ILoadable
{
    uint GetExpressAnimId(uint emotionId);
}