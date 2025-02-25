using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

using AAEmu.Commons.IO;

using NLog;

using Process_Commons;

namespace AAEmu.Commons.Utils.Creatures;

[Serializable]
public class Creature : IComparable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [XmlAttribute("Id")]
    public uint Id { get; set; }

    [XmlAttribute("Name")]
    public string Title { get; set; } /* len = 64 */

    public Creature() : this(0, string.Empty) { }
    public Creature(uint id, string title)
    {
        Id = id;
        Title = title;
    }

    public override string ToString()
    {
        return Id + " ­- " + Title;
    }

    public static Dictionary<uint, Creature> GetAllDoodads()
    {
        var result = new List<Creature>();
        try
        {
            Serialization.XmlToObject(FileManager.AppPath + "/Data/Doodads.xml", out result, "Creatures");
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to load Doodads.xml /n{e} {FileManager.AppPath}");
        }
        return result.ToDictionary(c => c.Id, c => c);
    }

    public static Dictionary<uint, Creature> GetAllCreatures()
    {
        var result = new List<Creature>();
        try
        {
            Serialization.XmlToObject(FileManager.AppPath + "/Data/Creatures.xml", out result, "Creatures");
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to load Creatures.xml /n{e} {FileManager.AppPath}");
        }
        return result.ToDictionary(c => c.Id, c => c);
    }


    #region IComparable Members

    public int CompareTo(object obj)
    {
        if (obj is Creature)
        {
            var comp = (Creature)obj;
            if (comp.Id > Id)
                return -1;
            return 1;
        }
        throw new Exception();
    }

    #endregion
}
