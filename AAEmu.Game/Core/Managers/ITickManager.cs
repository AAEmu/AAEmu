namespace AAEmu.Game.Core.Managers;

public interface ITickManager : IInitializable
{
    TickManager.TickEventHandler OnTick { get; }
    void Stop();
}
