namespace AAEmu.Game.Models.CryEngine.Entities;

internal class LinkRecord
{
    public int NodeIndex { get; set; }
    public double RadiusOut { get; set; }
    public double RadiusIn { get; set; }

    public bool Equals(LinkRecord other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return NodeIndex == other.NodeIndex &&
               RadiusOut.Equals(other.RadiusOut) &&
               RadiusIn.Equals(other.RadiusIn);
    }
}
