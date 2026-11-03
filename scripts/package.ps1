param([ValidateSet('linux-x64','linux-arm64','win-x64')][string]$Runtime='linux-x64',[string]$DotnetPath='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=(Get-Command $DotnetPath -ErrorAction Stop).Source
$projectPath=Join-Path $root 'src/Jellyfin.Plugin.Plm/Jellyfin.Plugin.Plm.csproj'
$project=[xml](Get-Content -LiteralPath $projectPath -Raw)
$version=[string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$') { throw 'Invalid package version.' }
$stage=Join-Path $root "artifacts/package-$version-$Runtime"
& $dotnet publish $projectPath -c Release -r $Runtime --self-contained false -p:ContinuousIntegrationBuild=true -p:DebugType=none -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage
Copy-Item -Path (Join-Path $root 'licenses/*') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'translations/NOTICE.md') -Destination (Join-Path $stage 'NOTICE-Translations.md')
# Exclude debug symbols and host Jellyfin assemblies. Native SQLite is RID-specific.
$files=Get-ChildItem -LiteralPath $stage -File | Where-Object { $_.Extension -ne '.pdb' }
if ($files.Name -match '^MediaBrowser\.|^Jellyfin\.(Controller|Model|Common|Data|Database)') { throw 'Host assemblies must not be packaged.' }
$archive=Join-Path $root "artifacts/plm-$version-$Runtime.zip"
Compress-Archive -LiteralPath $files.FullName -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
$receipt=[ordered]@{
    releaseVersion=$version
    version=[string]$project.Project.PropertyGroup.AssemblyVersion
    targetAbi='12.4.0.0'
    runtime=$Runtime
    filename=[IO.Path]::GetFileName($archive)
    sourceUrl="https://github.com/guiguichinelo/plm_plugin/releases/download/v$version/$([IO.Path]::GetFileName($archive))"
    checksum=(Get-FileHash -LiteralPath $archive -Algorithm MD5).Hash.ToLowerInvariant()
    sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    timestamp=[DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
}
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $sourceCommit=(& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify source commit.' }
    $receipt.sourceCommit=$sourceCommit
}
$utf8=New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $root "artifacts/plm-$version-$Runtime.json"),($receipt | ConvertTo-Json)+[Environment]::NewLine,$utf8)
[IO.File]::WriteAllText((Join-Path $root 'artifacts/SHA256SUMS.txt'),"$($receipt.sha256)  $($receipt.filename)"+[Environment]::NewLine,$utf8)
