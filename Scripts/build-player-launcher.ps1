param(
    [Parameter(Mandatory = $true)][string]$LauncherExe,
    [Parameter(Mandatory = $true)][string]$PlayerDriver,
    [Parameter(Mandatory = $true)][string]$NativeLauncher,
    [Parameter(Mandatory = $true)][string]$OutputExe
)

$ErrorActionPreference = 'Stop'
# This supplied WinForms launcher embeds three helper binaries as CLR resources.
# Patch a copy without changing resource offsets or managed method bodies.
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom([IO.Path]::GetFullPath($LauncherExe))
if ($assembly.GetName().GetPublicKey().Length) { throw 'This launcher has a strong name; supply the supported unsigned launcher.' }
$resource = $assembly.GetManifestResourceStream('aadriver.dll')
if (-not $resource) { throw 'Launcher does not embed aadriver.dll.' }
$memory = New-Object IO.MemoryStream
try { $resource.CopyTo($memory); $originalDriver = $memory.ToArray() }
finally { $resource.Dispose(); $memory.Dispose() }
$resource = $assembly.GetManifestResourceStream('aaemu-launcher.exe')
if (-not $resource) { throw 'Launcher does not embed aaemu-launcher.exe.' }
$memory = New-Object IO.MemoryStream
try { $resource.CopyTo($memory); $originalLauncher = $memory.ToArray() }
finally { $resource.Dispose(); $memory.Dispose() }

Add-Type -TypeDefinition @'
using System;
using System.Text;
public static class AAEmuPlayerLauncherPatch {
    static int Find(byte[] bytes, byte[] value) {
        int found = -1;
        for (int i = 0; i <= bytes.Length - value.Length; i++) {
            if (bytes[i] != value[0]) continue;
            int j = 1;
            while (j < value.Length && bytes[i + j] == value[j]) j++;
            if (j != value.Length) continue;
            if (found >= 0) throw new InvalidOperationException("Ambiguous launcher patch target.");
            found = i;
        }
        if (found < 0) throw new InvalidOperationException("Unsupported launcher build: patch target not found.");
        return found;
    }
    static void Text(byte[] bytes, string previous, string next) {
        if (next.Length > previous.Length) throw new InvalidOperationException("Replacement exceeds the string slot.");
        byte[] before = Encoding.Unicode.GetBytes(previous);
        byte[] after = Encoding.Unicode.GetBytes(next.PadRight(previous.Length));
        Buffer.BlockCopy(after, 0, bytes, Find(bytes, before), after.Length);
    }
    static void Resource(byte[] bytes, byte[] original, byte[] player) {
        int offset = Find(bytes, original);
        if (player.Length > original.Length || offset < 4 || BitConverter.ToUInt32(bytes, offset - 4) != original.Length)
            throw new InvalidOperationException("Unsupported embedded driver resource layout.");
        Buffer.BlockCopy(BitConverter.GetBytes(player.Length), 0, bytes, offset - 4, 4);
        Buffer.BlockCopy(player, 0, bytes, offset, player.Length);
        Array.Clear(bytes, offset + player.Length, original.Length - player.Length);
    }
    public static byte[] Patch(byte[] bytes, byte[] original, byte[] player, byte[] originalLauncher, byte[] launcher) {
        Resource(bytes, original, player);
        Resource(bytes, originalLauncher, launcher);
        Text(bytes, " --sound --no-fast-start --no-fast-pak --no-smooth-start --no-compressed-pak",
                    " --sound --fast-start --fast-pak --smooth-start");
        // Separate helper/settings storage from the untouched downloaded launcher.
        Text(bytes, "AAEmuPortableLauncher", "AAEmuPlayerLauncher12");
        return bytes;
    }
}
'@
$patched = [AAEmuPlayerLauncherPatch]::Patch([IO.File]::ReadAllBytes($LauncherExe),
    $originalDriver, [IO.File]::ReadAllBytes($PlayerDriver), $originalLauncher, [IO.File]::ReadAllBytes($NativeLauncher))
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($OutputExe), $patched)
Write-Output "Player GUI launcher ready: $OutputExe"
