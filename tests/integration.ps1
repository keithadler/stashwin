# End-to-end through the console twin, with the key and settings in a temp folder (STASH_HOME), never touching
# the real settings or a real destination. Run on Windows:  pwsh tests/integration.ps1 [path\to\stash.exe]
param([string]$Exe = "dist\win-arm64-cli\stash.exe")
$ErrorActionPreference = "Continue"
if (-not (Test-Path $Exe)) { $Exe = "dist\win-x64-cli\stash.exe" }
if (-not (Test-Path $Exe)) { Write-Host "no stash.exe; run scripts/publish.sh first"; exit 2 }
$root = Join-Path ([IO.Path]::GetTempPath()) ("stash-it-" + [guid]::NewGuid().ToString("N"))
$env:STASH_HOME = Join-Path $root "home"; $src = Join-Path $root "src"; $dest = Join-Path $root "dest"; $dest2 = Join-Path $root "dest2"; $out = Join-Path $root "out"
foreach ($d in @($env:STASH_HOME, "$src\photos", "$src\node_modules\x", $dest, $dest2, $out)) { New-Item -ItemType Directory -Force $d | Out-Null }
"hello" | Set-Content "$src\notes.txt"
$b = New-Object byte[] 4500000; (New-Object Random).NextBytes($b); [IO.File]::WriteAllBytes("$src\photos\big.bin", $b); Copy-Item "$src\photos\big.bin" "$src\photos\copy.bin"
[IO.File]::WriteAllText("$src\empty.txt", ""); "ignored" | Set-Content "$src\node_modules\x\i.js"
$fail = 0
function Check($name, [scriptblock]$test) { $ok = $false; try { $ok = & $test } catch { $ok = $false }; if ($ok) { Write-Host "ok    $name" } else { Write-Host "FAIL  $name"; $script:fail = 1 } }

Check "key new"                      { (& $Exe key new --json | Out-String) -match "fingerprint" }
Check "key new refuses a second"     { & $Exe key new *> $null; $LASTEXITCODE -ne 0 }
$words = (& $Exe key show --json | ConvertFrom-Json).words -join " "
Check "24 words"                     { ($words -split " ").Count -eq 24 }
Check "card text"                    { & $Exe key card "$out\card.txt" *> $null; (Get-Content "$out\card.txt" -Raw) -match "24 words" }
Check "card png"                     { & $Exe key card "$out\card.png" *> $null; (Get-Item "$out\card.png").Length -gt 1000 }
Check "add and dest"                 { & $Exe add $src *> $null; & $Exe dest $dest *> $null; & $Exe dest $dest2 *> $null; $LASTEXITCODE -eq 0 }
Check "status exits 0 when ready"    { & $Exe status --json *> $null; $LASTEXITCODE -eq 0 }
Check "backup"                       { (& $Exe backup --json | ConvertFrom-Json).results[0].new_chunks -eq 4 }
Check "two destinations written"     { (Test-Path "$dest\Stash for Mac") -and (Test-Path "$dest2\Stash for Mac") }
Check "chunks are ciphertext"        { $files = (Get-ChildItem -Recurse -File (Join-Path $dest "Stash for Mac") | Where-Object Name -ne README.txt).FullName; (Select-String -Path $files -Pattern "hello" -SimpleMatch | Measure-Object).Count -eq 0 }
Check "second backup uploads nothing" { (& $Exe backup --json | ConvertFrom-Json).results[0].new_chunks -eq 0 }
Check "snapshots listed"             { ((& $Exe snapshots --json | ConvertFrom-Json).snapshots).Count -eq 4 }
Check "verify ok"                    { $v = & $Exe verify --json | ConvertFrom-Json; ($v.results | ForEach-Object { $_.sample_ok }) -notcontains $false }
Check "restore identical"            { & $Exe restore latest "$out\r" *> $null; (Get-FileHash "$src\photos\big.bin").Hash -eq (Get-FileHash "$out\r\src\photos\big.bin").Hash -and (Get-Content "$out\r\src\notes.txt") -eq "hello" -and -not (Test-Path "$out\r\src\node_modules") }
Check "restore subtree"              { & $Exe restore latest "$out\s" --only photos *> $null; (Test-Path "$out\s\src\photos\big.bin") -and -not (Test-Path "$out\s\src\notes.txt") }
$chunk = Get-ChildItem -Recurse -File (Join-Path $dest "Stash for Mac") | Where-Object { $_.DirectoryName -match "chunks" } | Select-Object -First 1
[IO.File]::WriteAllBytes($chunk.FullName, ([IO.File]::ReadAllBytes($chunk.FullName) + [byte]120))
Check "tampered chunk reported"      { $v = & $Exe verify --json; $code = $LASTEXITCODE; $code -eq 2 -and (($v | ConvertFrom-Json).results | Where-Object { $_.bad.Count -gt 0 }) }
Check "key forget then restore words" { & $Exe key forget *> $null; & $Exe key restore "$words" *> $null; ((& $Exe snapshots --json | ConvertFrom-Json).snapshots).Count -gt 0 }
Check "wrong word rejected"          { & $Exe key restore ("zoo " + ($words -replace "^\S+ ", "")) *> $null; $LASTEXITCODE -ne 0 }
Check "schedule signed-out is S4U"   { & $Exe schedule daily --when-signed-out *> $null; $LASTEXITCODE -eq 0 -and ((schtasks /query /tn "Stash for Windows (test)" /xml | Out-String) -match "<LogonType>S4U</LogonType>") -and ((schtasks /query /tn "Stash for Windows (test)" /xml | Out-String) -match "<StartWhenAvailable>true</StartWhenAvailable>") }
Check "schedule off removes it"      { & $Exe schedule off *> $null; schtasks /query /tn "Stash for Windows (test)" *> $null; $LASTEXITCODE -ne 0 }
Check "selftest"                     { (& $Exe selftest | Select-Object -Last 1) -match "0 failed" }
Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
if ($fail -eq 0) { Write-Host "integration: all passed" } else { Write-Host "integration: FAILURES" }
exit $fail
