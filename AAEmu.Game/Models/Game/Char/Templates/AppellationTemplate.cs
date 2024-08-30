namespace AAEmu.Game.Models.Game.Char.Templates;

public class AppellationTemplate
{
    public bool ApplyAppellationAtOnce { get; set; }
    public uint IconId { get; set; }
    public string Name { get; set; }
    public uint Id { get; set; }
    public uint BuffId { get; set; }
    public uint OrderIndex { get; set; }
    public string RouteDesc { get; set; }
    public uint RouteKindId { get; set; }
    public bool RoutePopup { get; set; }
    public uint RouteType { get; set; }
}
