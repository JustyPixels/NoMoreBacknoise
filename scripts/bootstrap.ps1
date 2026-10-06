$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
New-Item -ItemType Directory -Force .deps,.cache,artifacts | Out-Null
$dependencies = @(
    @{ Name = 'rnnoise'; Url = 'https://github.com/xiph/rnnoise.git'; Tag = 'v0.1'; Commit = 'cdf196b1e9de2f8ff1003328ebf9a4316477429d' },
    @{ Name = 'DeepFilterNet'; Url = 'https://github.com/Rikorose/DeepFilterNet.git'; Tag = 'v0.5.6'; Commit = '978576aa8400552a4ce9730838c635aa30db5e61' }
)
foreach ($dependency in $dependencies) {
    $path = Join-Path $root ('.deps/' + $dependency.Name)
    if (!(Test-Path -LiteralPath $path)) {
        & git clone --depth 1 --branch $dependency.Tag $dependency.Url $path
        if ($LASTEXITCODE -ne 0) { throw 'Dependency download failed.' }
    }
    $actual = & git -C $path rev-parse HEAD
    if ($actual -ne $dependency.Commit) { throw "Unexpected revision in $path. Expected $($dependency.Commit)." }
}
$model = Join-Path $root '.deps/DeepFilterNet/models/DeepFilterNet3_onnx.tar.gz'
if ((Get-FileHash -LiteralPath $model).Hash -ne 'C94D91F70911001C946E0FABB4AA9ADC37045F45A03B56008CB0C8244CB63616') { throw 'DeepFilterNet3 model checksum mismatch.' }
Write-Host 'Pinned audio dependencies and verified model are ready.'
