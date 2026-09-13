param([string]$ArchivePath = "")
$ErrorActionPreference = "Stop"
$version = "0.20.0"
$tag = "v0.20.0-godot4"
$archiveName = "gdcef-$($version)_godot-4.5.zip"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../BrickVerse"))
$targetRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "cef_artifacts"))
$oldRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "addons/godot_cef"))

foreach ($path in @($targetRoot, $oldRoot)) {
    if (!$path.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar)) { throw "Refusing to modify a path outside the BrickVerse project." }
}
if (!$ArchivePath) {
    $ArchivePath = Join-Path ([IO.Path]::GetTempPath()) $archiveName
    if (!(Test-Path -LiteralPath $ArchivePath)) {
        Invoke-WebRequest "https://github.com/Lecrapouille/gdcef/releases/download/$tag/$archiveName" -OutFile $ArchivePath
    }
}
if (Test-Path -LiteralPath $targetRoot) { Remove-Item -LiteralPath $targetRoot -Recurse -Force }
[IO.Directory]::CreateDirectory($targetRoot) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith("/")) { continue }
        $marker = "cef_artifacts/"
        $start = $entry.FullName.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase)
        if ($start -lt 0) { continue }
        $relative = $entry.FullName.Substring($start + $marker.Length)
        $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
        if (!$target.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar)) { throw "Invalid archive path." }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $zip.Dispose() }
if (!(Get-ChildItem -LiteralPath $targetRoot -Filter *.gdextension -Recurse)) { throw "The archive did not contain a gdCEF extension." }
if (Test-Path -LiteralPath $oldRoot) {
    try {
        Remove-Item -LiteralPath $oldRoot -Recurse -Force
    } catch {
        $manifest = Join-Path $oldRoot "godot_cef.gdextension"
        if (Test-Path -LiteralPath $manifest) {
            Move-Item -LiteralPath $manifest -Destination ($manifest + ".disabled") -Force
        }
        Write-Warning "The running Godot process has locked the old CEF DLL. Its extension manifest was disabled; restart Godot, then run this installer again to remove the remaining files."
    }
}
Write-Host "Installed gdCEF $version and removed the old godot-cef runtime. Restart Godot before opening Forge."
