using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NoMoreBacknoise;
public sealed class HostClient : IAsyncDisposable {
    private NamedPipeServerStream? _pipe; private StreamWriter? _writer; private Process? _process;
    private readonly SemaphoreSlim _writeLock = new(1,1);
    private readonly ConcurrentDictionary<string,TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cancel = new(); private Task? _readerTask;
    public event Action<JsonElement>? Event; public event Action<string>? Disconnected;
    public async Task Connect() {
        var name = "nmb-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var executable = Path.Combine(AppContext.BaseDirectory,"nmb-host.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Audio host missing. Use the complete portable package or scripts/package.ps1.",executable);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        info.ArgumentList.Add("--pipe"); info.ArgumentList.Add(name);
        _process = Process.Start(info)!;
        _ = _process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _pipe.WaitForConnectionAsync(timeout.Token);
        _writer = new StreamWriter(_pipe,new UTF8Encoding(false),16384,leaveOpen:true) { AutoFlush = true };
        _readerTask = ReadLoop();
    }
    public async Task<JsonElement> Request(string operation, object? body = null) {
        if (_writer == null || _pipe?.IsConnected != true) throw new IOException("Audio host disconnected.");
        var id = Guid.NewGuid().ToString("N"); var obj = new System.Collections.Generic.Dictionary<string,object?> { ["version"] = 1,["id"] = id,["op"] = operation };
        if (body != null) foreach (var p in JsonSerializer.SerializeToElement(body,App.Json).EnumerateObject()) obj[p.Name] = p.Value;
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); _pending[id] = completion;
        try {
            await _writeLock.WaitAsync(_cancel.Token);
            try { await _writer.WriteLineAsync(JsonSerializer.Serialize(obj,App.Json)); } finally { _writeLock.Release(); }
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (result.GetProperty("type").GetString() == "error") throw new InvalidOperationException(result.GetProperty("message").GetString());
            return result;
        } finally { _pending.TryRemove(id,out _); }
    }
    private async Task ReadLoop() {
        try {
            using var reader = new StreamReader(_pipe!,Encoding.UTF8,false,16384,leaveOpen:true);
            while (!_cancel.IsCancellationRequested) {
                var line = await reader.ReadLineAsync(_cancel.Token); if (line == null) break;
                if (line.Length > 262144) throw new IOException("Oversized host message.");
                using var document = JsonDocument.Parse(line); var root = document.RootElement.Clone();
                if (root.GetProperty("version").GetInt32() != 1) throw new IOException("Unsupported host protocol.");
                if (root.TryGetProperty("requestId",out var id) && _pending.TryGetValue(id.GetString()!,out var completion)) completion.TrySetResult(root);
                else Event?.Invoke(root);
            }
        } catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or JsonException) { if (!_cancel.IsCancellationRequested) Disconnected?.Invoke(e.Message); }
        finally { foreach (var completion in _pending.Values) completion.TrySetException(new IOException("Audio host disconnected.")); if (!_cancel.IsCancellationRequested) Disconnected?.Invoke("Audio host stopped. Retry to reconnect."); }
    }
    public async ValueTask DisposeAsync() {
        try { if (_pipe?.IsConnected == true && !_cancel.IsCancellationRequested) await Request("shutdown").WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        _cancel.Cancel(); _pipe?.Dispose();
        if (_process is { HasExited:false }) { try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { _process.Kill(entireProcessTree:true); } }
        _process?.Dispose();
    }
}
