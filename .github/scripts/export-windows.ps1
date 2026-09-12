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

$godot = Get-ChildItem -Path $godotDirectory -Recurse -File -Filter "Godot_*_mono_win64_console.exe" -ErrorAction SilentlyContinue |
	Select-Object -First 1
if (-not $godot) {
	curl.exe --fail --location --retry 3 --output $godotArchive `
		"https://github.com/godotengine/godot/releases/download/$GodotVersion/Godot_v${GodotVersion}_mono_win64.zip"
	if ($LASTEXITCODE -ne 0) { throw "Godot download failed with exit code $LASTEXITCODE." }
	New-Item -ItemType Directory -Path $godotDirectory -Force | Out-Null
	tar.exe -xf $godotArchive -C $godotDirectory
	if ($LASTEXITCODE -ne 0) { throw "Godot extraction failed with exit code $LASTEXITCODE." }
	$godot = Get-ChildItem -Path $godotDirectory -Recurse -File -Filter "Godot_*_mono_win64_console.exe" |
		Select-Object -First 1
}

if (-not $godot) {
	throw "Could not locate the Godot .NET console executable."
}

$releaseTemplate = Join-Path $templateDirectory "windows_release_x86_64.exe"
if (-not (Test-Path -LiteralPath $releaseTemplate -PathType Leaf)) {
	curl.exe --fail --location --retry 3 --output $templateArchive `
		"https://github.com/godotengine/godot/releases/download/$GodotVersion/Godot_v${GodotVersion}_mono_export_templates.tpz"
	if ($LASTEXITCODE -ne 0) { throw "Godot template download failed with exit code $LASTEXITCODE." }
	New-Item -ItemType Directory -Path $templateExtract -Force | Out-Null
	tar.exe -xf $templateArchive -C $templateExtract
	if ($LASTEXITCODE -ne 0) { throw "Godot template extraction failed with exit code $LASTEXITCODE." }
	New-Item -ItemType Directory -Path $templateDirectory -Force | Out-Null
	Copy-Item -Path (Join-Path $templateExtract "templates\*") -Destination $templateDirectory -Recurse -Force
}

dotnet restore $ProjectFile --nologo
if ($LASTEXITCODE -ne 0) {
	throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet build $ProjectFile --configuration ExportRelease --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
	throw "dotnet build failed with exit code $LASTEXITCODE."
}
dotnet build $ProjectFile --configuration Debug --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
	throw "dotnet Debug build failed with exit code $LASTEXITCODE."
}
New-Item -ItemType Directory -Path $ExportDirectory -Force | Out-Null

$generatedProjectState = @(
	(Join-Path $ProjectDirectory ".godot\editor"),
	(Join-Path $ProjectDirectory ".godot\uid_cache.bin"),
	(Join-Path $ProjectDirectory ".godot\extension_list.cfg"),
	(Join-Path $ProjectDirectory ".godot\global_script_class_cache.cfg")
)
foreach ($path in $generatedProjectState) {
	Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
}

& $godot.FullName --headless --editor --path $ProjectDirectory --import --quit
if ($LASTEXITCODE -ne 0) {
	throw "Godot import failed with exit code $LASTEXITCODE."
}

$requiredFontImports = @(
	@{
		Source = "assets/fonts/Montserrat-Medium.ttf"
		Import = "assets/fonts/Montserrat-Medium.ttf.import"
	},
	@{
		Source = "assets/fonts/emoji/twemoji.ttf"
		Import = "assets/fonts/emoji/twemoji.ttf.import"
	}
)
foreach ($font in $requiredFontImports) {
	$sourcePath = Join-Path $ProjectDirectory $font.Source
	$importPath = Join-Path $ProjectDirectory $font.Import
	if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
		throw "Required font source is missing after checkout: $sourcePath"
	}
	if (-not (Test-Path -LiteralPath $importPath -PathType Leaf)) {
		throw "Required font import metadata is missing: $importPath"
	}

	$importContents = Get-Content -LiteralPath $importPath -Raw
	$destinationMatch = [regex]::Match($importContents, 'dest_files=\["([^"]+)"\]')
	if (-not $destinationMatch.Success) {
		throw "Godot import metadata does not declare a generated resource: $importPath"
	}

	$generatedPath = $destinationMatch.Groups[1].Value -replace '^res://', ''
	$generatedResourcePath = Join-Path $ProjectDirectory ($generatedPath -replace '/', '\\')
	if (-not (Test-Path -LiteralPath $generatedResourcePath -PathType Leaf)) {
		throw "Godot did not generate the imported font resource: $generatedResourcePath"
	}
}

$exportPath = Join-Path $ExportDirectory $ExportFile
$completionMarker = Join-Path $env:RUNNER_TEMP "brickverse-export-$([Guid]::NewGuid().ToString('N')).complete"
$env:BV_EXPORT_COMPLETE_MARKER = $completionMarker
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $godot.FullName
$startInfo.UseShellExecute = $false
foreach ($argument in @(
	"--headless",
	"--path", $ProjectDirectory,
	"--export-release", $ExportPreset, $exportPath
)) {
	[void]$startInfo.ArgumentList.Add($argument)
}

$exportProcess = [System.Diagnostics.Process]::Start($startInfo)
$exportDeadline = [DateTime]::UtcNow.AddMinutes(15)
$completionMarkerSeen = $false

try {
	while (-not $exportProcess.HasExited) {
		if (Test-Path -LiteralPath $completionMarker -PathType Leaf) {
			$completionMarkerSeen = $true
		}
		if ([DateTime]::UtcNow -ge $exportDeadline) {
			$exportProcess.Kill($true)
			$exportProcess.WaitForExit()
			throw "Godot export did not complete within 15 minutes."
		}
		Start-Sleep -Milliseconds 250
	}

	$exportProcess.WaitForExit()
	$exportExitCode = $exportProcess.ExitCode
	$completionMarkerSeen = $completionMarkerSeen -or (Test-Path -LiteralPath $completionMarker -PathType Leaf)
	if ($exportExitCode -ne 0) {
		throw "Godot export failed with exit code $exportExitCode."
	}
	if (-not $completionMarkerSeen) {
		throw "Godot export exited successfully without the export completion marker."
	}
}
finally {
	Remove-Item -LiteralPath $completionMarker -Force -ErrorAction SilentlyContinue
	Remove-Item Env:BV_EXPORT_COMPLETE_MARKER -ErrorAction SilentlyContinue
	$exportProcess.Dispose()
}

$packPath = [System.IO.Path]::ChangeExtension($exportPath, ".pck")
$requiredNativeLibraries = @(
	"Luau.VM.dll",
	"Luau.Compiler.dll",
	"discord_game_sdk.dll"
)
$missingNativeLibraries = @($requiredNativeLibraries | Where-Object {
	-not (Get-ChildItem -LiteralPath $ExportDirectory -Recurse -File -Filter $_ -ErrorAction SilentlyContinue)
})
if (
	-not (Test-Path -LiteralPath $exportPath -PathType Leaf) -or
	-not (Test-Path -LiteralPath $packPath -PathType Leaf) -or
	(Get-Item -LiteralPath $packPath).Length -eq 0 -or
	$missingNativeLibraries.Count -gt 0
) {
	Get-ChildItem -Path $ExportDirectory -Recurse | ForEach-Object { Write-Host $_.FullName }
	throw "Windows export is incomplete; expected a non-empty executable, PCK, and the required native payload: $($requiredNativeLibraries -join ', ')."
}

Write-Host "Windows export completed: $exportPath"
Write-Host "Export size: $([Math]::Round((Get-ChildItem -Path $ExportDirectory -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 2)) MB"
