[CmdletBinding()]
param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRoot 'src/AnimaLive/AnimaLive.csproj'
$taskOutput = Join-Path $taskRoot 'artifacts/windows-x64'

& dotnet publish $taskProject -c Release -r win-x64 --self-contained true `
    -o $taskOutput -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de Anima Live.' }

if ($Test) {
    $taskReport = Join-Path $taskOutput 'resultado-pruebas.txt'
    $taskProcess = Start-Process -FilePath (Join-Path $taskOutput 'AnimaLivePad.exe') `
        -ArgumentList @('--self-test', ('"{0}"' -f $taskReport), '--offline') `
        -WindowStyle Hidden -Wait -PassThru
    if (Test-Path -LiteralPath $taskReport) {
        Get-Content -LiteralPath $taskReport -Encoding UTF8
    }
    if ($taskProcess.ExitCode -ne 0) { throw 'Fallaron las pruebas de Anima Live.' }
}

Write-Host "Aplicación lista: $taskOutput"
