using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace NoMoreBacknoise;
internal enum CableCondition { Missing, Ready, Disabled, Partial }
internal static class CablePolicy {
    public static CableCondition Classify(IReadOnlyList<CableDriver> drivers,IReadOnlyList<Endpoint> endpoints) {
        if(drivers.Count==0)return CableCondition.Missing;
        var cable=endpoints.Where(e=>e.IsStandardCable).ToList();
        if(drivers.Any(d=>d.Started) && cable.Any(e=>e.Direction=="render" && e.State=="Active") && cable.Any(e=>e.Direction=="capture" && e.State=="Active"))return CableCondition.Ready;
        if(cable.Any(e=>e.State=="Disabled"))return CableCondition.Disabled;
        return CableCondition.Partial;
    }
    public static long BootIdentity {
        get {
            using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
            if(key?.GetValue("BootId") is int id)return id;
            // Fail closed if Windows boot identity cannot be read: verification stays pending.
            return -1;
        }
    }
    public static bool AwaitingReboot(long? before,long now) => before.HasValue && (before.Value==now || before.Value<0 || now<0);
    public static string[] DefaultChanges(Dictionary<string,string?> before,Dictionary<string,string?> after) => before.Keys.Union(after.Keys).Where(key=>before.GetValueOrDefault(key)!=after.GetValueOrDefault(key)).ToArray();
    public static string[] Packages(string html) => Regex.Matches(html,@"https://download\.vb-audio\.com/Download_CABLE/VBCABLE_Driver_Pack[0-9]+\.zip",RegexOptions.IgnoreCase).Select(m=>m.Value).Distinct().ToArray();
    public static async Task<bool> HasNews(CableManifest approved) {
        using var client=new HttpClient {Timeout=TimeSpan.FromSeconds(15)};client.DefaultRequestHeaders.UserAgent.ParseAdd("NoMoreBacknoise/0.2.0-preview.1");
        var page=await client.GetStringAsync(approved.Site);
        var known=Regex.Match(approved.Package,@"Pack([0-9]+)");
        return Packages(page).Any(url=>int.TryParse(Regex.Match(url,@"Pack([0-9]+)",RegexOptions.IgnoreCase).Groups[1].Value,out var pack) && pack>int.Parse(known.Groups[1].Value));
    }
}
public sealed class CableSetupWindow : Window {
    private readonly HostClient? _host;private readonly UserSettings _settings;private readonly CableManifest _manifest;private readonly Func<Task>? _beforeInstall;
    private readonly TextBlock _status=new(),_details=new(),_news=new(),_defaults=new();
    private readonly Button _install=new(),_download=new(),_refresh=new();private readonly CheckBox _consent=new();
    private Dictionary<string,string?> _currentDefaults=new();private CableCondition? _condition;private bool _busy;
    public CableSetupWindow(HostClient? host,UserSettings settings,bool verification=false,Func<Task>? beforeInstall=null) {
        _host=host;_settings=settings;_beforeInstall=beforeInstall;_manifest=CableManifest.Load();_manifest.Validate();
        Style=(Style)Application.Current.FindResource(typeof(Window));Title="NoMoreBacknoise++ · VB-CABLE";Width=760;Height=760;MinWidth=540;MinHeight=440;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=Localization.Current=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        var panel=new StackPanel {Margin=new Thickness(26)};Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void Text(string key,double size=13) {var text=new TextBlock {Text=Localization.T(key),FontSize=size,Margin=new Thickness(0,0,0,12)};panel.Children.Add(text);}
        Text("cableTitle",24);Text("cableRoute");
        _status.FontSize=18;_status.FontWeight=FontWeights.SemiBold;_status.Margin=new Thickness(0,0,0,10);panel.Children.Add(_status);panel.Children.Add(_details);
        _defaults.Margin=new Thickness(0,12,0,12);panel.Children.Add(_defaults);
        var actions=new WrapPanel {Margin=new Thickness(0,14,0,10)};panel.Children.Add(actions);
        void Button(WrapPanel parent,Button button,string key,Action action) {button.Content=Localization.T(key);button.Click+=(_,_)=>action();parent.Children.Add(button);}
        Button(actions,_refresh,"cableRefresh",async()=>await Refresh());Button(actions,new(),"cableSound",()=>Open("ms-settings:sound"));
        Button(actions,new(),"cableDefaultsAck",()=>{_settings.CableDefaultsBefore=null;SettingsStore.Save(_settings);_defaults.Text="";});
        Text("cableLicense");
        var links=new WrapPanel();panel.Children.Add(links);
        Button(links,new(),"cableSite",()=>Open(_manifest.Site));Button(links,new(),"cableTerms",()=>Open(_manifest.License));Button(links,new(),"cableDonate",()=>Open(_manifest.Donate));
        Button(links,new(),"cableReadme",()=>Open(System.IO.Path.Combine(AppContext.BaseDirectory,"dependencies","VB-CABLE-readme.txt")));
        Text("cableChanges");
        _consent.Content=new TextBlock {Text=Localization.T("cableConsent")};_consent.Margin=new Thickness(0,8,0,12);_consent.Checked+=(_,_)=>EnableInstall();_consent.Unchecked+=(_,_)=>EnableInstall();panel.Children.Add(_consent);
        var installActions=new WrapPanel();panel.Children.Add(installActions);
        Button(installActions,_install,"cableOffline",async()=>await Install(false));Button(installActions,_download,"cableDownload",async()=>await Install(true));
        Text("cableInternet");
        var newsActions=new WrapPanel();panel.Children.Add(newsActions);
        Button(newsActions,new(),"cableCheckNews",async()=> {if(_busy)return;_busy=true;EnableInstall();try {_news.Text=Localization.T(await CablePolicy.HasNews(_manifest)?"cableUnapproved":"cableCurrent");}catch{_news.Text=Localization.T("cableNetworkFailed");}finally{_busy=false;EnableInstall();}});
        panel.Children.Add(_news);
        var close=new Button {Content=Localization.T("cableLater"),Margin=new Thickness(0,16,0,0)};close.Click+=(_,_)=>Close();panel.Children.Add(close);
        Closing+=(_,e)=> {if(_busy)e.Cancel=true;};
        if(verification) { _condition=CableCondition.Missing;_status.Text=Localization.T("cableMissing");_details.Text=$"{_manifest.Product} · {_manifest.Vendor}\nPack45 · {_manifest.DriverVersion}";EnableInstall(); }
        else Loaded+=async(_,_)=>await Refresh();
    }
    private void EnableInstall() {var enabled=!_busy && _condition==CableCondition.Missing && _consent.IsChecked==true && !CablePolicy.AwaitingReboot(_settings.CableInstallBoot,CablePolicy.BootIdentity);_install.IsEnabled=_download.IsEnabled=enabled;_refresh.IsEnabled=!_busy;_consent.IsEnabled=!_busy;}
    private async Task Refresh() {
        try {
            if(_host==null)throw new InvalidOperationException("Audio host unavailable.");
            var response=await _host.Request("devices",new {includeInactive=true});
            var endpoints=JsonSerializer.Deserialize<List<Endpoint>>(response.GetProperty("devices"),App.Json)!;
            var drivers=await Task.Run(CableDevices.Drivers);CableDevices.Identify(endpoints,drivers);
            _currentDefaults=JsonSerializer.Deserialize<Dictionary<string,string?>>(response.GetProperty("defaults"),App.Json)!;
            _condition=CablePolicy.Classify(drivers,endpoints);
            _status.Text=Localization.T("cable"+_condition);
            _details.Text=$"{_manifest.Product} · {_manifest.Vendor}\n{Localization.T("cableApproved")}: Pack45 · {_manifest.DriverVersion}\n"+
                string.Join("\n",drivers.Select(d=>$"{d.Provider} · {d.Version}"))+"\n"+string.Join("\n",endpoints.Where(e=>e.IsStandardCable).Select(e=>$"{e.Direction}: {e.Name} ({e.State})"));
            if(CablePolicy.AwaitingReboot(_settings.CableInstallBoot,CablePolicy.BootIdentity))_status.Text=Localization.T("cableReboot");
            else if(_settings.CableInstallBoot.HasValue && _condition==CableCondition.Ready) {_settings.CableInstallBoot=null;SettingsStore.Save(_settings);}
            if(_settings.CableDefaultsBefore!=null) {
                var changes=CablePolicy.DefaultChanges(_settings.CableDefaultsBefore,_currentDefaults);
                _defaults.Text=changes.Length==0?"":Localization.T("cableDefaultsChanged")+"\n"+string.Join("\n",changes.Select(key=>$"{key}: {_settings.CableDefaultsBefore.GetValueOrDefault(key) ?? "—"} → {_currentDefaults.GetValueOrDefault(key) ?? "—"}"));
            }
        }catch { _condition=null;_status.Text=Localization.T("cableDetectionFailed"); }
        EnableInstall();
    }
    private async Task Install(bool online) {
        if(_busy || _consent.IsChecked!=true)return;
        await Refresh();if(_condition!=CableCondition.Missing || CablePolicy.AwaitingReboot(_settings.CableInstallBoot,CablePolicy.BootIdentity))return;
        _busy=true;EnableInstall();_status.Text=Localization.T("cablePreparing");
        try {
            var installer=await Task.Run(()=>CablePackage.Prepare(_manifest,online));
            // Recheck after preparation: another app or user may have installed the cable meanwhile.
            await Refresh();if(_condition!=CableCondition.Missing) return;
            Signature.Verify(installer,_manifest.InstallerSigner);
            if(_beforeInstall!=null)await _beforeInstall();
            var before=new Dictionary<string,string?>(_currentDefaults);var boot=CablePolicy.BootIdentity;
            using var process=Process.Start(new ProcessStartInfo(installer) {UseShellExecute=true,Verb="runas",WorkingDirectory=System.IO.Path.GetDirectoryName(installer)!});
            if(process==null)throw new InvalidOperationException();
            _settings.CableDefaultsBefore=before;_settings.CableInstallBoot=boot;SettingsStore.Save(_settings);
            await process.WaitForExitAsync();await Refresh();
            if(_condition==CableCondition.Missing) {_settings.CableInstallBoot=null;SettingsStore.Save(_settings);_status.Text=Localization.T("cableNotInstalled");}
        }catch(Win32Exception ex) when(ex.NativeErrorCode==1223) {_status.Text=Localization.T("cableCancelled");}
        catch {_status.Text=Localization.T("cableFailed");}
        finally {_busy=false;_consent.IsChecked=false;EnableInstall();}
    }
    private static void Open(string target) {try {Process.Start(new ProcessStartInfo(target) {UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,"NoMoreBacknoise++");}}
}
