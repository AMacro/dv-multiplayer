param (
    [switch]$NoArchive,
    [string]$OutputDirectory = $PSScriptRoot
)

Set-Location "$PSScriptRoot"
$FilesToInclude = "build/*"

$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id
$modVersion = $modInfo.Version

$DistDir = "$OutputDirectory/dist"
if ($NoArchive) {
    $ZipWorkDir = "$OutputDirectory"
} else {
    $ZipWorkDir = "$DistDir/tmp"
}
$ZipOutDir = "$ZipWorkDir/$modId"

New-Item "$ZipOutDir" -ItemType Directory -Force
Copy-Item -Force -Path $FilesToInclude -Destination "$ZipOutDir"

if (!$NoArchive)
{
    $FILE_NAME = "$DistDir/${modId}_v$modVersion.zip"
    Compress-Archive -Update -CompressionLevel Fastest -Path "$ZipOutDir/*" -DestinationPath "$FILE_NAME"
}
