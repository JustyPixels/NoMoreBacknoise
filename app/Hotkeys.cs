using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
namespace NoMoreBacknoise;
public sealed class Hotkeys : IDisposable {
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd,int id);
    private readonly HwndSource _source; public event Action<int>? Pressed;
    public Hotkeys(IntPtr handle) { _source=HwndSource.FromHwnd(handle); _source.AddHook(Hook); }
    public void Apply(string mute,string bypass) {
        var m=Parse(mute); var b=Parse(bypass); if(m != null && m==b) throw new ArgumentException("Mute and bypass shortcuts must differ.");
        UnregisterHotKey(_source.Handle,1); UnregisterHotKey(_source.Handle,2);
        if(m != null && !RegisterHotKey(_source.Handle,1,m.Value.Modifiers|0x4000,m.Value.Key)) throw new InvalidOperationException("Mute shortcut is already in use.");
        if(b != null && !RegisterHotKey(_source.Handle,2,b.Value.Modifiers|0x4000,b.Value.Key)) { UnregisterHotKey(_source.Handle,1); throw new InvalidOperationException("Bypass shortcut is already in use."); }
    }
    public static (uint Modifiers,uint Key)? Parse(string value) {
        if(string.IsNullOrWhiteSpace(value)) return null; uint modifiers=0; Key key=Key.None;
        foreach(var token in value.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)) {
            switch(token.ToUpperInvariant()) { case "CTRL": case "CONTROL": modifiers|=2; break; case "ALT":modifiers|=1;break;case "SHIFT":modifiers|=4;break;default:if(key!=Key.None || !Enum.TryParse(token,true,out key) || key==Key.None) throw new ArgumentException("Use a shortcut such as Ctrl+Shift+M.");break; }
        }
        if(modifiers==0 || key==Key.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) throw new ArgumentException("A shortcut requires Ctrl, Alt, or Shift and one normal key.");
        return (modifiers,(uint)KeyInterop.VirtualKeyFromKey(key));
    }
    private IntPtr Hook(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled) { if(message==0x0312) { Pressed?.Invoke(wParam.ToInt32()); handled=true; } return IntPtr.Zero; }
    public void Dispose() { UnregisterHotKey(_source.Handle,1);UnregisterHotKey(_source.Handle,2);_source.RemoveHook(Hook); }
}
