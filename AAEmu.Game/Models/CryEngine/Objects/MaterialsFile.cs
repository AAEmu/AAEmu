using System.Text;
using AAEmu.Game.IO;
using NLog;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class MaterialsFile(string fileName)
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();
    public string FileName { get; init; } = fileName;
    public List<string> MaterialList { get; set; } = [];

    public bool ReadFile()
    {
        MaterialList.Clear();
        try
        {
            using var sourceFs = ClientFileManager.GetFileStream(FileName);
            using var fs = new MemoryStream();
            sourceFs.CopyTo(fs);
            if (fs.Length <= 4)
            {
                // File too small contain data
                return false;
            }
            fs.Seek(0, SeekOrigin.Begin);

            using var br = new BinaryReader(fs);
            var count = br.ReadUInt32();
            for (var i = 0u; i < count; i++)
            {
                var chunkBytes = br.ReadBytes(256);
                var materialName = Encoding.UTF8.GetString(chunkBytes).Replace("\0", "").TrimEnd();
                MaterialList.Add(materialName);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error reading file {FileName}, Exception: {ex}");
        }

        return false;
    }
}
