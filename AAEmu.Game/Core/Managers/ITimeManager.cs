namespace AAEmu.Game.Core.Managers;

public interface ITimeManager
{
    float GetTime { get; }
    float Get();
    void Start();
    void Set(float hours);
    /// <summary>Deprecated no-op; instance ZW ToD must not drive the shared day.</summary>
    void OnZoneReport(float hours);
}
