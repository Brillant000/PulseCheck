<#
  Measures, on this machine, how the installed PulseCheck package sees the registry:
    READ  - does a process inside the package see a value that Windows changes outside it (live view)?
    WRITE - does a change made inside the package reach Windows?
  It only touches a throw-away value under HKCU\Software\PulseCheckProbe and removes it afterwards.
  The package must be installed first.

    ./packaging/msix/probe-virtualization.ps1
#>
param([string]$Pfn = '')
$ErrorActionPreference = 'Stop'
if (-not $Pfn) {
  $pkg = Get-AppxPackage -Name 'BBTraining.PulseCheck' | Select-Object -First 1
  if (-not $pkg) { throw 'PulseCheck package is not installed' }
  $Pfn = $pkg.PackageFamilyName
}
$keyPath = 'Software\PulseCheckProbe'
$key     = "HKCU:\$keyPath"
# Outside AppData on purpose: file writes under AppData are virtualized too, so the answer file would stay invisible.
$work    = Join-Path $env:PUBLIC 'pulsecheck-probe'
New-Item -ItemType Directory -Force $work | Out-Null
$result = Join-Path $work 'inside.txt'

function Invoke-Inside([string]$code) {
  $script = Join-Path $work 'inside.ps1'
  Remove-Item $result -ErrorAction SilentlyContinue
  "& { try { $code } catch { 'ERROR ' + `$_.Exception.Message } } | Set-Content '$result'" | Set-Content $script
  Invoke-CommandInDesktopPackage -PackageFamilyName $Pfn -AppId PulseCheck -PreventBreakaway -Command 'powershell.exe' `
    -Args "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
  $i = 0; while (-not (Test-Path $result) -and $i -lt 30) { Start-Sleep 1; $i++ }
  if (Test-Path $result) { (Get-Content $result -Raw).Trim() } else { 'no answer' }
}
$readCode  = "[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('$keyPath').GetValue('Existing')"
$writeCode = "`$r = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('$keyPath'); `$r.SetValue('Existing', 0, 'DWord'); `$r.Close(); 'written'"

Remove-Item $key -Recurse -ErrorAction SilentlyContinue
New-Item $key -Force | Out-Null
try {
  Set-ItemProperty $key -Name Existing -Value 1 -Type DWord
  $r1 = Invoke-Inside $readCode                                   # inside should see 1
  Set-ItemProperty $key -Name Existing -Value 2 -Type DWord
  $r2 = Invoke-Inside $readCode                                   # inside should see 2 if the view is live
  Set-ItemProperty $key -Name Existing -Value 3 -Type DWord
  $w  = Invoke-Inside $writeCode                                  # inside writes 0
  $seen = (Get-ItemProperty $key).Existing                        # outside sees 0 if the write reached Windows
} finally { Remove-Item $key -Recurse -ErrorAction SilentlyContinue }

"OS build: $([Environment]::OSVersion.Version)"
"READ : Windows had 1 -> inside saw [$r1]; Windows then had 2 -> inside saw [$r2]   => " + $(if ($r1 -eq '1' -and $r2 -eq '2') { 'LIVE view' } else { 'STALE or different view' })
"WRITE: inside wrote 0 ($w); Windows (had 3) now has [$seen]   => " + $(if ($seen -eq 0) { 'VISIBLE to Windows' } else { 'VIRTUALIZED (stays in the package)' })
