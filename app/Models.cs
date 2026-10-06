using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace NoMoreBacknoise;
public class ProcessingSettings {
    public string Engine { get; set; } = "auto"; public string Backend { get; set; } = "cpu";
    public double Strength { get; set; } = 75; public double AttenuationDb { get; set; } = 35;
    public double SpeechThreshold { get; set; } = .35; public double AttackMs { get; set; } = 2;
    public double HoldMs { get; set; } = 180; public double ReleaseMs { get; set; } = 140;
    public double FloorDb { get; set; } = -35; public double GainDb { get; set; } = 0; public bool GateEnabled { get; set; } = true;
    public ProcessingSettings Copy() => JsonSerializer.Deserialize<ProcessingSettings>(JsonSerializer.Serialize(this, App.Json), App.Json)!;
    public void Validate() {
        if (Engine is not ("auto" or "rnnoise" or "deepfilter") || Backend != "cpu") throw new InvalidDataException("Unsupported engine or backend.");
        foreach (var (value, low, high) in new[] {(Strength,0d,100d),(AttenuationDb,0d,60d),(SpeechThreshold,.05,.95),(AttackMs,.1,100d),(HoldMs,0d,2000d),(ReleaseMs,1d,2000d),(FloorDb,-80d,0d),(GainDb,-12d,12d)}) if (!double.IsFinite(value) || value < low || value > high) throw new InvalidDataException("Processing value outside its supported range.");
    }
}
public class UserSettings {
    public int Version { get; set; } = 2; public bool FirstLaunchComplete { get; set; }
    public string Language { get; set; } = "en"; public string? InputId { get; set; } public string? OutputId { get; set; }
    public bool WindowsStartup { get; set; } public bool AutoProcess { get; set; } public bool CloseToTray { get; set; } = true;
    public bool UpdateChecks { get; set; } public DateTimeOffset? LastUpdateCheck { get; set; }
    public bool CableUpdateChecks { get; set; } public DateTimeOffset? LastCableUpdateCheck { get; set; }
    public bool CableSetupSeen { get; set; }
    public long? CableInstallBoot { get; set; }
    public Dictionary<string,string?>? CableDefaultsBefore { get; set; }
    public string MuteHotkey { get; set; } = ""; public string BypassHotkey { get; set; } = "";
    public ProcessingSettings Processing { get; set; } = new(); public Dictionary<string, ProcessingSettings> Profiles { get; set; } = new();
}
public record Endpoint(string Id, string Name, string Direction, string? InstanceId=null, string State="Active", uint? FormFactor=null) {
    public bool IsStandardCable { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverProvider { get; set; }
    public string? HardwareId { get; set; }
    public override string ToString() => Name;
}
public static class SettingsStore {
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoMoreBacknoise");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static UserSettings Load() {
        try { if (!File.Exists(FilePath) || new FileInfo(FilePath).Length>1048576) return new(); return Parse(File.ReadAllText(FilePath)); }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return new(); }
    }
    public static UserSettings Parse(string json) {
        var settings=JsonSerializer.Deserialize<UserSettings>(json,App.Json) ?? new();
        if(settings.Version is not (1 or 2) || settings.Processing==null || settings.Profiles==null || settings.Language==null || settings.MuteHotkey==null || settings.BypassHotkey==null) throw new InvalidDataException("Unsupported settings.");
        settings.Processing.Validate(); foreach(var profile in settings.Profiles.Values) { if(profile==null)throw new InvalidDataException("Invalid profile.");profile.Validate(); }
        if(settings.Version==1) { settings.CableUpdateChecks=false;settings.LastCableUpdateCheck=null; }
        settings.Version=2; return settings;
    }
    public static void Save(UserSettings settings) { settings.Processing.Validate(); Directory.CreateDirectory(DirectoryPath); var temp = FilePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(settings, App.Json)); File.Move(temp, FilePath, true); }
}
