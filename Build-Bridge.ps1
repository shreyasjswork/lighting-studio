param([string]$OutputName='RgbAudioBridge.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework x64 C# compiler is required.'
}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Web.Extensions.dll ('/out:' + (Join-Path $PSScriptRoot $OutputName)) (Join-Path $PSScriptRoot 'HidDiagnostics.cs') (Join-Path $PSScriptRoot 'LightingPreferences.cs') (Join-Path $PSScriptRoot 'HpLightingPacket.cs') (Join-Path $PSScriptRoot 'LegionLightingPacket.cs') (Join-Path $PSScriptRoot 'AudioLightingResponse.cs') (Join-Path $PSScriptRoot 'AudioSpectrum.cs') (Join-Path $PSScriptRoot 'WasapiLoopback.cs') (Join-Path $PSScriptRoot 'EqualizerMotion.cs') (Join-Path $PSScriptRoot 'EqualizerStyle.cs') (Join-Path $PSScriptRoot 'RgbAudioBridge.cs')
if ($LASTEXITCODE -ne 0) { throw 'Bridge compilation failed' }
