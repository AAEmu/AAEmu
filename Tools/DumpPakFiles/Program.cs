using AAEmu.Commons.Utils.AAPak;

if (args.Length < 1)
{
    Console.WriteLine("Usage: DumpPakFiles <pak_path> [prefix_filter]");
    Console.WriteLine("  Example: DumpPakFiles game_pak game/worlds/main_world/level_design");
    return;
}

var pakPath = args[0];
var prefix = args.Length > 1 ? args[1].ToLower().Replace('\\', '/') : "";

Console.WriteLine($"Opening {pakPath}...");
var pak = new AAPak(pakPath);

if (!pak.isOpen)
{
    Console.Error.WriteLine("Failed to open pak file.");
    return;
}

Console.WriteLine($"Pak opened. Total files: {pak.pakFiles.Count}");

var results = new SortedDictionary<string, long>();
var extensions = new Dictionary<string, int>();
var directories = new SortedSet<string>();

foreach (var kvp in pak.pakFiles)
{
    var name = kvp.Key.ToLower();
    if (!string.IsNullOrEmpty(prefix) && !name.StartsWith(prefix))
        continue;

    results[kvp.Key] = kvp.Value.size;

    // Track extensions
    var ext = Path.GetExtension(name);
    if (!string.IsNullOrEmpty(ext))
        extensions[ext] = extensions.GetValueOrDefault(ext) + 1;

    // Track directories (2 levels deep from prefix)
    var relPath = string.IsNullOrEmpty(prefix) ? name : name[prefix.Length..].TrimStart('/');
    var parts = relPath.Split('/');
    if (parts.Length > 1)
    {
        directories.Add(parts[0]);
        if (parts.Length > 2)
            directories.Add($"{parts[0]}/{parts[1]}");
    }
}

Console.WriteLine($"\n=== Files matching '{prefix}': {results.Count} ===\n");

// Print directory structure summary
Console.WriteLine("--- Directories ---");
foreach (var dir in directories)
    Console.WriteLine($"  {dir}/");

Console.WriteLine($"\n--- Extensions ---");
foreach (var kvp in extensions.OrderByDescending(x => x.Value))
    Console.WriteLine($"  {kvp.Key}: {kvp.Value} files");

// Print all files
Console.WriteLine($"\n--- All Files ---");
foreach (var kvp in results)
    Console.WriteLine($"  {kvp.Key}  ({kvp.Value:N0} bytes)");

// Export to file
var outPath = Path.Combine(Directory.GetCurrentDirectory(), "level_design_files.txt");
File.WriteAllLines(outPath, results.Keys);
Console.WriteLine($"\nExported file list to: {outPath}");

pak.ClosePak();
