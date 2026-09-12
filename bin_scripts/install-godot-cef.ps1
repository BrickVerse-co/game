param(
    [ValidateSet("windows-x64", "windows-arm64")]
    [string]$Platform = "windows-x64",
    [string]$ArchivePath = ""
)
$ErrorActionPreference = "Stop"
$version = "v1.15.4"
$targets = @{
    "windows-x64" = "x86_64-pc-windows-msvc"
    "windows-arm64" = "aarch64-pc-windows-msvc"
}
if (!$ArchivePath) {
    $ArchivePath = Join-Path ([IO.Path]::GetTempPath()) "brickverse-godot-cef-$version.zip"
    if (!(Test-Path -LiteralPath $ArchivePath)) {
        Invoke-WebRequest "https://github.com/dsh0416/godot-cef/releases/download/$version/godot_cef-$version.zip" -OutFile $ArchivePath
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../BrickVerse/addons/godot_cef"))
$zip = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    foreach ($entry in $zip.Entries) {
        $prefix = "dist/addons/godot_cef/"
        if (!$entry.FullName.StartsWith($prefix) -or $entry.FullName.EndsWith("/")) { continue }
        $relative = $entry.FullName.Substring($prefix.Length)
        if ($relative.StartsWith("bin/") -and !$relative.StartsWith("bin/$($targets[$Platform])/")) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar)) { throw "Invalid archive path." }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $zip.Dispose() }
Write-Host "Installed Godot CEF $version for $Platform. Restart Godot before opening Forge."
