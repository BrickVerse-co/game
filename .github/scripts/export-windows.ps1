param(
	[Parameter(Mandatory = $true)][string]$GodotVersion,
	[Parameter(Mandatory = $true)][string]$TemplateVersion,
	[Parameter(Mandatory = $true)][string]$ProjectDirectory,
	[Parameter(Mandatory = $true)][string]$ProjectFile,
	[Parameter(Mandatory = $true)][string]$ExportDirectory,
	[Parameter(Mandatory = $true)][string]$ExportPreset,
	[Parameter(Mandatory = $true)][string]$ExportFile
)

$ErrorActionPreference = "Stop"

$godotArchive = Join-Path $env:RUNNER_TEMP "godot.zip"
$godotDirectory = Join-Path $env:RUNNER_TEMP "godot"
$templateArchive = Join-Path $env:RUNNER_TEMP "templates.tpz"
$templateExtract = Join-Path $env:RUNNER_TEMP "godot-templates"
$templateDirectory = Join-Path $env:APPDATA "Godot\export_templates\$TemplateVersion"

Invoke-WebRequest `
	-Uri "https://github.com/godotengine/godot/releases/download/$GodotVersion/Godot_v${GodotVersion}_mono_win64.zip" `
	-OutFile $godotArchive
Expand-Archive -LiteralPath $godotArchive -DestinationPath $godotDirectory -Force

Invoke-WebRequest `
	-Uri "https://github.com/godotengine/godot/releases/download/$GodotVersion/Godot_v${GodotVersion}_mono_export_templates.tpz" `
	-OutFile $templateArchive
New-Item -ItemType Directory -Path $templateExtract -Force | Out-Null
tar -xf $templateArchive -C $templateExtract
if ($LASTEXITCODE -ne 0) {
	throw "Godot export template extraction failed with exit code $LASTEXITCODE."
}
New-Item -ItemType Directory -Path $templateDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $templateExtract "templates\*") -Destination $templateDirectory -Recurse -Force

$godot = Get-ChildItem -Path $godotDirectory -Recurse -File -Filter "Godot_*_mono_win64_console.exe" |
	Select-Object -First 1
if (-not $godot) {
	throw "Could not locate the Godot .NET console executable."
}

dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) {
	throw "dotnet restore failed with exit code $LASTEXITCODE."
}
New-Item -ItemType Directory -Path $ExportDirectory -Force | Out-Null

& $godot.FullName --headless --path $ProjectDirectory --import
if ($LASTEXITCODE -ne 0) {
	throw "Godot import failed with exit code $LASTEXITCODE."
}

$exportPath = Join-Path $ExportDirectory $ExportFile
# The Windows editor can remain alive after a successful command-line export
# (the pack is complete, but editor shutdown stalls). Exporting is synchronous,
# so quit after deferred import/export cleanup has had two main-loop iterations.
& $godot.FullName --headless --quit-after 2 --path $ProjectDirectory --export-release $ExportPreset $exportPath
if ($LASTEXITCODE -ne 0) {
	throw "Godot export failed with exit code $LASTEXITCODE."
}

$dataDirectory = Get-ChildItem -Path $ExportDirectory -Directory -Filter "data_*_windows_x86_64" |
	Select-Object -First 1
$managedAssembly = if ($dataDirectory) { Join-Path $dataDirectory.FullName "BrickVerse.dll" } else { "" }
$packPath = [System.IO.Path]::ChangeExtension($exportPath, ".pck")
if (
	-not (Test-Path -LiteralPath $exportPath -PathType Leaf) -or
	-not (Test-Path -LiteralPath $packPath -PathType Leaf) -or
	-not $dataDirectory -or
	-not (Test-Path -LiteralPath $managedAssembly -PathType Leaf)
) {
	Get-ChildItem -Path $ExportDirectory -Recurse | ForEach-Object { Write-Host $_.FullName }
	throw "Windows export is incomplete; expected the executable, PCK, and managed assembly data directory."
}

Write-Host "Windows export contents:"
Get-ChildItem -Path $ExportDirectory -Recurse | ForEach-Object { Write-Host $_.FullName }
