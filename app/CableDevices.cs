using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
namespace NoMoreBacknoise;

public record CableDriver(string InstanceId,string HardwareId,string Provider,string Version,bool Started);
// Read-only PnP inventory. Endpoint labels are deliberately not used for identity.
internal static class CableDevices {
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size;public Guid Class;public uint DevInst;public IntPtr Reserved; }
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr SetupDiGetClassDevsW(IntPtr guid,string? enumerator,IntPtr hwnd,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref DeviceInfo info);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set,ref DeviceInfo info,uint property,out uint type,byte[] data,uint length,out uint required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node,StringBuilder id,uint length,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node,string id,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent,uint child,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
    public static bool Standard(string hardware,string provider) => hardware.Split('\0').Any(id=>id.Equals("VBAudioVACWDM",StringComparison.OrdinalIgnoreCase) || id.Equals(@"ROOT\VBAudioVACWDM",StringComparison.OrdinalIgnoreCase)) && provider.Equals("VB-Audio Software",StringComparison.OrdinalIgnoreCase);
    public static List<CableDriver> Drivers() {
        var result=new List<CableDriver>();var set=SetupDiGetClassDevsW(IntPtr.Zero,null,IntPtr.Zero,4);
        if(set==new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            for(uint i=0;;i++) {
                var info=new DeviceInfo {Size=(uint)Marshal.SizeOf<DeviceInfo>()};
                if(!SetupDiEnumDeviceInfo(set,i,ref info)) { if(Marshal.GetLastWin32Error()!=259)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());break; }
                string Property(uint key) { var bytes=new byte[16384];return SetupDiGetDeviceRegistryPropertyW(set,ref info,key,out _,bytes,(uint)bytes.Length,out var size)?Encoding.Unicode.GetString(bytes,0,(int)size).TrimEnd('\0'):""; }
                var hardware=Property(1);if(!hardware.Contains("VBAudioVACWDM",StringComparison.OrdinalIgnoreCase))continue;
                using var driver=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\"+Property(9));
                var provider=driver?.GetValue("ProviderName") as string ?? Property(11);if(!Standard(hardware,provider))continue;
                var id=new StringBuilder(4096);if(CM_Get_Device_IDW(info.DevInst,id,4096,0)!=0)continue;
                var started=CM_Get_DevNode_Status(out var status,out var problem,info.DevInst,0)==0 && problem==0 && (status&8)!=0;
                result.Add(new(id.ToString(),"VBAudioVACWDM",provider,driver?.GetValue("DriverVersion") as string ?? "",started));
            }
        }finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
    public static void Identify(IEnumerable<Endpoint> endpoints,IReadOnlyList<CableDriver> drivers) {
        foreach(var endpoint in endpoints) {
            var id=endpoint.InstanceId;
            // Some endpoint stores omit PKEY_Device_InstanceId. The MMDEVAPI PnP ID is defined by Windows.
            if(string.IsNullOrEmpty(id))id=@"SWD\MMDEVAPI\"+endpoint.Id;
            CableDriver? match=null;
            if(CM_Locate_DevNodeW(out var node,id,0)==0) {
                for(var depth=0;depth<16;depth++) {
                    var current=new StringBuilder(4096);if(CM_Get_Device_IDW(node,current,4096,0)!=0)break;
                    match=drivers.FirstOrDefault(d=>d.InstanceId.Equals(current.ToString(),StringComparison.OrdinalIgnoreCase));if(match!=null)break;
                    if(CM_Get_Parent(out var parent,node,0)!=0 || parent==node)break;node=parent;
                }
            }
            endpoint.IsStandardCable=match!=null;endpoint.DriverVersion=match?.Version;endpoint.DriverProvider=match?.Provider;endpoint.HardwareId=match?.HardwareId;
        }
    }
}
