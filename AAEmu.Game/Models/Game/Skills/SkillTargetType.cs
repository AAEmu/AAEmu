namespace AAEmu.Game.Models.Game.Skills
{
    public enum SkillTargetType : byte
    {
        Self = 0,
        Friendly = 1,
        Party = 2,
        Raid = 3,
        Hostile = 4,
        AnyUnit = 5,
        Pos = 6,
        Line = 7,
        Doodad = 8,
        Item = 9,
        Pet = 10,
        BallisticPos = 11,
        SummonPos = 12,
        RelativePos = 13,
        SourcePos = 14,
        ArtilleryPos = 15,
        Others = 16,
        FriendlyOthers = 17,
        CursorPos = 18,
        Building = 19
    }
}
