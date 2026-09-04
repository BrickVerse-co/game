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

function Get-DirectoryFingerprint([string]$Directory) {
	if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
		return $null
	}

	$files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File)
	if ($files.Count -eq 0) {
		return $null
	}

	$totalLength = ($files | Measure-Object -Property Length -Sum).Sum
	return "$($files.Count):$totalLength"
}

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
New-Item -ItemType Directory -Path $ExportDirectory -Force | Out-Null

& $godot.FullName --headless --quiet --editor --path $ProjectDirectory --import
if ($LASTEXITCODE -ne 0) {
	throw "Godot import failed with exit code $LASTEXITCODE."
}

$exportPath = Join-Path $ExportDirectory $ExportFile
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectFile)
$managedDataDirectory = Join-Path $ExportDirectory "data_${projectName}_windows_x86_64"
$managedAssembly = Join-Path $managedDataDirectory "$projectName.dll"
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
$completedExport = $false
$lastManagedFingerprint = $null
$stableManagedPolls = 0

try {
	while (-not $exportProcess.HasExited) {
		if (Test-Path -LiteralPath $completionMarker -PathType Leaf) {
			# EditorExportPlugin._export_end can run before the .NET export plugin has
			# finished copying its sidecar payload. Do not terminate a stalled Godot
			# process until the managed directory exists and has stopped changing.
			$currentFingerprint = Get-DirectoryFingerprint $managedDataDirectory
			if (
				$currentFingerprint -and
				(Test-Path -LiteralPath $managedAssembly -PathType Leaf) -and
				(Get-Item -LiteralPath $managedAssembly).Length -gt 0
			) {
				if ($currentFingerprint -eq $lastManagedFingerprint) {
					$stableManagedPolls++
				} else {
					$lastManagedFingerprint = $currentFingerprint
					$stableManagedPolls = 0
				}

				if ($stableManagedPolls -ge 8) {
					$completedExport = $true
					break
				}
			}
		}
		if ([DateTime]::UtcNow -ge $exportDeadline) {
			$exportProcess.Kill($true)
			$exportProcess.WaitForExit()
			throw "Godot export did not complete within 15 minutes."
		}
		Start-Sleep -Milliseconds 250
	}

	if ($completedExport -and -not $exportProcess.WaitForExit(5000)) {
		Write-Warning "Godot finished exporting but stalled during shutdown; terminating the editor process."
		$exportProcess.Kill($true)
		$exportProcess.WaitForExit()
	}

	if (-not $completedExport -and $exportProcess.ExitCode -ne 0) {
		throw "Godot export failed with exit code $($exportProcess.ExitCode)."
	}
}
finally {
	Remove-Item -LiteralPath $completionMarker -Force -ErrorAction SilentlyContinue
	Remove-Item Env:BV_EXPORT_COMPLETE_MARKER -ErrorAction SilentlyContinue
	$exportProcess.Dispose()
}

$packPath = [System.IO.Path]::ChangeExtension($exportPath, ".pck")
if (
	-not (Test-Path -LiteralPath $exportPath -PathType Leaf) -or
	-not (Test-Path -LiteralPath $packPath -PathType Leaf) -or
	(Get-Item -LiteralPath $packPath).Length -eq 0 -or
	-not (Test-Path -LiteralPath $managedDataDirectory -PathType Container) -or
	-not (Test-Path -LiteralPath $managedAssembly -PathType Leaf) -or
	(Get-Item -LiteralPath $managedAssembly).Length -eq 0
) {
	Get-ChildItem -Path $ExportDirectory -Recurse | ForEach-Object { Write-Host $_.FullName }
	throw "Windows export is incomplete; expected a non-empty executable, PCK, and '$([System.IO.Path]::GetFileName($managedDataDirectory))' managed payload."
}

Write-Host "Windows export completed: $exportPath"
Write-Host "Export size: $([Math]::Round((Get-ChildItem -Path $ExportDirectory -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 2)) MB"
