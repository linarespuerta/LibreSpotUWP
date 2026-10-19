param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..\LibreSpotUWP'),
    [string]$GeneratedRoot,
    [string]$PackagePath,
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')

if ($PackagePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $inspection = Join-Path $env:TEMP ('librespot-package-uris-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $inspection | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
    try {
        $pri = Join-Path $inspection 'resources.pri'
        [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('resources.pri'), $pri)
        $dumpPath = Join-Path $inspection 'resources.xml'
        $makePri = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.19041.0\x64\makepri.exe'
        & $makePri dump /if $pri /of $dumpPath /dt detailed | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect package PRI.' }
        [xml]$dump = Get-Content -LiteralPath $dumpPath -Raw
        $resources = @{}
        foreach ($node in $dump.SelectNodes('//NamedResource')) {
            $key = ([Uri]$node.uri).AbsolutePath
            $resources[$key] = $true
        }
        $stream = $zip.GetEntry('LibreSpotUWP.dll').Open()
        $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
        finally { $stream.Dispose(); $memory.Dispose() }
        $uris = @{}
        foreach ($offset in @(0, 1)) {
            $text = [Text.Encoding]::Unicode.GetString($bytes, $offset, $bytes.Length - $offset)
            foreach ($match in [regex]::Matches($text, 'ms-appx:///[^\x00\s"<>]{1,240}\.xaml')) {
                $uris[$match.Value] = $true
            }
        }
        if ($uris.Count -eq 0) { throw 'No native executable XAML URIs found.' }
        $missing = @()
        foreach ($uri in $uris.Keys) {
            $key = '/Files/' + $uri.Substring(11).Replace('.xaml', '.xbf')
            if (-not $resources.ContainsKey($key)) { $missing += $uri }
        }
        if ($missing.Count -gt 0) { throw ('Native XAML URIs absent from packaged PRI: ' + ($missing -join ', ')) }
        foreach ($theme in @('Win10_1507', 'Win10_1709', 'Win11')) {
            if (-not $uris.ContainsKey("ms-appx:///Views/$theme/SettingsPage_$theme.xaml")) {
                throw "Native executable lacks the expected Settings URI for $theme"
            }
        }
        Write-Output "PASS $($uris.Count) packaged native XAML URIs resolve in PRI, including all three Settings pages."
    }
    finally { $zip.Dispose() }
    exit 0
}

function Assert-XamlUri([string]$Uri) {
    if (-not $Uri.StartsWith('ms-appx:///', [StringComparison]::Ordinal)) {
        throw "Unexpected generated XAML URI: $Uri"
    }
    $relative = [Uri]::UnescapeDataString($Uri.Substring(11)).Replace('/', '\')
    $source = [IO.Path]::GetFullPath((Join-Path $ProjectRoot $relative))
    if (-not $source.StartsWith($ProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Generated XAML URI does not resolve to an application source: $Uri"
    }
}

if ($SelfTest) {
    foreach ($theme in @('Win10_1507', 'Win10_1709', 'Win11')) {
        $relative = "Views/$theme/SettingsPage_$theme.xaml"
        Assert-XamlUri "ms-appx:///$relative"
        $rejected = $false
        try { Assert-XamlUri "ms-appx:///LibreSpotUWP/$relative" } catch { $rejected = $true }
        if (-not $rejected) { throw "Regression: accepted duplicated project prefix for $theme" }
    }
    Write-Output 'PASS all three valid Settings URIs; duplicated project-prefix regression rejected.'
    exit 0
}

if (-not $GeneratedRoot) { throw 'GeneratedRoot is required.' }
if (-not [IO.Path]::IsPathRooted($GeneratedRoot)) { $GeneratedRoot = Join-Path $ProjectRoot $GeneratedRoot }
$files = @(Get-ChildItem -LiteralPath $GeneratedRoot -Recurse -Filter '*.g.i.cs')
$count = 0
foreach ($file in $files) {
    foreach ($match in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), 'ms-appx:///[^"\r\n]+\.xaml')) {
        Assert-XamlUri $match.Value
        $count++
    }
}
if ($count -eq 0) { throw 'No generated XAML application URIs found; validation cannot be skipped.' }
Write-Output "PASS $count generated XAML application URIs."
