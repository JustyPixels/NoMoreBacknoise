param([switch]$Live)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$hostExe = Join-Path $root 'target/release/nmb-host.exe'
$report = & $hostExe --self-test | ConvertFrom-Json
if ($LASTEXITCODE) { throw 'Engine smoke test failed' }
foreach ($engine in $report) { if ($engine.requested -ne $engine.selected) { Write-Warning $engine.reason }; if ($engine.p95ProcessingMs -gt 10) { throw 'Processing exceeds frame budget' } }
$locales = Get-Content app/Localization/strings.json -Raw | ConvertFrom-Json -AsHashtable
if ($locales.Count -ne 9) { throw 'Nine locales required' }
foreach ($locale in $locales.Keys) { foreach ($key in $locales.en.Keys) { if (!$locales[$locale][$key]) { throw "Missing $locale/$key" } } }
$name = 'nmb-' + [guid]::NewGuid().ToString('N')
$pipe = [IO.Pipes.NamedPipeServerStream]::new($name,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,([IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly))
$process = Start-Process -FilePath $hostExe -ArgumentList '--pipe',$name -WindowStyle Hidden -PassThru
try {
    $connect = $pipe.WaitForConnectionAsync(); if (!$connect.Wait(15000)) { throw 'Pipe connection timed out' }
    $reader = [IO.StreamReader]::new($pipe); $writer = [IO.StreamWriter]::new($pipe); $writer.AutoFlush=$true
    function Read-Event { $read=$reader.ReadLineAsync(); if(!$read.Wait(30000)){throw 'Protocol timeout'}; if(!$read.Result){throw 'Host disconnected'}; $read.Result | ConvertFrom-Json }
    function Send-Request($Op,$Values=@{},$Version=1) {
        $id=[guid]::NewGuid().ToString('N'); $request=@{version=$Version;id=$id;op=$Op};foreach($key in $Values.Keys){$request[$key]=$Values[$key]}
        $writer.WriteLine(($request | ConvertTo-Json -Compress -Depth 8))
        do {$message=Read-Event}while($message.requestId -ne $id)
        return $message
    }
    $hello=Read-Event; if($hello.version -ne 1 -or $hello.type -ne 'hello'){throw 'Handshake failed'}
    $invalid=Send-Request 'devices' @{} 999; if($invalid.type -ne 'error'){throw 'Invalid version accepted'}
    $devices=Send-Request 'devices'; if($devices.type -ne 'devices'){throw 'Device enumeration failed'}
    $bad=Send-Request 'start' @{route=@{inputId='missing';outputId=$null;monitorId=$null}}; if($bad.type -ne 'error'){throw 'Unavailable device accepted'}
    if($Live) {
        $inputDevice=$devices.devices | Where-Object {$_.direction -eq 'capture' -and $_.name -notmatch 'CABLE|NoMoreBacknoise'} | Select-Object -First 1
        if(!$inputDevice){throw 'No physical microphone for live check'}
        $start=Send-Request 'start' @{route=@{inputId=$inputDevice.id;outputId=$null;monitorId=$null};settings=@{engine='rnnoise'};muted=$true;bypass=$true}
        if($start.type -ne 'ack'){throw $start.message}
        do {$data=Read-Event}while($data.type -ne 'telemetry')
        if(($data.clean | Where-Object {$_ -ne 0}).Count){throw 'Mute precedence failed'}
        $null=Send-Request 'configure' @{muted=$false;bypass=$false}
        $null=Send-Request 'recordStart'; Start-Sleep -Milliseconds 250; $null=Send-Request 'recordStop'
        $null=Send-Request 'stop'
    }
    $null=Send-Request 'shutdown'; if(!$process.WaitForExit(5000)){throw 'Host shutdown failed'}
} finally { $pipe.Dispose(); if(!$process.HasExited){$process.Kill()} }
$validation=@{date=[DateTimeOffset]::UtcNow.ToString('o');engines=$report;locales=$locales.Keys;protocol='passed';liveMicrophone=$Live.IsPresent;note='Synthetic smoke test. Human speech quality and full hardware matrix require manual validation.'}
$validation | ConvertTo-Json -Depth 8 | Set-Content artifacts/validation.json -Encoding UTF8
Write-Host 'Engine, translation and protected pipe checks passed.'
