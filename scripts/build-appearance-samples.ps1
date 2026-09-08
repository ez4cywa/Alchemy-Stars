$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repositoryRoot 'fork\AlchemyStars\src\AlchemyStars.Avalonia\Assets\AppleBlue'
$destination = Join-Path $repositoryRoot 'docs\samples\appearance\icons-template.zip'
# Package existing, unchanged artwork with all supported functional filenames.
# Regenerating this small sample archive is an explicit build operation.
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipArchive]::new([System.IO.File]::Create($destination), [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $source -Filter '*.png' | Sort-Object Name)) {
        $entry = $archive.CreateEntry($file.Name, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $inputStream = [System.IO.File]::OpenRead($file.FullName)
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) }
        finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
}
finally { $archive.Dispose() }
Write-Output "Appearance icon sample built: $destination"
