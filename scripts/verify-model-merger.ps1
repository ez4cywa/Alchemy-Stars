param([string]$Executable = '', [switch]$RealHawk)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $Executable) { $Executable = Join-Path $repositoryRoot 'third_party\modelmerger\rust\target\release\alchemy-model-merger.exe' }
$taskOutput = Join-Path $repositoryRoot ('output\model-merger-protocol-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($taskOutput) | Out-Null

function Invoke-Merger($Request) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
    $process = [System.Diagnostics.Process]::Start($start)
    $errors = $process.StandardError.ReadToEndAsync()
    try {
        $process.StandardInput.WriteLine(($Request | ConvertTo-Json -Depth 10 -Compress))
        $process.StandardInput.Flush()
        $result = $null
        while ($null -ne ($line = $process.StandardOutput.ReadLine())) {
            $event = $line | ConvertFrom-Json
            switch ($event.event) {
                'prepared' {
                    if (Test-Path -LiteralPath $event.output_path) { throw 'Smoke must never overwrite an existing file.' }
                    $process.StandardInput.WriteLine('{"command":"execute","overwrite":false}')
                    $process.StandardInput.Flush()
                }
                'error' { throw $event.message }
                'completed' { $result = $event }
                'analysis' { $result = $event }
                'preview' { $result = $event }
            }
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -or $null -eq $result) { throw "Native engine failed: $($errors.GetAwaiter().GetResult())" }
        return $result
    } finally {
        if (-not $process.HasExited) {
            $process.StandardInput.Close()
            $process.StandardOutput.ReadToEnd() | Out-Null
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

$fixtureRoot = Join-Path $repositoryRoot 'third_party\modelmerger\tests\fixtures\rust-migration\golden-small'
$parts = @((Join-Path $fixtureRoot 'part-00.cast'), (Join-Path $fixtureRoot 'part-01.cast'))
$before = @($parts | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash })
$merged = Invoke-Merger @{command='merge';input_files=$parts;output_directory=$taskOutput;output_file_name='golden.cast';overwrite=$false}
if ($merged.part_count -ne 2 -or $merged.mesh_count -ne 2 -or $merged.bone_count -ne 2) { throw 'Golden merge statistics changed.' }
$preview = Invoke-Merger @{command='preview';file_path=$merged.output_path;triangle_limit=250000}
if ($preview.displayed_triangle_count -le 0 -or $preview.displayed_triangle_count -gt 250000) { throw 'Preview triangle contract failed.' }
$after = @($parts | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash })
if (Compare-Object $before $after) { throw 'Read-only inputs were modified.' }
Write-Output 'Golden merge + bounded preview + source immutability: PASS'

if ($RealHawk) {
    $hawkRoot = 'D:\_tiqu\Saluki\exported_files\bo7\models\sat_vm_ar_hawk_rec'
    $hawkParts = @('rec','bar','mag','pgrip','stock' | ForEach-Object { Join-Path $hawkRoot "sat_vm_ar_hawk_${_}_LOD0.cast" })
    foreach ($path in $hawkParts) { if (-not (Test-Path -LiteralPath $path)) { throw "Hawk input missing: $path" } }
    $hawkHashes = @($hawkParts | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash })
    $hawk = Invoke-Merger @{command='merge';input_files=$hawkParts;output_directory=$taskOutput;output_file_name='hawk.cast';manual_root_file=$hawkParts[0];overwrite=$false}
    $view = Invoke-Merger @{command='preview';file_path=$hawk.output_path;triangle_limit=250000}
    $ammo = Invoke-Merger @{command='inspect_ammunition';file_path=$hawk.output_path}
    if ($view.source_vertex_count -le 0 -or $hawk.part_count -ne 5) { throw 'Real Hawk output is incomplete.' }
    if (Compare-Object $hawkHashes @($hawkParts | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash })) { throw 'Hawk input files changed.' }
    Write-Output "Hawk 5-part merge: PASS ($($hawk.bone_count) bones, $($hawk.mesh_count) meshes, $($view.source_vertex_count) vertices); magazine analysis: $(@($ammo.magazines).Count)"
    $ammoFile = 'D:\_tiqu\Saluki\exported_files\bo7\models\ammo_unspent_9p\ammo_unspent_9p_LOD0.cast'
    if ((Test-Path -LiteralPath $ammoFile) -and @($ammo.magazines).Count -gt 0) {
        $ammoHash = (Get-FileHash -LiteralPath $ammoFile).Hash
        $filled = Invoke-Merger @{command='fill_ammunition';weapon=$hawk.output_path;ammunition=$ammoFile;output=(Join-Path $taskOutput 'hawk_filled.cast');magazines=@($ammo.magazines[0].name);extra_slots=@();replicas=@()}
        if ($filled.inserted -le 0) { throw 'Real ammunition filling inserted no meshes.' }
        $filledView = Invoke-Merger @{command='preview';file_path=$filled.output_path;triangle_limit=250000}
        if ($filledView.source_mesh_count -le $view.source_mesh_count -or (Get-FileHash -LiteralPath $ammoFile).Hash -ne $ammoHash) { throw 'Ammunition filling result/immutability failed.' }
        Write-Output "Real CAST ammunition fill + result preview: PASS ($($filled.inserted) inserted, $($filled.skipped) skipped)"
    }
}
Write-Output "Verified outputs: $taskOutput"
