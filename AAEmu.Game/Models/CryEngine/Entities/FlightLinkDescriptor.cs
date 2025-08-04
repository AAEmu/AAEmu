namespace AAEmu.Game.Models.CryEngine.Entities;

public class FlightLinkDescriptor
{
    public int IndexFirst { get; set; }
    public int IndexSecond { get; set; }

    public bool Equals(FlightLinkDescriptor other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return (IndexFirst == other.IndexFirst) && (IndexSecond == other.IndexSecond);
    }
}
