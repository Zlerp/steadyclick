# Builds SteadyClick.exe with the C# compiler that ships with Windows. No SDK or install needed.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

Push-Location $PSScriptRoot
try {
    if (-not (Test-Path 'app.ico')) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }
    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 `
        /out:SteadyClick.exe /win32manifest:app.manifest /win32icon:app.ico `
        /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        (Get-ChildItem 'src\*.cs' | ForEach-Object { $_.FullName })
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
    "Built $(Join-Path $PSScriptRoot 'SteadyClick.exe')"
}
finally {
    Pop-Location
}
