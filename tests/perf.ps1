# Throughput and memory on a synthetic folder: N files of 4 MB (default 250 -> 1 GB), backed up to a local folder,
# then verified and restored. Not part of CI; run it by hand:  pwsh tests/perf.ps1 [files] [path\to\stash.exe]
param([int]$N = 250, [string]$Exe = "dist\win-arm64-cli\stash.exe")
if (-not (Test-Path $Exe)) { $Exe = "dist\win-x64-cli\stash.exe" }
$root = Join-Path ([IO.Path]::GetTempPath()) ("stash-perf-" + [guid]::NewGuid().ToString("N"))
$env:STASH_HOME = Join-Path $root "home"; $src = Join-Path $root "src"; $dest = Join-Path $root "dest"; $out = Join-Path $root "out"
foreach ($d in @($env:STASH_HOME, $src, $dest, $out)) { New-Item -ItemType Directory -Force $d | Out-Null }
Write-Host "generating $N x 4 MB files..."
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create(); $buf = New-Object byte[] 4194304
for ($i = 1; $i -le $N; $i++) { $rng.GetBytes($buf); [IO.File]::WriteAllBytes("$src\f$i.bin", $buf) }
& $Exe key new *> $null; & $Exe add $src *> $null; & $Exe dest $dest *> $null
function Timed($label, [string[]]$cmdArgs) {
    $p = Start-Process -FilePath $Exe -ArgumentList $cmdArgs -PassThru -NoNewWindow -RedirectStandardOutput "$root\out.txt" -Wait
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Exe -ArgumentList $cmdArgs -PassThru -NoNewWindow -RedirectStandardOutput "$root\out2.txt"
    $peak = 0; while (-not $p.HasExited) { try { $p.Refresh(); $peak = [Math]::Max($peak, $p.PeakWorkingSet64) } catch {}; Start-Sleep -Milliseconds 50 }
    $sw.Stop()
    Write-Host ("== {0}: {1:0.0} s, peak {2:0} MB. {3}" -f $label, $sw.Elapsed.TotalSeconds, ($peak / 1MB), (Get-Content "$root\out2.txt" -Raw).Trim())
}
# first run is the real one; Timed runs each command twice so the second-backup number is honest
$sw = [Diagnostics.Stopwatch]::StartNew(); $p = Start-Process -FilePath $Exe -ArgumentList @("backup") -PassThru -NoNewWindow -RedirectStandardOutput "$root\o1.txt"; $peak = 0; while (-not $p.HasExited) { try { $p.Refresh(); $peak = [Math]::Max($peak, $p.PeakWorkingSet64) } catch {}; Start-Sleep -Milliseconds 50 }; $sw.Stop()
Write-Host ("== first backup (all new): {0:0.0} s, peak {1:0} MB. {2}" -f $sw.Elapsed.TotalSeconds, ($peak / 1MB), (Get-Content "$root\o1.txt" -Raw).Trim())
$sw = [Diagnostics.Stopwatch]::StartNew(); $p = Start-Process -FilePath $Exe -ArgumentList @("backup") -PassThru -NoNewWindow -RedirectStandardOutput "$root\o2.txt"; $peak = 0; while (-not $p.HasExited) { try { $p.Refresh(); $peak = [Math]::Max($peak, $p.PeakWorkingSet64) } catch {}; Start-Sleep -Milliseconds 50 }; $sw.Stop()
Write-Host ("== second backup (nothing new): {0:0.0} s, peak {1:0} MB. {2}" -f $sw.Elapsed.TotalSeconds, ($peak / 1MB), (Get-Content "$root\o2.txt" -Raw).Trim())
$sw = [Diagnostics.Stopwatch]::StartNew(); $p = Start-Process -FilePath $Exe -ArgumentList @("verify") -PassThru -NoNewWindow -RedirectStandardOutput "$root\o3.txt"; $peak = 0; while (-not $p.HasExited) { try { $p.Refresh(); $peak = [Math]::Max($peak, $p.PeakWorkingSet64) } catch {}; Start-Sleep -Milliseconds 50 }; $sw.Stop()
Write-Host ("== verify: {0:0.0} s, peak {1:0} MB. {2}" -f $sw.Elapsed.TotalSeconds, ($peak / 1MB), (Get-Content "$root\o3.txt" -Raw).Trim())
$sw = [Diagnostics.Stopwatch]::StartNew(); $p = Start-Process -FilePath $Exe -ArgumentList @("restore", "latest", $out) -PassThru -NoNewWindow -RedirectStandardOutput "$root\o4.txt"; $peak = 0; while (-not $p.HasExited) { try { $p.Refresh(); $peak = [Math]::Max($peak, $p.PeakWorkingSet64) } catch {}; Start-Sleep -Milliseconds 50 }; $sw.Stop()
Write-Host ("== restore: {0:0.0} s, peak {1:0} MB. {2}" -f $sw.Elapsed.TotalSeconds, ($peak / 1MB), (Get-Content "$root\o4.txt" -Raw).Trim())
Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
