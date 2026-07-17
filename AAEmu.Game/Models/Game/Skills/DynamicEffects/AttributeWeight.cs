using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.DynamicEffects;

public class AttributeWeight
{
    public int Str { get; set; }
    public int Dex { get; set; }
    public int Sta { get; set; }
    public int Int { get; set; }
    public int Spi { get; set; }

    public UnitAttribute CheckFields()
    {
        var fields = new[] { Str, Dex, Sta, Int, Spi };
        var countOfOnes = fields.Count(field => field == 1);

        return countOfOnes switch
        {
            1 => GetFieldSetToOne(),
            4 => GetRandomFieldSetToOne(),
            _ => UnitAttribute.Fai
        };
    }

    private UnitAttribute GetFieldSetToOne()
    {
        var fields = new (int Value, UnitAttribute Attribute)[]
        {
            (Str, UnitAttribute.Str),
            (Dex, UnitAttribute.Dex),
            (Sta, UnitAttribute.Sta),
            (Int, UnitAttribute.Int),
            (Spi, UnitAttribute.Spi)
        };

        var fieldSetToOne = fields.FirstOrDefault(f => f.Value == 1);
        return fieldSetToOne.Value == 1 ? fieldSetToOne.Attribute : UnitAttribute.Fai;
    }

    private UnitAttribute GetRandomFieldSetToOne()
    {
        var fields = new (int Value, UnitAttribute Attribute)[]
        {
            (Str, UnitAttribute.Str),
            (Dex, UnitAttribute.Dex),
            (Sta, UnitAttribute.Sta),
            (Int, UnitAttribute.Int),
            (Spi, UnitAttribute.Spi)
        };

        var fieldsSetToOne = fields.Where(f => f.Value == 1).ToList();

        if (fieldsSetToOne.Count == 0)
        {
            return UnitAttribute.Fai;
        }

        var randomField = fieldsSetToOne[Random.Shared.Next(fieldsSetToOne.Count)];
        return randomField.Attribute;
    }
}
