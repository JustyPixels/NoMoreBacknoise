using System;
using System.Windows;
using System.Linq;
using System.Threading;
namespace NoMoreBacknoise;
public partial class App : Application {
    private Mutex? _instance;
    public static readonly System.Text.Json.JsonSerializerOptions Json = new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = false };
    protected override void OnStartup(StartupEventArgs e) {
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "NoMoreBacknoise++"); args.Handled = true; };
        Localization.Load(SettingsStore.Load().Language);
        if(e.Args.Contains("--verify-cable")) {
            try {CableVerification.Run(e.Args.Last());Shutdown(0);}
            catch(Exception exception) {System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args.Last(),"cable-error.txt"),exception.ToString());Shutdown(1);}return;
        }
        if(e.Args.Contains("--verify-ui")) {
            try { UiVerification.Run(e.Args.Last()); Shutdown(0); }
            catch(Exception exception) { System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args.Last(),"ui-error.txt"),exception.ToString()); Shutdown(1); }
            return;
        }
        _instance=new Mutex(true,"Local\\NoMoreBacknoise-"+System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value,out var created);
        if(!created) { MessageBox.Show("NoMoreBacknoise++ is already running. Open it from the tray.","NoMoreBacknoise++"); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
