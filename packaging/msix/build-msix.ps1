<#
  Builds PulseCheck.msix (needs the Windows SDK for makeappx.exe and the .NET 8 SDK).

    ./packaging/msix/build-msix.ps1 [-Name PulseCheck.Name -Publisher "CN=..." -Version 1.4.0.0] [-SignTest]

  -Name / -Publisher: the values from Partner Center > Product identity (defaults are test values).
  -SignTest: signs with a throw-away self-signed certificate so you can install the package locally.
             The Store ignores that signature and signs the package itself.
#>
param(
  [string]$Name = 'PulseCheck.Test',
  [string]$Publisher = 'CN=PulseCheck Test',
  [string]$PublisherDisplayName = 'PulseCheck',
  [string]$Version = '1.4.0.0',
  [string]$Out = 'dist-msix',
  [switch]$SignTest
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
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
$manifest = $manifest -replace 'Version="1.4.0.0"', "Version=`"$Version`""
[IO.File]::WriteAllText("$layout\AppxManifest.xml", $manifest, (New-Object Text.UTF8Encoding($false)))

function Find-SdkTool($tool) {
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
  $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
    -FriendlyName 'PulseCheck test' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  $pwd = ConvertTo-SecureString -String ([guid]::NewGuid().ToString()) -Force -AsPlainText
  $pfx = Join-Path $root "$Out\test.pfx"
  Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
  & $signtool sign /fd SHA256 /a /f $pfx /p ([System.Net.NetworkCredential]::new('', $pwd).Password) $msix
  if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
  Export-Certificate -Cert $cert -FilePath (Join-Path $root "$Out\test.cer") | Out-Null
  Remove-Item $pfx -Force
  Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
}

Write-Host "Done: $msix"
