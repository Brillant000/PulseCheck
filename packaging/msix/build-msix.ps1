<#
  Builds PulseCheck.msix (needs the Windows SDK for makeappx.exe and the .NET 8 SDK).

    ./packaging/msix/build-msix.ps1 [-Name PulseCheck.Name -Publisher "CN=..." -Version 1.4.1.0] [-SignTest]

  -Name / -Publisher: the values from Partner Center > Product identity (defaults are test values).
  -Version: defaults to <Version> in src/PulseCheck.csproj plus ".0" (the Store needs a last number of 0).
  -SignTest: signs with a throw-away self-signed certificate so you can install the package locally.
             The Store ignores that signature and signs the package itself.
#>
param(
  [string]$Name = 'PulseCheck.Test',
  [string]$Publisher = 'CN=PulseCheck Test',
  [string]$PublisherDisplayName = 'PulseCheck',
  [string]$Version = '',
  [string]$Out = 'dist-msix',
  [string]$SdkBin = '',        # folder holding makeappx.exe and signtool.exe (e.g. from the Microsoft.Windows.SDK.BuildTools NuGet package)
  [switch]$SignTest
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Version) {
  $csproj = Get-Content "$root\src\PulseCheck.csproj" -Raw
  if ($csproj -notmatch '<Version>(\d+\.\d+\.\d+)</Version>') { throw 'Cannot read <Version> from PulseCheck.csproj' }
  $Version = "$($Matches[1]).0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Store package versions must look like 1.2.3.0, got '$Version'" }
Write-Host "Package version: $Version"
$layout = Join-Path $root "$Out\layout"
Remove-Item (Join-Path $root $Out) -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $layout | Out-Null

Write-Host 'Publishing...'
dotnet publish "$root\src\PulseCheck.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $layout
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Get-ChildItem $layout -Filter *.pdb | Remove-Item -Force

Copy-Item "$PSScriptRoot\Assets" "$layout\Assets" -Recurse
$manifest = Get-Content "$PSScriptRoot\AppxManifest.xml" -Raw
$manifest = $manifest -replace 'Name="PulseCheck.Test"', "Name=`"$Name`""
$manifest = $manifest -replace 'Publisher="CN=PulseCheck Test"', "Publisher=`"$Publisher`""
$manifest = $manifest -replace '<PublisherDisplayName>.*?</PublisherDisplayName>', "<PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>"
$manifest = $manifest -replace '(<Identity\b[^>]*?\bVersion=")[^"]*(")', "`${1}$Version`$2"
[IO.File]::WriteAllText("$layout\AppxManifest.xml", $manifest, (New-Object Text.UTF8Encoding($false)))

function Find-SdkTool($tool) {
  if ($SdkBin) {
    $p = Join-Path $SdkBin $tool
    if (-not (Test-Path $p)) { throw "$tool not found in -SdkBin '$SdkBin'" }
    return (Resolve-Path $p).Path
  }
  $c = Get-Command $tool -ErrorAction SilentlyContinue
  if ($c) { return $c.Source }
  $f = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter $tool -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $f) { throw "$tool not found: install the Windows 10/11 SDK" }
  $f.FullName
}

$makeappx = Find-SdkTool 'makeappx.exe'
$msix = Join-Path $root "$Out\PulseCheck.msix"
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

if ($SignTest) {
  $signtool = Find-SdkTool 'signtool.exe'
  # One reusable test certificate per machine (kept in the current user's store), so it only has to be trusted once.
  $friendly = 'PulseCheck local test'
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.FriendlyName -eq $friendly -and $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date).AddDays(7) } |
          Sort-Object NotAfter -Descending | Select-Object -First 1
  if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
      -FriendlyName $friendly -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddMonths(6) `
      -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  }
  & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $msix
  if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
  Export-Certificate -Cert $cert -FilePath (Join-Path $root "$Out\test.cer") | Out-Null
  Write-Host "Test certificate thumbprint: $($cert.Thumbprint)  (remove it later with: Remove-Item Cert:\CurrentUser\My\$($cert.Thumbprint))"
}

Write-Host "Done: $msix"
