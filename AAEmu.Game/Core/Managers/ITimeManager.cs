namespace AAEmu.Game.Core.Managers;

public interface ITimeManager
{
    float GetTime { get; }
    float Get();
    void Set(float hours);
    void OnZoneReport(float hours);
}
