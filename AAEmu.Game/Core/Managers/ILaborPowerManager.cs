namespace AAEmu.Game.Core.Managers
{
    public interface ILaborPowerManager
    {
        void Initialize();
        void LaborPowerTickStart();
        void LaborPowerTick();
    }
}
