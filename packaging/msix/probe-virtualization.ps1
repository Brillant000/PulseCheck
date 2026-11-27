<#
  Checks, on this machine, whether a registry change made from INSIDE the installed PulseCheck package is
  visible to Windows (the thing the Fix buttons rely on). The package must be installed first.
  It only touches a throw-away value under HKCU\Software\PulseCheckProbe and removes it afterwards.

    ./packaging/msix/probe-virtualization.ps1
#>
param([string]$Pfn = '')
$ErrorActionPreference = 'Stop'
if (-not $Pfn) {
  $pkg = Get-AppxPackage -Name 'BBTraining.PulseCheck' | Select-Object -First 1
  if (-not $pkg) { throw 'PulseCheck package is not installed' }
  $Pfn = $pkg.PackageFamilyName
}
$key  = 'HKCU:\Software\PulseCheckProbe'
$work = Join-Path $env:TEMP 'pulsecheck-probe'
New-Item -ItemType Directory -Force $work | Out-Null
$inside = Join-Path $work 'inside.ps1'
$result = Join-Path $work 'inside.txt'
Remove-Item $result -ErrorAction SilentlyContinue

@"
try {
  `$r = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\PulseCheckProbe')
  `$r.SetValue('Existing', 0, 'DWord'); `$r.Close()
  'inside write ok' | Set-Content '$result'
} catch { 'inside ERROR ' + `$_.Exception.Message | Set-Content '$result' }
"@ | Set-Content $inside

Remove-Item $key -Recurse -ErrorAction SilentlyContinue
New-Item $key -Force | Out-Null
Set-ItemProperty $key -Name Existing -Value 1 -Type DWord      # an EXISTING value, like the real settings

Invoke-CommandInDesktopPackage -PackageFamilyName $Pfn -AppId PulseCheck -PreventBreakaway -Command 'powershell.exe' `
  -Args "-NoProfile -ExecutionPolicy Bypass -File `"$inside`""
$i = 0; while (-not (Test-Path $result) -and $i -lt 30) { Start-Sleep 1; $i++ }

$seen = (Get-ItemProperty $key).Existing
Remove-Item $key -Recurse -ErrorAction SilentlyContinue
"OS build: $([Environment]::OSVersion.Version)"
"Inside: $(if (Test-Path $result) { Get-Content $result } else { 'no answer' })"
if ($seen -eq 0) { "RESULT: VISIBLE - a packaged change to an existing HKCU value reaches Windows." }
else { "RESULT: VIRTUALIZED - the change stayed inside the package (Windows still sees $seen)." }
