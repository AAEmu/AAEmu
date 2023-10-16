using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.FishSchools;

#pragma warning disable IDE0052 // Remove unread private members

public abstract class FishSchoolsTask : Task
{
    private Character _character;

    protected FishSchoolsTask(Character character)
    {
        _character = character;
    }
}
