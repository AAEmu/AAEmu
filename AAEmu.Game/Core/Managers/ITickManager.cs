namespace AAEmu.Game.Core.Managers;

public interface ITickManager
{
    TickManager.TickEventHandler OnTick { get; }
    void Initialize();
    void Stop();
}
