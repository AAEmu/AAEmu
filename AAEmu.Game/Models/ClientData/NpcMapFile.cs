using System.Numerics;
using AAEmu.Game.IO;
using NLog;

namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// Reads NPC position data from map_data/npc_map/*.dat files.
/// Binary format: array of (uint16 npcId, float X, float Y, float Z) entries.
/// </summary>
public class NpcMapFile(string fileName)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public string FileName { get; } = fileName;
    public List<NpcMapEntry> Entries { get; } = [];

    public bool ReadFile()
    {
        using var sourceFs = ClientFileManager.GetFileStream(FileName);
        if (sourceFs == null)
            return false;

        using var ms = new MemoryStream();
        sourceFs.CopyTo(ms);
        ms.Position = 0;

        using var br = new BinaryReader(ms);

        // Each entry: uint16 npcId (2) + float X (4) + float Y (4) + float Z (4) = 14 bytes
        // But hexdump shows uint32 + 3 floats = 16 bytes per entry. Let's try 16 first.
        const int entrySize = 16; // uint32 npcId + 3 floats
        var totalEntries = (int)(ms.Length / entrySize);

        for (var i = 0; i < totalEntries; i++)
        {
            try
            {
                var npcId = br.ReadUInt32();
                var x = br.ReadSingle();
                var y = br.ReadSingle();
                var z = br.ReadSingle();

                // Skip entries with zeroed-out positions (padding)
                if (x == 0f && y == 0f && z == 0f)
                    continue;

                Entries.Add(new NpcMapEntry
                {
                    NpcId = npcId,
                    Pos = new Vector3(x, y, z)
                });
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }

        return true;
    }
}
