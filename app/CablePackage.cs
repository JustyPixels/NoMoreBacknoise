using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
namespace NoMoreBacknoise;
public sealed class CableManifest {
    public int SchemaVersion {get;set;}
    public string Product {get;set;}="";public string Vendor {get;set;}="";public string Package {get;set;}="";public string DriverVersion {get;set;}="";
    public string Url {get;set;}="";public string Sha256 {get;set;}="";public string Installer {get;set;}="";public string Catalog {get;set;}="";
    public string InstallerSigner {get;set;}="";public string CatalogSigner {get;set;}="";public string InstallMode {get;set;}="";public bool SilentInstallationValidated {get;set;}
    public string Site {get;set;}="";public string License {get;set;}="";public string Donate {get;set;}="";
    public static CableManifest Load() => JsonSerializer.Deserialize<CableManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"dependencies","vb-cable.json")),App.Json) ?? throw new InvalidDataException("Missing manifest.");
    public void Validate() {
        if(SchemaVersion!=1 || InstallMode!="interactive" || SilentInstallationValidated || !Version.TryParse(DriverVersion,out _) || Sha256.Length!=64 || !Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Unapproved manifest.");
        foreach(var file in new[]{Package,Installer,Catalog})if(Path.GetFileName(file)!=file || file.Contains('\\') || file.Contains('/') || string.IsNullOrEmpty(file))throw new InvalidDataException("Invalid dependency path.");
        var uri=new Uri(Url);if(uri.Scheme!="https" || uri.Host!="download.vb-audio.com" || uri.AbsolutePath!="/Download_CABLE/"+Package)throw new InvalidDataException("Unapproved package origin.");
    }
}
internal static class CablePackage {
    public static void VerifyHash(string path,CableManifest manifest) {
        manifest.Validate();using var file=File.OpenRead(path);
        if(!Convert.ToHexString(SHA256.HashData(file)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(Localization.T("cableInvalid"));
    }
    public static async Task<string> Prepare(CableManifest manifest,bool download,string? stageRoot=null) {
        manifest.Validate();
        // Stage on the system user-data volume. A fresh private directory avoids stale extracted binaries.
        var stage=Path.Combine(stageRoot ?? Path.Combine(SettingsStore.DirectoryPath,"cable-setup"),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        var archive=Path.Combine(stage,manifest.Package);
        if(download) {
            using var client=new HttpClient {Timeout=TimeSpan.FromSeconds(60)};
            using var response=await client.GetAsync(manifest.Url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync();await using(var file=File.Create(archive)) {
                var buffer=new byte[65536];long total=0;int read;
                while((read=await stream.ReadAsync(buffer))>0) { total+=read;if(total>16*1024*1024)throw new InvalidDataException("Package too large.");await file.WriteAsync(buffer.AsMemory(0,read)); }
            }
        }else File.Copy(Path.Combine(AppContext.BaseDirectory,"dependencies",manifest.Package),archive);
        VerifyHash(archive,manifest);var folder=Path.Combine(stage,"official");ZipFile.ExtractToDirectory(archive,folder);
        var installer=Path.Combine(folder,manifest.Installer);Signature.Verify(installer,manifest.InstallerSigner);
        Signature.Verify(Path.Combine(folder,manifest.Catalog),manifest.CatalogSigner);
        return installer;
    }
}
internal static class Signature {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct FileInfo {public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Path;public IntPtr Handle;public IntPtr Subject;}
    [StructLayout(LayoutKind.Sequential)] private struct TrustData {public uint Size;public IntPtr Policy;public IntPtr Sip;public uint Ui;public uint Revocation;public uint Choice;public IntPtr File;public uint StateAction;public IntPtr State;public IntPtr Url;public uint Flags;public uint Context;public IntPtr SignatureSettings;}
    [DllImport("wintrust.dll",ExactSpelling=true)]private static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
    public static void Verify(string path,string expectedSigner) {
        var info=new FileInfo {Size=(uint)Marshal.SizeOf<FileInfo>(),Path=path};var memory=Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());Marshal.StructureToPtr(info,memory,false);
        var data=new TrustData {Size=(uint)Marshal.SizeOf<TrustData>(),Ui=2,Choice=1,File=memory,Flags=0x1000}; // cache-only URL retrieval: works offline, no trust bypass
        var action=new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try {
            if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)throw new InvalidDataException(Localization.T("cableInvalid"));
            // .NET's new loader does not extract Authenticode certificates from PE/catalog files.
#pragma warning disable SYSLIB0057
            using var signed=X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var certificate=X509CertificateLoader.LoadCertificate(signed.GetRawCertData());
            if(!certificate.GetNameInfo(X509NameType.SimpleName,false).Equals(expectedSigner,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(Localization.T("cableInvalid"));
        }finally {Marshal.DestroyStructure<FileInfo>(memory);Marshal.FreeHGlobal(memory);}
    }
}
