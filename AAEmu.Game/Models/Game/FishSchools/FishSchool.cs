using AAEmu.Game.Models.Game.Char;
using Task = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Models.Game.FishSchools;

public class FishSchool
{
    public Task FishFinderTickTask { get; set; }
    //unused private Character Owner { get; set; }

    public FishSchool(Character character)
    {
        //unused Owner = character;
    }
}
