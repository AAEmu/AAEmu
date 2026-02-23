using System.IO;

namespace CgfConverter.PackFileSystem;

public interface IPackFileSystem
{
    Stream GetStream(string path);

    bool Exists(string path);

    string[] Glob(string pattern);

    byte[] ReadAllBytes(string path);
}