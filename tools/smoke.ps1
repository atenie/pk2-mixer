# Smoke test: start the GUI, keep it alive for a few seconds, then stop it.
param(
    [string]$Exe = "$PSScriptRoot\..\src\Pk2Mixer.App\bin\Debug\net8.0\Pk2Mixer.exe",
    [int]$Seconds = 5
)

$log = Join-Path $PSScriptRoot 'smoke.txt'
$trace = Join-Path $PSScriptRoot 'smoke-trace.txt'
$env:PK2MIXER_TRACE = $trace
try {
    $p = Start-Process -FilePath $Exe -PassThru -RedirectStandardError (Join-Path $PSScriptRoot 'smoke-err.txt')
    $exited = $p.WaitForExit($Seconds * 1000)
    if ($exited) {
        "exited early with code $($p.ExitCode)" | Set-Content -Path $log
    } else {
        $p.Refresh()
        $title = $p.MainWindowTitle
        $handle = $p.MainWindowHandle
        "running; window title: '$title'; handle: $handle" | Set-Content -Path $log
        Stop-Process -Id $p.Id -Force
    }
} catch {
    "failed: $($_.Exception.Message)" | Set-Content -Path $log
}
