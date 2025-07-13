using System.Numerics;
using System.Text;
using NLog;
using AAEmu.Commons.Exceptions;

namespace AAEmu.Game.Models.CryEngine.Readers;

internal abstract class BaiReader : IDisposable
{
    private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();
    public System.IO.Stream RawStream { get; set; }
    public BinaryReader Reader { get; set; }
    public uint ZoneId { get; }

    /// <summary>
    /// This Vector3 gets added to every point read
    /// </summary>
    public Vector3 ReaderPointOffset { get; set; } = Vector3.Zero;

    public static bool IgnoreDuplicateAreaNames { get; set; } = true;

    protected BaiReader(System.IO.Stream rawStream, uint zoneId)
    {
        RawStream = rawStream;
        ZoneId = zoneId;
    }

    public virtual void InitReaderUtil()
    {
        Reader = new BinaryReader(RawStream);
    }

    /// <summary>
    /// Initialize and load the file
    /// </summary>
    /// <exception cref="Exception"></exception>
    public virtual void ReadFile()
    {
        try
        {
            InitReaderUtil();
            var version = Reader?.ReadInt32() ?? -1;
            CheckVersion(version);
            ReadFromFile();
        }
        catch (Exception ex)
        {
            Logger.Debug($"Exception Pos: {Reader?.BaseStream.Position}");
            throw new GameException(ex.Message);
        }
        finally
        {
            Close();
        }
    }

    public virtual void CheckVersion(int version)
    {
        // Do nothing
    }

    public virtual void ReadFromFile()
    {
        throw new IOException();
    }

    public virtual void Close()
    {
        Reader?.Dispose();
        Reader = null;
    }

    public virtual void PrepareExport()
    {
        // Do nothing
    }

    public void Dispose()
    {
        Close();
    }

    /// <summary>
    /// Reads 3 singles as a Vector3 and adds the ReaderPointOffset and returns the result
    /// </summary>
    /// <returns></returns>
    protected Vector3 ReadVector3(bool doNotAddOffset = false)
    {
        if (Reader == null)
            return Vector3.Zero;
        var v3 = new Vector3(Reader.ReadSingle(), Reader.ReadSingle(), Reader.ReadSingle());
        return doNotAddOffset ? v3 : v3 + ReaderPointOffset;
    }

    protected void ReadPoints(List<Vector3> points)
    {
        points.Clear();
        if (Reader == null)
            return;

        var pointsSize = Reader.ReadUInt32();
        for (var i = 0; i < pointsSize; i++)
            points.Add(ReadVector3());
    }

    protected string ReadName()
    {
        if (Reader == null)
            return string.Empty;

        var nameSize = Reader.ReadInt32();
        var name = Encoding.Default.GetString(Reader.ReadBytes(nameSize));
        return name;
    }
}
