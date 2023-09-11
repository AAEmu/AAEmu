using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.FishSchools;

public abstract class FishSchoolsTask : Task
{
    private Character _character;

    protected FishSchoolsTask(Character character)
    {
        _character = character;
    }
}
