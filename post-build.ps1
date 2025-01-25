param
(   
    [switch]$NoArchive,
    [string]$GameDir,
    [string]$Target,
	[string]$Ver
)

Write-Host "Root: $PSScriptRoot"
Write-Host "No Archive: $NoArchive"
Write-Host "Target: $Target"
Write-Host "Game Dir: $GameDir"
Write-Host "Version: $Ver"

$compress

#Update the JSON
$json = Get-Content ($PSScriptRoot + '/info.json') -raw | ConvertFrom-Json
$json.Version = $Ver
$modId = $json.Id
$json | ConvertTo-Json -depth 32| set-content ($PSScriptRoot + '/info.json')

#Copy files to Build Dir
Copy-Item ($PSScriptRoot + '/info.json') -Destination ("$PSScriptRoot/build/")
Copy-Item ($Target) -Destination ("$PSScriptRoot/build/")
Copy-Item ($PSScriptRoot + '/LICENSE') -Destination ("$PSScriptRoot/build/")

#Copy files to Game Dir
if (!(Test-Path ($GameDir))) {
	New-Item -ItemType Directory -Path $GameDir
}
Copy-Item ("$PSScriptRoot/build/*") -Destination ($GameDir)


#Files to be compressed if we make a zip
$compress = @{
	Path = ($PSScriptRoot + "/build/*")
	CompressionLevel = "Fastest"
	DestinationPath = ($PSScriptRoot + "/Releases/$modId $Ver.zip")
}

#Are we building a release or debug?
if (!$NoArchive){
	if (!(Test-Path ($PSScriptRoot + "/releases"))) {
		New-Item -ItemType Directory -Path ($PSScriptRoot + "/releases")
	}
	
	Write-Host "Zip Path: " $compress.DestinationPath
    Compress-Archive @compress -Force
}