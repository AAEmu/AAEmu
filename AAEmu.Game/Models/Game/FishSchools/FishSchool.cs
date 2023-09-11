using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks;

namespace AAEmu.Game.Models.Game.FishSchools;

public class FishSchool
{
    public Task FishFinderTickTask { get; set; }
    private Character Owner { get; set; }

    public FishSchool(Character character)
    {
        Owner = character;
    }
}
