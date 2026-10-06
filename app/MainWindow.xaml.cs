using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
namespace NoMoreBacknoise;
public partial class MainWindow : Window {
    private UserSettings _settings = SettingsStore.Load(); private HostClient? _host;
    private bool _ready, _running, _busy, _muted, _bypassed, _recording, _quit, _monitorRaw;
    private string _lastWarning=""; private DateTimeOffset _lastWarningTime=DateTimeOffset.MinValue;
    private Forms.NotifyIcon? _tray; private Hotkeys? _hotkeys;
    private readonly DispatcherTimer _configureTimer=new() { Interval=TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _updateTimer=new() { Interval=TimeSpan.FromHours(1) };
    private const string Repository="https://github.com/JustyPixels/NoMoreBacknoise";
    public MainWindow() { InitializeComponent(); Spectrum.Spectrum=true; _configureTimer.Tick+=async (_,_)=> { _configureTimer.Stop(); await Configure(); }; _updateTimer.Tick+=async (_,_)=>await CheckForUpdates(false); }
    private async void WindowLoaded(object sender,RoutedEventArgs e) {
        if(Environment.GetCommandLineArgs().Contains("--verify-ui")) return;
        try {
            var firstLaunch=!_settings.FirstLaunchComplete;
            if(firstLaunch) { var setup=new FirstLaunchWindow(_settings) { Owner=this }; if(setup.ShowDialog()!=true) { _quit=true; Close(); return; } ApplyStartup(_settings.WindowsStartup); SettingsStore.Save(_settings); }
            LanguageBox.ItemsSource=Localization.Languages; LanguageBox.SelectedValue=_settings.Language;
            FlowDirection=_settings.Language=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
            StartupCheck.IsChecked=_settings.WindowsStartup; AutoProcessCheck.IsChecked=_settings.AutoProcess; TrayCheck.IsChecked=_settings.CloseToTray; UpdatesCheck.IsChecked=_settings.UpdateChecks;
            MuteHotkeyBox.Text=_settings.MuteHotkey; BypassHotkeyBox.Text=_settings.BypassHotkey;
            LoadProcessing(_settings.Processing); RefreshProfiles();
            _hotkeys=new Hotkeys(new WindowInteropHelper(this).Handle); _hotkeys.Pressed+=id=> { if(id==1) ToggleMute(this,new()); else ToggleBypass(this,new()); };
            try { _hotkeys.Apply(_settings.MuteHotkey,_settings.BypassHotkey); } catch(Exception ex) { Warn(ex.Message); }
            CreateTray(); _ready=true;
            await ConnectHost(); await LoadDevices(); _updateTimer.Start();
            await CheckForUpdates(false);
            if(_settings.AutoProcess && InputDevices.SelectedItem!=null) await StartProcessing();
        } catch(Exception ex) { _ready=true; Warn(ex.Message); }
    }
    private void CreateTray() {
        _tray=new Forms.NotifyIcon { Icon=System.Drawing.SystemIcons.Information,Text="NoMoreBacknoise++",Visible=true };
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add(Localization.T("showApp"),null,(_,_)=>Dispatcher.Invoke(()=> { Show(); WindowState=WindowState.Normal; Activate(); }));
        menu.Items.Add(Localization.T("mute"),null,(_,_)=>Dispatcher.Invoke(()=>ToggleMute(this,new())));
        menu.Items.Add(Localization.T("bypass"),null,(_,_)=>Dispatcher.Invoke(()=>ToggleBypass(this,new())));
        menu.Items.Add(Localization.T("quit"),null,(_,_)=>Dispatcher.Invoke(()=> { _quit=true; Close(); }));
        _tray.ContextMenuStrip=menu; _tray.DoubleClick+=(_,_)=> { Show(); WindowState=WindowState.Normal; Activate(); };
    }
    private async Task ConnectHost() {
        if(_host!=null) await _host.DisposeAsync();
        _host=new HostClient(); _host.Event+=root=>Dispatcher.BeginInvoke(()=>OnHostEvent(root));
        _host.Disconnected+=message=>Dispatcher.BeginInvoke(()=> { if(!_quit) { _running=false; StartButton.SetResourceReference(Button.ContentProperty,"t.start"); StateLabel.Text=Localization.T("stopped"); Warn(message); } });
        await _host.Connect();
    }
    private static bool IsVirtual(Endpoint device) => device.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase) || device.Name.Contains("NoMoreBacknoise",StringComparison.OrdinalIgnoreCase);
    private async Task LoadDevices() {
        if(_host==null) return; var response=await _host.Request("devices");
        var devices=JsonSerializer.Deserialize<List<Endpoint>>(response.GetProperty("devices"),App.Json)!;
        var wasReady=_ready; _ready=false;
        var inputs=devices.Where(d=>d.Direction=="capture" && !IsVirtual(d)).ToList();
        InputDevices.ItemsSource=inputs; InputDevices.SelectedValue=_settings.InputId;
        if(_settings.InputId==null && inputs.Count>0) InputDevices.SelectedIndex=0;
        OutputDevices.ItemsSource=new[] { new Endpoint("",Localization.T("previewOnly"),"render") }.Concat(devices.Where(d=>d.Direction=="render" && IsVirtual(d))).ToList();
        OutputDevices.SelectedValue=_settings.OutputId ?? "";
        if(_settings.OutputId==null) { var cable=devices.FirstOrDefault(d=>d.Direction=="render" && IsVirtual(d)); if(cable!=null) OutputDevices.SelectedValue=cable.Id; }
        MonitorDevices.ItemsSource=devices.Where(d=>d.Direction=="render" && !IsVirtual(d)).ToList();
        _ready=wasReady;
        _settings.InputId=InputDevices.SelectedValue as string ?? _settings.InputId;
        _settings.OutputId=OutputDevices.SelectedValue as string ?? _settings.OutputId;
        SettingsStore.Save(_settings);
    }
    private async void RefreshDevices(object sender,RoutedEventArgs e) { try { await LoadDevices(); } catch(Exception ex) { Warn(ex.Message); } }
    private async void DeviceChanged(object sender,SelectionChangedEventArgs e) {
        if(!_ready) return; _settings.InputId=InputDevices.SelectedValue as string; _settings.OutputId=OutputDevices.SelectedValue as string; SaveSettings();
        if(_running && !_busy) { await StopProcessing(); Warn(Localization.T("routeChanged")); }
    }
    private async Task StartProcessing() {
        if(_busy) return; _busy=true; StartButton.IsEnabled=false;
        try {
            if(_host==null) await ConnectHost();
            var input=InputDevices.SelectedValue as string; if(string.IsNullOrEmpty(input)) throw new InvalidOperationException(Localization.T("selectMic"));
            var output=OutputDevices.SelectedValue as string;
            if(OutputDevices.SelectedItem==null) throw new InvalidOperationException("Selected cleaned output is unavailable. Reconnect it or choose Preview only explicitly.");
            var monitor=MonitorEnabled.IsChecked==true ? MonitorDevices.SelectedValue as string : null;
            if(MonitorEnabled.IsChecked==true && string.IsNullOrEmpty(monitor)) throw new InvalidOperationException(Localization.T("selectMonitor"));
            await _host!.Request("start",new { route=new { inputId=input,outputId=string.IsNullOrEmpty(output)?null:output,monitorId=monitor,monitorRaw=_monitorRaw },settings=_settings.Processing,muted=_muted,bypass=_bypassed });
            _running=true; _recording=false; RecordButton.SetResourceReference(Button.ContentProperty,"t.recordTest"); ExportRecordingButton.IsEnabled=false; RecordingStatus.Text="0.0 / 60 s";
            StartButton.SetResourceReference(Button.ContentProperty,"t.stop"); StateLabel.Text=Localization.T("starting"); WarningPanel.Visibility=Visibility.Collapsed;
            Waveform.Clear(); Spectrum.Clear();
        } catch(Exception ex) { Warn(ex.Message); }
        finally { _busy=false; StartButton.IsEnabled=true; }
    }
    private async Task StopProcessing() {
        try { if(_host!=null) await _host.Request("stop"); } catch(Exception ex) { Warn(ex.Message); }
        _running=false; _recording=false; RecordButton.SetResourceReference(Button.ContentProperty,"t.recordTest"); ExportRecordingButton.IsEnabled=false;
        StartButton.SetResourceReference(Button.ContentProperty,"t.start"); StateLabel.Text=Localization.T("stopped"); RawMeter.Value=CleanMeter.Value=-60;
    }
    private async void StartStop(object sender,RoutedEventArgs e) { if(_running) await StopProcessing(); else await StartProcessing(); }
    private async void RetryProcessing(object sender,RoutedEventArgs e) { if(_busy) return; await StopProcessing(); try { await ConnectHost(); await LoadDevices(); await StartProcessing(); } catch(Exception ex) { Warn(ex.Message); } }
    private async void ToggleMute(object sender,RoutedEventArgs e) { _muted=!_muted; MuteButton.SetResourceReference(Button.ContentProperty,_muted?"t.unmute":"t.mute"); MuteButton.Background=_muted?System.Windows.Media.Brushes.IndianRed:(System.Windows.Media.Brush)FindResource("PanelBrush"); await Configure(); }
    private async void ToggleBypass(object sender,RoutedEventArgs e) { _bypassed=!_bypassed; BypassButton.SetResourceReference(Button.ContentProperty,_bypassed?"t.restoreFiltering":"t.bypass"); if(_bypassed) StateLabel.Text=Localization.T("unfiltered"); await Configure(); }
    private async Task Configure() { if(!_running || _host==null) return; try { await _host.Request("configure",new { settings=_settings.Processing,muted=_muted,bypass=_bypassed,monitorRaw=_monitorRaw }); } catch(Exception ex) { Warn(ex.Message); } }
    private void ProcessingChanged(object sender,RoutedEventArgs e) {
        if(!_ready) return;
        var ready=_ready; _ready=false; PresetBox.SelectedIndex=3; _ready=ready;
        _settings.Processing.Strength=StrengthSlider.Value; _settings.Processing.AttenuationDb=AttenuationSlider.Value;
        _settings.Processing.SpeechThreshold=SpeechSlider.Value; _settings.Processing.AttackMs=AttackSlider.Value; _settings.Processing.HoldMs=HoldSlider.Value;
        _settings.Processing.ReleaseMs=ReleaseSlider.Value; _settings.Processing.FloorDb=FloorSlider.Value; _settings.Processing.GainDb=GainSlider.Value; _settings.Processing.GateEnabled=GateEnabled.IsChecked==true;
        StrengthValue.Text=$"{StrengthSlider.Value:0}%"; SaveSettings(); _configureTimer.Stop(); _configureTimer.Start();
    }
    private async void EngineChanged(object sender,SelectionChangedEventArgs e) {
        if(!_ready) return; _settings.Processing.Engine=((ComboBoxItem)EngineBox.SelectedItem).Tag.ToString()!; SaveSettings();
        if(_running && !_busy) { await StopProcessing(); await StartProcessing(); }
    }
    private void PresetChanged(object sender,SelectionChangedEventArgs e) {
        if(!_ready || PresetBox.SelectedIndex==3) return;
        var value=new ProcessingSettings { Engine=_settings.Processing.Engine };
        if(PresetBox.SelectedIndex==0) { value.Strength=45;value.AttenuationDb=20;value.SpeechThreshold=.2;value.HoldMs=250;value.ReleaseMs=200;value.FloorDb=-15; }
        if(PresetBox.SelectedIndex==2) { value.Strength=100;value.AttenuationDb=60;value.SpeechThreshold=.6;value.HoldMs=100;value.ReleaseMs=70;value.FloorDb=-70; }
        _settings.Processing=value; LoadProcessing(value); SaveSettings(); _configureTimer.Stop(); _configureTimer.Start();
    }
    private void LoadProcessing(ProcessingSettings value) {
        var ready=_ready; _ready=false;
        EngineBox.SelectedIndex=value.Engine=="rnnoise"?1:value.Engine=="deepfilter"?2:0;
        StrengthSlider.Value=value.Strength;AttenuationSlider.Value=value.AttenuationDb;SpeechSlider.Value=value.SpeechThreshold;AttackSlider.Value=value.AttackMs;HoldSlider.Value=value.HoldMs;ReleaseSlider.Value=value.ReleaseMs;FloorSlider.Value=value.FloorDb;GainSlider.Value=value.GainDb;GateEnabled.IsChecked=value.GateEnabled;StrengthValue.Text=$"{value.Strength:0}%";
        PresetBox.SelectedIndex=3;
        if(value.Strength==75 && value.AttenuationDb==35 && value.SpeechThreshold==.35 && value.HoldMs==180 && value.ReleaseMs==140 && value.FloorDb==-35 && value.GainDb==0 && value.AttackMs==2 && value.GateEnabled)PresetBox.SelectedIndex=1;
        else if(value.Strength==45 && value.AttenuationDb==20 && value.SpeechThreshold==.2 && value.HoldMs==250 && value.ReleaseMs==200 && value.FloorDb==-15 && value.GainDb==0 && value.AttackMs==2 && value.GateEnabled)PresetBox.SelectedIndex=0;
        else if(value.Strength==100 && value.AttenuationDb==60 && value.SpeechThreshold==.6 && value.HoldMs==100 && value.ReleaseMs==70 && value.FloorDb==-70 && value.GainDb==0 && value.AttackMs==2 && value.GateEnabled)PresetBox.SelectedIndex=2;
        _ready=ready;
    }
    private void AdvancedToggled(object sender,RoutedEventArgs e) { if(AdvancedControls!=null) AdvancedControls.IsEnabled=AdvancedEnabled.IsChecked==true; }
    private async void MonitorChanged(object sender,RoutedEventArgs e) { if(!_ready || !_running || _busy) return; await StopProcessing(); await StartProcessing(); }
    private async void ListenRaw(object sender,RoutedEventArgs e) { await StopTestPlayback(); _monitorRaw=true; await Configure(); }
    private async void ListenClean(object sender,RoutedEventArgs e) { await StopTestPlayback(); _monitorRaw=false; await Configure(); }
    private async Task StopTestPlayback() { try { if(_running && _host!=null) await _host.Request("recordPlaybackStop"); } catch(Exception ex) { Warn(ex.Message); } }
    private async void PlayTestRaw(object sender,RoutedEventArgs e) { await PlayTest(true); }
    private async void PlayTestClean(object sender,RoutedEventArgs e) { await PlayTest(false); }
    private async Task PlayTest(bool raw) { try { if(_host==null || !_running)throw new InvalidOperationException(Localization.T("startFirst")); await _host.Request("recordPlay",new {monitorRaw=raw}); }catch(Exception ex){Warn(ex.Message);} }
    private async void RecordTest(object sender,RoutedEventArgs e) {
        try { if(!_running) throw new InvalidOperationException(Localization.T("startFirst")); await _host!.Request(_recording?"recordStop":"recordStart"); _recording=!_recording; RecordButton.SetResourceReference(Button.ContentProperty,_recording?"t.stopRecording":"t.recordTest"); ExportRecordingButton.IsEnabled=!_recording; }
        catch(Exception ex) { Warn(ex.Message); }
    }
    private async void ExportRecording(object sender,RoutedEventArgs e) {
        try { var dialog=new SaveFileDialog { Title=Localization.T("exportWav"),Filter="WAV|*.wav",FileName="NoMoreBacknoise-test",OverwritePrompt=true }; if(dialog.ShowDialog()!=true) return; var result=await _host!.Request("recordExport",new { path=dialog.FileName }); DiagnosticText.Text=$"{result.GetProperty("rawPath").GetString()}\n{result.GetProperty("cleanPath").GetString()}"; }
        catch(Exception ex) { Warn(ex.Message); }
    }
    private void RefreshProfiles() { ProfileBox.ItemsSource=_settings.Profiles.Keys.OrderBy(x=>x).ToList(); }
    private void SaveProfile(object sender,RoutedEventArgs e) { var name=ProfileName.Text.Trim(); if(name.Length==0) { Warn(Localization.T("profileNameHint")); return; } _settings.Profiles[name]=_settings.Processing.Copy(); SaveSettings(); RefreshProfiles(); ProfileBox.SelectedItem=name; }
    private async void ProfileSelected(object sender,SelectionChangedEventArgs e) { if(!_ready || ProfileBox.SelectedItem is not string name) return; var changed=_settings.Processing.Engine!=_settings.Profiles[name].Engine; _settings.Processing=_settings.Profiles[name].Copy(); LoadProcessing(_settings.Processing); ProfileName.Text=name;SaveSettings(); if(_running && changed) { await StopProcessing();await StartProcessing(); } else await Configure(); }
    private void ImportProfile(object sender,RoutedEventArgs e) { try { var dialog=new OpenFileDialog { Filter="JSON|*.json" }; if(dialog.ShowDialog()!=true)return; if(new FileInfo(dialog.FileName).Length>65536)throw new InvalidDataException("Profile is too large."); var root=JsonSerializer.Deserialize<ProfileExport>(File.ReadAllText(dialog.FileName),App.Json) ?? throw new InvalidDataException("Empty profile.");if(root.Version!=1 || string.IsNullOrWhiteSpace(root.Name) || root.Name.Length>80)throw new InvalidDataException("Invalid profile.");root.Processing.Validate();_settings.Profiles[root.Name]=root.Processing;SaveSettings();RefreshProfiles();ProfileBox.SelectedItem=root.Name; }catch(Exception ex){Warn(ex.Message);} }
    private void ExportProfile(object sender,RoutedEventArgs e) { try { var dialog=new SaveFileDialog { Filter="JSON|*.json",FileName="NoMoreBacknoise-profile.json" };if(dialog.ShowDialog()!=true)return;var profile=new ProfileExport { Name=string.IsNullOrWhiteSpace(ProfileName.Text)?"Custom":ProfileName.Text.Trim(),Processing=_settings.Processing.Copy() };File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(profile,App.Json)); }catch(Exception ex){Warn(ex.Message);} }
    private sealed class ProfileExport { public int Version { get;set; }=1;public string Name { get;set; }="Custom";public ProcessingSettings Processing { get;set; }=new(); }
    private void LanguageChanged(object sender,SelectionChangedEventArgs e) { if(!_ready || LanguageBox.SelectedValue is not string locale)return;Localization.Load(locale);_settings.Language=locale;FlowDirection=locale=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;SaveSettings();if(_tray!=null){_tray.Dispose();CreateTray();} }
    private async void ApplySettings(object sender,RoutedEventArgs e) {
        try { _hotkeys?.Apply(MuteHotkeyBox.Text,BypassHotkeyBox.Text);ApplyStartup(StartupCheck.IsChecked==true);_settings.WindowsStartup=StartupCheck.IsChecked==true;_settings.AutoProcess=AutoProcessCheck.IsChecked==true;_settings.CloseToTray=TrayCheck.IsChecked==true;_settings.UpdateChecks=UpdatesCheck.IsChecked==true;_settings.MuteHotkey=MuteHotkeyBox.Text;_settings.BypassHotkey=BypassHotkeyBox.Text;SaveSettings();DiagnosticText.Text=Localization.T("settingsSaved");await CheckForUpdates(false); }catch(Exception ex){Warn(ex.Message);} }
    private static void ApplyStartup(bool enabled) {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled)key.SetValue("NoMoreBacknoise",$"\"{Environment.ProcessPath}\"");else key.DeleteValue("NoMoreBacknoise",false);
    }
    private void SaveSettings() { try { SettingsStore.Save(_settings); } catch(Exception ex) { Warn(ex.Message); } }
    private async void CheckUpdates(object sender,RoutedEventArgs e) => await CheckForUpdates(true);
    private async Task CheckForUpdates(bool manual) {
        if(!manual && (!_settings.UpdateChecks || (_settings.LastUpdateCheck!=null && DateTimeOffset.UtcNow-_settings.LastUpdateCheck<TimeSpan.FromDays(1))))return;
        try {
            _settings.LastUpdateCheck=DateTimeOffset.UtcNow;SaveSettings();using var client=new HttpClient { Timeout=TimeSpan.FromSeconds(10) };client.DefaultRequestHeaders.UserAgent.ParseAdd("NoMoreBacknoise/0.1.0");
            using var response=await client.GetAsync("https://api.github.com/repos/JustyPixels/NoMoreBacknoise/releases/latest");
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound){if(manual)DiagnosticText.Text=Localization.T("noRelease");return;}
            response.EnsureSuccessStatusCode();using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var tag=document.RootElement.GetProperty("tag_name").GetString()??"";
            if(Version.TryParse(tag.TrimStart('v'),out var version) && version>new Version(0,1,0)) { DiagnosticText.Text=Localization.T("updateAvailable")+" "+tag;_tray?.ShowBalloonTip(8000,"NoMoreBacknoise++",Localization.T("updateAvailable")+" "+tag,Forms.ToolTipIcon.Info); }
            else if(manual)DiagnosticText.Text=Localization.T("upToDate");
        }catch(Exception ex){if(manual)Warn(ex.Message);}
    }
    private void OnHostEvent(JsonElement root) {
        if(_quit)return; var type=root.GetProperty("type").GetString();
        if(type=="status") { EngineStatus.Text=root.GetProperty("engine").GetString()+" / CPU"; DiagnosticText.Text=root.GetProperty("message").GetString();if(root.TryGetProperty("degraded",out var degraded)&&degraded.GetBoolean())Warn(DiagnosticText.Text??"");return; }
        if(type=="error" || type=="warning") { Warn(root.GetProperty("message").GetString()??"");if(type=="error"){_running=false;StartButton.SetResourceReference(Button.ContentProperty,"t.start");StateLabel.Text=Localization.T("stopped");}return; }
        if(type=="recordingStopped") { _recording=false;RecordButton.SetResourceReference(Button.ContentProperty,"t.recordTest");ExportRecordingButton.IsEnabled=true;return; }
        if(type!="telemetry")return;
        float[] Samples(string key)=>root.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        Waveform.SetSamples(Samples("raw"),Samples("clean"));Spectrum.SetSamples(Samples("rawSpectrum"),Samples("cleanSpectrum"));
        var raw=root.GetProperty("rawRms").GetDouble();var clean=root.GetProperty("cleanRms").GetDouble();RawMeter.Value=raw;CleanMeter.Value=clean;
        RawLevelLabel.Text=$"{Localization.T("raw")} RMS {raw:0.0} / Peak {root.GetProperty("rawPeak").GetDouble():0.0} dBFS";CleanLevelLabel.Text=$"{Localization.T("cleaned")} RMS {clean:0.0} / Peak {root.GetProperty("cleanPeak").GetDouble():0.0} dBFS";
        var engine=root.GetProperty("engine").GetString();EngineStatus.Text=engine+" / CPU";AttenuationSlider.IsEnabled=engine=="deepfilter";
        var process=root.GetProperty("processingMs").GetDouble();var delay=root.GetProperty("modelDelayMs").GetDouble()+root.GetProperty("captureBufferMs").GetDouble()+root.GetProperty("renderBufferMs").GetDouble()+root.GetProperty("queuedMs").GetDouble()+process;
        DelayStatus.Text=$"~{delay:0} ms";LoadStatus.Text=$"{process:0.0} ms";SpeechStatus.Text=$"{root.GetProperty("speech").GetDouble():P0}";
        StateLabel.Text=_muted?Localization.T("muted"):_bypassed||engine=="raw"?Localization.T("unfiltered"):string.IsNullOrEmpty(OutputDevices.SelectedValue as string)?Localization.T("previewOnly"):Localization.T("running");
        RecordingStatus.Text=$"{root.GetProperty("recordingSeconds").GetDouble():0.0} / 60 s";
        if(root.GetProperty("clipping").GetBoolean())DiagnosticText.Text=Localization.T("clippingHint");
    }
    private void Warn(string message) {
        WarningText.Text=message;WarningPanel.Visibility=Visibility.Visible;
        if(message==_lastWarning && DateTimeOffset.UtcNow-_lastWarningTime<TimeSpan.FromSeconds(10))return;
        _lastWarning=message;_lastWarningTime=DateTimeOffset.UtcNow;
        _tray?.ShowBalloonTip(8000,"NoMoreBacknoise++",message,Forms.ToolTipIcon.Warning);
        if(!_busy && !IsVisible){Show();WindowState=WindowState.Normal;}
    }
    private static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private void OpenRepository(object sender,RoutedEventArgs e)=>OpenUrl(Repository);
    private void OpenDownloads(object sender,RoutedEventArgs e)=>OpenUrl(Repository+"/releases");
    private void OpenCable(object sender,RoutedEventArgs e)=>OpenUrl("https://vb-audio.com/Cable/");
    private void OpenGuide(object sender,RoutedEventArgs e)=>OpenUrl(Repository+"/blob/main/docs/SETUP.md");
    private async void WindowClosing(object? sender,CancelEventArgs e) {
        if(Environment.GetCommandLineArgs().Contains("--verify-ui")) return;
        if(!_quit && _settings.CloseToTray && _tray!=null){e.Cancel=true;Hide();return;}
        if(_host==null){_quit=true;_tray?.Dispose();_hotkeys?.Dispose();return;}
        e.Cancel=true;_quit=true;_configureTimer.Stop();_updateTimer.Stop();_hotkeys?.Dispose();_tray?.Dispose();
        if(_host!=null){await _host.DisposeAsync();_host=null;}Close();
    }
}
