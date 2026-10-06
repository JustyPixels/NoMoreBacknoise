param([switch]$Live)
$ErrorActionPreference='Stop'
if(!$Live) {throw 'Use -Live explicitly on an authorized microphone; this sends cleaned audio to the installed cable, without saving recordings.'}
$root=Split-Path -Parent $PSScriptRoot
$hostExe=Join-Path $root 'target/release/nmb-host.exe'
$connections=[Collections.Generic.List[object]]::new()
function Connect-TestHost {
    $name='nmb-'+[guid]::NewGuid().ToString('N')
    $pipe=[IO.Pipes.NamedPipeServerStream]::new($name,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,([IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly))
    $process=Start-Process -FilePath $hostExe -ArgumentList '--pipe',$name -WindowStyle Hidden -PassThru
    $connection=[pscustomobject]@{Pipe=$pipe;Process=$process;Reader=$null;Writer=$null}
    $connections.Add($connection)
    if(!$pipe.WaitForConnectionAsync().Wait(15000)){throw 'Host connection timeout'}
    $connection.Reader=[IO.StreamReader]::new($pipe);$connection.Writer=[IO.StreamWriter]::new($pipe);$connection.Writer.AutoFlush=$true
    $hello=Read-TestEvent $connection;if($hello.type -ne 'hello'){throw 'Handshake failed'}
    return $connection
}
function Read-TestEvent($Connection) {
    $read=$Connection.Reader.ReadLineAsync();if(!$read.Wait(15000)){throw 'Audio event timeout'}
    if(!$read.Result){throw 'Host disconnected'}
    $read.Result | ConvertFrom-Json
}
function Request-TestHost($Connection,$Op,$Values=@{}) {
    $id=[guid]::NewGuid().ToString('N');$request=@{version=1;id=$id;op=$Op};foreach($key in $Values.Keys){$request[$key]=$Values[$key]}
    $Connection.Writer.WriteLine(($request | ConvertTo-Json -Depth 8 -Compress))
    do {$message=Read-TestEvent $Connection}while($message.requestId -ne $id)
    if($message.type -eq 'error'){throw $message.message};return $message
}
function Telemetry-TestHost($Connection,$Count=12) {
    $events=[Collections.Generic.List[object]]::new()
    while($events.Count -lt $Count) {$message=Read-TestEvent $Connection;if($message.type -eq 'error' -or $message.type -eq 'warning'){throw $message.message};if($message.type -eq 'telemetry'){$events.Add($message)}}
    return $events.ToArray()
}
try {
    $producer=Connect-TestHost;$consumer=Connect-TestHost;$consumerTwo=Connect-TestHost
    $devices=(Request-TestHost $producer 'devices').devices
    # Test harness only: require a unique physical source and canonical standard endpoints.
    # The actual app uses PnP ancestry/provider identity, never these display-name assertions.
    $physical=@($devices | Where-Object {$_.direction -eq 'capture' -and $_.name -notmatch 'CABLE|VB-Audio|NoMoreBacknoise'})
    $render=@($devices | Where-Object {$_.direction -eq 'render' -and $_.name -match '^CABLE Input \(VB-Audio Virtual Cable\)$'})
    $capture=@($devices | Where-Object {$_.direction -eq 'capture' -and $_.name -match '^CABLE Output \(VB-Audio Virtual Cable\)$'})
    if($physical.Count -ne 1 -or $render.Count -ne 1 -or $capture.Count -ne 1){throw 'Choose test identities explicitly: this harness requires one physical microphone and canonical standard endpoints'}
    foreach($client in @($consumer,$consumerTwo)) {
        $null=Request-TestHost $client 'start' @{route=@{inputId=$capture[0].id};settings=@{engine='rnnoise';gateEnabled=$false;strength=0};muted=$false;bypass=$true}
    }
    $results=@()
    foreach($engine in @('rnnoise','deepfilter')) {
        $null=Request-TestHost $producer 'start' @{route=@{inputId=$physical[0].id;outputId=$render[0].id};settings=@{engine=$engine};muted=$true;bypass=$true}
        $first=Telemetry-TestHost $producer 12
        if(@($first | Where-Object { @($_.clean | Where-Object {$_ -ne 0}).Count -gt 0 }).Count){throw 'Mute did not override bypass'}
        foreach($client in @($consumer,$consumerTwo)) { $drain=Telemetry-TestHost $client 36; if(@($drain | Select-Object -Last 8 | Where-Object { @($_.raw | Where-Object {[Math]::Abs($_) -gt 0.00001}).Count -gt 0 }).Count){throw 'Cable is not silent while producer muted'} }
        $null=Request-TestHost $producer 'configure' @{muted=$false;bypass=$true}
        $bypass=Telemetry-TestHost $producer 36
        foreach($event in $bypass | Select-Object -Last 12) { if($event.raw.Count -ne $event.clean.Count){throw 'Alignment length'};for($i=0;$i -lt $event.raw.Count;$i++){if([Math]::Abs($event.raw[$i]-$event.clean[$i]) -gt 0.000001){throw 'Bypass changed audio'}} }
        $null=Request-TestHost $producer 'configure' @{muted=$false;bypass=$false}
        $filtered=Telemetry-TestHost $producer 90
        $receiver=Telemetry-TestHost $consumer 24;$receiverTwo=Telemetry-TestHost $consumerTwo 24
        $null=Request-TestHost $producer 'recordStart';$null=Telemetry-TestHost $producer 12;$null=Request-TestHost $producer 'recordStop'
        $maxLoad=($filtered.processingMs | Measure-Object -Maximum).Maximum
        $delay=@($filtered | ForEach-Object {$_.modelDelayMs+$_.captureBufferMs+$_.renderBufferMs+$_.queuedMs+$_.processingMs})
        $results+=@{engineRequested=$engine;engineObserved=($filtered.engine | Select-Object -Unique);telemetryFrames=$filtered.Count;maxProcessingMs=$maxLoad;maxEstimatedApplicationDelayMs=($delay | Measure-Object -Maximum).Maximum;physicalSignalDetected=(@($filtered | Where-Object {$_.rawPeak -gt -80}).Count -gt 0);cableSignalDetected=(@($receiver | Where-Object {$_.rawPeak -gt -80}).Count -gt 0);simultaneousSecondConsumerSignalDetected=(@($receiverTwo | Where-Object {$_.rawPeak -gt -80}).Count -gt 0);muteOverBypass='passed';bypassEquality='passed';recording='RAM only, not exported'}
        $null=Request-TestHost $producer 'stop'
    }
    @{date=[DateTimeOffset]::UtcNow.ToString('o');results=$results;consumers=2;note='Live WASAPI shared-mode smoke check. No saved audio. Signal presence is not speech-quality validation; estimates exclude cable and hardware delay.';pending=@('Human speech, quiet speech, keyboard and fan comparisons','Unplug/reconnect and sleep/resume','TeamSpeak and Discord internal microphone tests')} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'artifacts/routing-validation.json') -Encoding utf8
    Write-Host 'Installed cable routing, mute, bypass, RAM recording and two simultaneous consumers checked.'
}finally {
    foreach($client in $connections) {try{if($client.Writer){$null=Request-TestHost $client 'shutdown'}}catch{};$client.Pipe.Dispose();if(!$client.Process.HasExited){$client.Process.Kill()} }
}
