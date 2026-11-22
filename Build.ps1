param([string]$Output = "LecooPanel.exe")
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (!(Test-Path -LiteralPath $compiler)) {
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (!(Test-Path -LiteralPath $compiler)) { throw 'Install or enable .NET Framework 4.8 before building.' }
    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu "/out:$Output" `
        /win32icon:LecooPanel.ico /win32manifest:LecooPanel.manifest `
        /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.ServiceProcess.dll `
        LecooPanel.cs Dashboard.cs Theme.cs LecooClient.cs
    if ($LASTEXITCODE -ne 0) { throw "C# compiler exited with code $LASTEXITCODE." }
} finally {
    Pop-Location
}