param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repository "executor/src/bin/$Configuration/net10.0/win-x64/publish"
$logs = Join-Path (Split-Path $output) 'logs'
if([IO.Path]::GetFullPath($output) -ne [IO.Path]::Combine($repository, 'executor', 'src', 'bin', $Configuration, 'net10.0', 'win-x64', 'publish')) { throw 'Invalid runtime publication directory.' }
if(Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $logs | Out-Null

& dotnet publish (Join-Path $repository 'executor/src/Zongsoft.Tools.Migrator.Executor.csproj') --configuration $Configuration --runtime win-x64 --self-contained -p:TrimmerSingleWarn=false -p:IlcSingleWarn=false 2>&1 | Tee-Object -FilePath (Join-Path $logs 'publish.log')
if($LASTEXITCODE -ne 0) { throw 'Native AOT publishing failed for win-x64.' }

foreach($name in @('Zongsoft.Tools.Migrator.Executor.exe', 'duckdb.dll', 'e_sqlite3.dll')) {
	$path = Join-Path $output $name
	$stream = [IO.File]::OpenRead($path)
	$reader = [IO.BinaryReader]::new($stream)
	try {
		if($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid PE image: $name" }
		$stream.Position = 60
		$stream.Position = $reader.ReadInt32()
		if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Invalid x64 PE image: $name" }
	}
	finally { $reader.Dispose() }
}

$symbols = Join-Path (Split-Path $output) 'symbols'
New-Item -ItemType Directory -Force -Path $symbols | Out-Null
Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Extension -in '.pdb', '.dbg', '.lib', '.exp' | ForEach-Object {
	Move-Item -LiteralPath $_.FullName -Destination (Join-Path $symbols $_.Name) -Force
}

Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output 'Zongsoft.Tools.Migrator.Executor.exe') | Format-List | Out-File (Join-Path $logs 'identity.txt')
