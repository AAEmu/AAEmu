using AAEmu.Game.Models.Game.Char.Templates;

namespace AAEmu.Game.Models.Game.Char
{
    public class Actability
    {
        public uint Id { get; set; }
        public ActabilityTemplate Template { get; set; }
        public int Point { get; set; }
        public byte Step { get; set; }

        public Actability(ActabilityTemplate template)
        {
            Id = template.Id;
            Template = template;
        }
    }
}
