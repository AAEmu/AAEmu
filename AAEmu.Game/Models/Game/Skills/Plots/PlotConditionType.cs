namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public enum PlotConditionType
    {
        Level = 1,   
        Relation = 2,       
        Direction = 3,       
        Unk4 = 4,       // DOESNT EXIST
        BuffTag = 5,    
        WeaponEquipStatus = 6,       
        Chance = 7,       
        Dead = 8,       
        CombatDiceResult = 9,       
        InstrumentType = 10,     
        Range = 11,     
        Variable = 12,     
        UnitAttrib = 13,     
        Actability = 14,    
        Stealth = 15,    
        Visible = 16,     
        ABLevel = 17,    
    }

    // No clue what that shit does fam
    // It's probably Param1 when PlotConditionType == Variable
    public enum PlotVariableType 
    {
        A = 1,
        B = 2,
        C = 3,
        D = 4,
        E = 5,
        F = 6,
        G = 7,
        H = 8,
        I = 9,
        J = 10,
        Zero = 11,
        Targets = 12
    }
}
