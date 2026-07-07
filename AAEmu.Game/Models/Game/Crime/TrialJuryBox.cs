using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Game.Crime;

public class TrialJuryBox
{
    public TrialCourtRoom CourtRoom { get; set; }
    public Doodad Seat { get; set; }
    public int SeatId { get; set; }
    public Character JuryMember { get; set; }
    public bool ConfirmTestimony { get; set; }
    public int SelectedSentence { get; set; }
}
