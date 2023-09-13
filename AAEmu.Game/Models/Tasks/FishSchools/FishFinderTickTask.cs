using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.FishSchools
{
    public class FishSchoolTickTask : FishSchoolsTask
    {
        private Character _character;

        public FishSchoolTickTask(Character character) : base(character)
        {
            _character = character;
        }

        public override void Execute()
        {
            FishSchoolManager.Instance.FishFinderTick(_character);
        }
    }
}