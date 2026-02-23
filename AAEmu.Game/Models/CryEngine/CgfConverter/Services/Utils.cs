using System;
using System.Diagnostics;
using System.Text;

namespace CgfConverter;

public enum StringSizeEnum
{
    Int8 = 1,
    Int16 = 2,
    Int32 = 4,
}

public enum LogLevelEnum
{
    /// <summary>Log Everything</summary>
    Verbose = 0x01,
    /// <summary>Log Some Stuff Useful For Debugging</summary>
    Debug = 0x02,
    /// <summary>Log Information/Progress Updates </summary>
    Info = 0x04,
    /// <summary>Log Warnings (Default)</summary>
    Warning = 0x08,
    /// <summary>Log Errors</summary>
    Error = 0x0F,
    /// <summary>Log Critical Errors</summary>
    Critical = 0x10,
    /// <summary>Don't Log Anything</summary>
    None = 0xFF,
}

public static class Utilities
{
    public static LogLevelEnum LogLevel { get; set; }
    public static LogLevelEnum DebugLevel { get; set; }

    /// <summary>Private DataStore for the DateTimeFormats property.summary>
    private static string[] _dateTimeFormats = new [] {
        @"yyyy-MM-dd\THHmm", // 2010-12-31T2359
    };

    public static double Safe(double value)
    {
        if (value == double.NegativeInfinity)
            return double.MinValue;

        if (value == double.PositiveInfinity)
            return double.MaxValue;

        if (double.IsNaN(value))
            return 0;

        return value;
    }

    public static void CleanNumbers(StringBuilder sb)
    {
        sb.Replace("0.000000", "0");
        sb.Replace("-0.000000", "0");
        sb.Replace("1.000000", "1");
        sb.Replace("-1.000000", "-1");
    }

    /// <summary>Custom DateTime formats supported by the parser.</summary>
    public static string[] DateTimeFormats
    {
        get { return _dateTimeFormats; }
        set { _dateTimeFormats = value; }
    }

//    #region Gets the build date and time (by reading the COFF header)

//    // http://msdn.microsoft.com/en-us/library/ms680313

//    struct _IMAGE_FILE_HEADER
//    {
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.Machine' is never assigned to, and will always have its default value 0
//        public ushort Machine;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.Machine' is never assigned to, and will always have its default value 0
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.NumberOfSections' is never assigned to, and will always have its default value 0
//        public ushort NumberOfSections;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.NumberOfSections' is never assigned to, and will always have its default value 0
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.TimeDateStamp' is never assigned to, and will always have its default value 0
//        public uint TimeDateStamp;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.TimeDateStamp' is never assigned to, and will always have its default value 0
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.PointerToSymbolTable' is never assigned to, and will always have its default value 0
//        public uint PointerToSymbolTable;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.PointerToSymbolTable' is never assigned to, and will always have its default value 0
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.NumberOfSymbols' is never assigned to, and will always have its default value 0
//        public uint NumberOfSymbols;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.NumberOfSymbols' is never assigned to, and will always have its default value 0
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.SizeOfOptionalHeader' is never assigned to, and will always have its default value 0
//        public ushort SizeOfOptionalHeader;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.SizeOfOptionalHeader' is never assigned to, and will always have its default value 0
//#pragma warning disable CS0649 // Field 'Utils._IMAGE_FILE_HEADER.Characteristics' is never assigned to, and will always have its default value 0
//        public ushort Characteristics;
//#pragma warning restore CS0649 // Field 'Utils._IMAGE_FILE_HEADER.Characteristics' is never assigned to, and will always have its default value 0
//    };

//    public static DateTime GetBuildDateTime(Assembly assembly)
//    {
//        if (File.Exists(assembly.Location))
//        {
//            var buffer = new byte[Math.Max(Marshal.SizeOf(typeof(_IMAGE_FILE_HEADER)), 4)];
//            using (var fileStream = new FileStream(assembly.Location, FileMode.Open, FileAccess.Read))
//            {
//                fileStream.Position = 0x3C;
//                fileStream.Read(buffer, 0, 4);
//                fileStream.Position = BitConverter.ToUInt32(buffer, 0); // COFF header offset
//                fileStream.Read(buffer, 0, 4); // "PE\0\0"
//                fileStream.Read(buffer, 0, buffer.Length);
//            }
//            var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
//            try
//            {
//                var coffHeader = (_IMAGE_FILE_HEADER)Marshal.PtrToStructure(pinnedBuffer.AddrOfPinnedObject(), typeof(_IMAGE_FILE_HEADER));

//                return TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1) + new TimeSpan(coffHeader.TimeDateStamp * TimeSpan.TicksPerSecond));
//            }
//            finally
//            {
//                pinnedBuffer.Free();
//            }
//        }
//        return new DateTime();
//    }

//    #endregion

    public static void Log(string? format = null, params object[] args) => Log(LogLevelEnum.Debug, format, args);

    public static void Log(LogLevelEnum logLevel, string? format = null, params object[] args)
    {
        if (Utilities.LogLevel <= logLevel)
            Console.WriteLine(format ?? string.Empty, args);

        if (Utilities.DebugLevel <= logLevel)
            Debug.WriteLine(format ?? string.Empty, args);
    }
}