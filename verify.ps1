$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build src/AbaTaggedPointer -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $app = Join-Path $PSScriptRoot 'src/AbaTaggedPointer/bin/Release/net10.0/AbaTaggedPointer.dll'
    foreach ($command in @('verify', 'demo', 'explore')) {
        dotnet $app $command
        if ($LASTEXITCODE -ne 0) { throw "$command failed." }
    }
    $traces = Get-Content artifacts/trace.json -Raw | ConvertFrom-Json
    if ($traces.Count -ne 2 -or $traces[0].InvalidStates -ne 1 -or $traces[1].InvalidStates -ne 0) { throw 'Unexpected trace report.' }
    $summary = Get-Content artifacts/exploration-summary.json -Raw | ConvertFrom-Json
    if ($summary.Count -ne 2 -or @($summary | Where-Object { -not $_.AllExecutionsTerminated }).Count -ne 0) { throw 'Incomplete exploration.' }
    foreach ($file in @('aba-pointer-only.svg', 'aba-tagged.svg', 'aba-comparison.svg')) {
        [xml]$svg = Get-Content (Join-Path artifacts $file) -Raw
        if ($svg.DocumentElement.NamespaceURI -ne 'http://www.w3.org/2000/svg') { throw 'Invalid SVG namespace.' }
    }
    foreach ($invalidArgs in @(@('demo', '--strategy', 'wrong'), @('explore', '--max-steps', '0'), @('demo', '--output'), @('unknown'), @('demo', '--strategy', 'pointer', '--strategy', 'tagged'))) {
        dotnet $app @invalidArgs
        if ($LASTEXITCODE -ne 2) { throw 'Invalid CLI arguments must return exit code 2.' }
    }
    Write-Host 'PASS: build, model verification, reports, and CLI error handling.'
}
finally { Pop-Location }
exit 0
