[CmdletBinding()]
param(
	[Parameter(Mandatory)][string]$Collection,
	[Parameter(Mandatory)][ValidatePattern('^(ubuntu@22\.04|debian@(12|13)|rhel@9|rocky@9|almalinux@9)_(x64|arm64)$')][string]$Profile,
	[Parameter(Mandatory)][string]$BaseImageReference,
	[Parameter(Mandatory)][string]$Destination
)

$ErrorActionPreference = 'Stop'
$collectionPath = (Resolve-Path -LiteralPath $Collection).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $destinationPath) { throw 'The destination must not already exist.' }
if($BaseImageReference -notmatch '^.+@sha256:[a-f0-9]{64}$') { throw 'Supply the exact resolver image repository and manifest digest.' }
$metadata = Join-Path $collectionPath 'metadata.tsv'
$packages = @()
foreach($line in [IO.File]::ReadAllLines($metadata))
{
	$fields = $line.Split("`t")
	if($fields.Count -ne 6 -or [IO.Path]::GetFileName($fields[0]) -cne $fields[0] -or $fields[0] -match '[\\/:]' -or $fields[4] -notmatch '^https://') { throw 'Invalid collector metadata.' }
	$path = Join-Path $collectionPath $fields[0]
	$packages += [ordered]@{
		name = $fields[1]; version = $fields[2]; architecture = $fields[3]; path = 'packages/' + $fields[0]
		url = $fields[4]; dependencies = $fields[5]; hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); length = (Get-Item -LiteralPath $path).Length
	}
}
$engine = @($packages | Where-Object { $_.name -ceq 'docker-ce' })
$compose = @($packages | Where-Object { $_.name -ceq 'docker-compose-plugin' })
if($engine.Count -ne 1 -or $compose.Count -ne 1) { throw 'The collection must contain exactly one engine and Compose plugin.' }
$lock = [ordered]@{ mode='offline'; profile=$Profile; baseImageReference=$BaseImageReference; engineVersion=$engine[0].version; composeVersion=$compose[0].version; metadata='packages/metadata.tsv'; packages=$packages }
if($Profile -match '^(rhel|rocky|almalinux)@')
{
	$requirements = [IO.File]::ReadAllLines((Join-Path $collectionPath 'requirements.txt'))
	if($requirements.Count -eq 0 -or ($requirements | Sort-Object -Unique).Count -ne $requirements.Count) { throw 'Invalid RPM requirements.' }
	foreach($requirement in $requirements)
	{
		if($requirement -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._+-]*$' -or $packages.name -cnotcontains $requirement) { throw 'Invalid RPM requirement.' }
	}
	$repository = Join-Path $collectionPath 'repodata'
	if(!(Test-Path -LiteralPath (Join-Path $repository 'repomd.xml') -PathType Leaf)) { throw 'RPM repository metadata is required.' }
	$lock.requirements = @($requirements)
	$lock.repository = 'packages/repodata'
}
$packageDirectory = Join-Path $destinationPath 'packages'
New-Item -ItemType Directory -Path $packageDirectory | Out-Null
foreach($package in $packages) { Copy-Item -LiteralPath (Join-Path $collectionPath ([IO.Path]::GetFileName($package.path))) -Destination $packageDirectory }
Copy-Item -LiteralPath $metadata -Destination $packageDirectory
if($lock.repository) { Copy-Item -LiteralPath $repository -Destination (Join-Path $packageDirectory 'repodata') -Recurse }
$json = ($lock | ConvertTo-Json -Depth 10) -replace "`r?`n", "`r`n"
[IO.File]::WriteAllText((Join-Path $destinationPath 'bootstrap.lock.json'), $json + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Output $destinationPath
