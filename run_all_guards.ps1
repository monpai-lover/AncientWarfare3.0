$ErrorActionPreference = 'Continue'
$testsDir = Join-Path $PSScriptRoot 'Tests'
$guards = Get-ChildItem $testsDir -Filter '*.ps1' | Where-Object { $_.Name -ne 'run_all_guards.ps1' } | Sort-Object Name
$pass = @(); $fail = @()
foreach ($g in $guards) {
  $name = [IO.Path]::GetFileNameWithoutExtension($g.Name)
  if ($name -in @('run_relevant_guards','run_all_guards')) { continue }
  $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $g.FullName 2>&1
  $code = $LASTEXITCODE
  $threw = ($out | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] }).Count -gt 0
  if (($code -eq 0 -or $null -eq $code) -and -not $threw) { $pass += $name }
  else {
    $firstErr = ($out | Select-Object -First 1) -join ' '
    if ($firstErr.Length -gt 130) { $firstErr = $firstErr.Substring(0,130) }
    $fail += ,@($name, $firstErr)
  }
}
Write-Host "TOTAL=$($guards.Count) PASS=$($pass.Count) FAIL=$($fail.Count)"
Write-Host "=== FAILING ==="
foreach ($f in $fail) { Write-Host "FAIL`t$($f[0])`t$($f[1])" }
