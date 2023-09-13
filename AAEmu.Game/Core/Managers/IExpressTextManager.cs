namespace AAEmu.Game.Core.Managers
{
    public interface IExpressTextManager
    {
        uint GetExpressAnimId(uint emotionId);
        void Load();
    }
}