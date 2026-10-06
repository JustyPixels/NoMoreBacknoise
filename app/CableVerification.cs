using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
namespace NoMoreBacknoise;
internal static class CableVerification {
    public static void Run(string directory) {
        Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(string name,bool pass) {if(!pass)throw new Exception(name);checks.Add(name);}
        void Reject(string name,Action action) {try{action();}catch(Exception ex) when(ex is InvalidDataException or FileNotFoundException) {checks.Add(name);return;}throw new Exception(name+" unexpectedly accepted");}
        var migrated=SettingsStore.Parse("""
            {"version":1,"firstLaunchComplete":true,"language":"pt-BR","inputId":"physical-stable-id","outputId":"cable-stable-id","windowsStartup":true,"autoProcess":true,"closeToTray":false,"updateChecks":true,"muteHotkey":"Ctrl+F8","bypassHotkey":"Ctrl+F9","cableUpdateChecks":true,"processing":{"engine":"rnnoise","strength":48},"profiles":{"quiet":{"engine":"deepfilter","gainDb":-2}}}
            """);
        Check("v1 migration preserves devices, profiles, hotkeys and preferences; cable background checks stay off",migrated.Version==2 && !migrated.CableUpdateChecks && migrated.InputId=="physical-stable-id" && migrated.OutputId=="cable-stable-id" && migrated.WindowsStartup && migrated.AutoProcess && !migrated.CloseToTray && migrated.UpdateChecks && migrated.MuteHotkey=="Ctrl+F8" && migrated.BypassHotkey=="Ctrl+F9" && migrated.Processing.Strength==48 && migrated.Profiles["quiet"].GainDb==-2 && migrated.Language=="pt-BR" && migrated.FirstLaunchComplete);
        Check("fresh cable network checks default off",!new UserSettings().CableUpdateChecks);
        var versionTwo=SettingsStore.Parse(JsonSerializer.Serialize(migrated,App.Json));Check("v2 round trip",versionTwo.Profiles.Count==1 && versionTwo.InputId==migrated.InputId);
        Reject("future settings rejected",()=>SettingsStore.Parse("{\"version\":99}"));
        Check("standard vendor hardware identity",CableDevices.Standard("VBAudioVACWDM","VB-Audio Software") && !CableDevices.Standard("VBAudioVACWDM_A","VB-Audio Software") && !CableDevices.Standard("VBAudioVACWDM","Another vendor"));
        var driver=new CableDriver("ROOT\\MEDIA\\0000","VBAudioVACWDM","VB-Audio Software","3.3.1.7",true);
        var render=new Endpoint("render","Renamed audio bus","render") {IsStandardCable=true};var capture=new Endpoint("capture","Renamed microphone bus","capture") {IsStandardCable=true};
        Check("missing driver",CablePolicy.Classify([],[])==CableCondition.Missing);
        foreach(var version in new[]{"2.1.0.0","3.3.1.7","9.0.0.0"})Check("renamed endpoints and reusable driver "+version,CablePolicy.Classify([driver with{Version=version}],[render,capture])==CableCondition.Ready);
        Check("disabled endpoints distinct from absence",CablePolicy.Classify([driver],[render,capture with{State="Disabled"}])==CableCondition.Disabled);
        Check("partial endpoints",CablePolicy.Classify([driver],[render])==CableCondition.Partial);
        Check("unstarted driver",CablePolicy.Classify([driver with{Started=false}],[render,capture])==CableCondition.Partial);
        Check("unrelated endpoints not a cable",CablePolicy.Classify([driver],[render with{IsStandardCable=false},capture with{IsStandardCable=false}])==CableCondition.Partial);
        Check("pending boot, post reboot and unavailable boot identity",CablePolicy.AwaitingReboot(1000,1000) && !CablePolicy.AwaitingReboot(1000,1001) && CablePolicy.AwaitingReboot(1000,-1) && !CablePolicy.AwaitingReboot(null,1000));
        Check("all six default roles compared",CablePolicy.DefaultChanges(new(){{"capture/communications","old"},{"render/console",null}},new(){{"capture/communications","new"},{"render/console","speakers"}}).Length==2);
        Check("official package discovery excludes A+B, mirrors and downloads with arbitrary names",CablePolicy.Packages("https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip https://mirror.invalid/Download_CABLE/VBCABLE_Driver_Pack46.zip https://download.vb-audio.com/Download_CABLE/VBCABLE_AB.zip").Length==1);
        Check("preview and stable release ordering",ReleaseVersion.IsNewer("v0.2.0-preview.2","0.2.0-preview.1") && ReleaseVersion.IsNewer("v0.2.0","0.2.0-preview.1") && !ReleaseVersion.IsNewer("v0.2.0-preview.1","0.2.0-preview.1") && !ReleaseVersion.IsNewer("v0.1.0-preview.99","0.2.0-preview.1"));
        var manifest=CableManifest.Load();manifest.Validate();Check("interactive-only approved manifest",manifest.InstallMode=="interactive" && !manifest.SilentInstallationValidated);
        var archive=Path.Combine(AppContext.BaseDirectory,"dependencies",manifest.Package);CablePackage.VerifyHash(archive,manifest);checks.Add("included official ZIP SHA256");
        Reject("missing package rejected",()=>CablePackage.VerifyHash(Path.Combine(directory,"absent.zip"),manifest));
        var invalid=Path.Combine(directory,"invalid.zip");File.WriteAllText(invalid,"synthetic invalid package");Reject("invalid package rejected",()=>CablePackage.VerifyHash(invalid,manifest));
        var installer=CablePackage.Prepare(manifest,false,directory).GetAwaiter().GetResult();checks.Add("offline full extraction and cached Authenticode signer checks; installer not executed");
        Check("vendor README retained byte for byte",File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(installer)!,"readme.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"dependencies","VB-CABLE-readme.txt"))));
        var tampered=Path.Combine(directory,"tampered.exe");var binary=File.ReadAllBytes(installer);binary[512]^=1;File.WriteAllBytes(tampered,binary);Reject("tampered signed executable rejected",()=>Signature.Verify(tampered,manifest.InstallerSigner));
        Reject("wrong signer rejected",()=>Signature.Verify(installer,"Wrong vendor"));
        var drivers=CableDevices.Drivers();
        var actual=Task.Run(async()=> {await using var host=new HostClient();await host.Connect();var response=await host.Request("devices",new{includeInactive=true});var endpoints=JsonSerializer.Deserialize<List<Endpoint>>(response.GetProperty("devices"),App.Json)!;CableDevices.Identify(endpoints,drivers);return endpoints;}).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(directory,"cable-validation.json"),JsonSerializer.Serialize(new {date=DateTimeOffset.UtcNow,checks,actualStandardCableDrivers=drivers.Count,actualDriverVersions=drivers.Select(d=>d.Version),actualCondition=CablePolicy.Classify(drivers,actual).ToString(),actualRecognizedRender=actual.Count(e=>e.IsStandardCable && e.Direction=="render" && e.State=="Active"),actualRecognizedCapture=actual.Count(e=>e.IsStandardCable && e.Direction=="capture" && e.State=="Active"),installerExecuted=false,driverChanged=false,pending=new[]{"Fresh Windows 10/11 isolated install/uninstall", "Actual UAC cancellation", "Actual driver reboot and renamed/disabled endpoints", "Human speech/noise quality", "Discord and TeamSpeak local and simultaneous capture"}},App.Json));
    }
}
