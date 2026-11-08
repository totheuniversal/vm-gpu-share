using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace VM_Manager.HyperV
{
    /// <summary>
    /// A hidden Windows PowerShell 5.1 process with GpuLib.ps1 loaded. The Hyper-V
    /// module only runs in Windows PowerShell, which .NET 10 can't host in-process.
    /// Requests run one at a time; progress lines arrive through <see cref="Log"/>.
    /// </summary>
    public sealed class PowerShellWorker : IDisposable
    {
        static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        readonly Process process;
        readonly SemaphoreSlim gate = new(1, 1);
        TaskCompletionSource<string>? pending;

        public event Action<string>? Log;

        public PowerShellWorker()
        {
            string script = ReadResource("GpuLib.ps1") + Environment.NewLine + ReadResource("Worker.ps1");
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NoLogo -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                StandardInputEncoding = new UTF8Encoding(false),
            };
            process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start powershell.exe");
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log?.Invoke(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        static string ReadResource(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing embedded resource " + name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        void OnOutput(object sender, DataReceivedEventArgs e)
        {
            string? line = e.Data;
            if (line == null) { pending?.TrySetException(new InvalidOperationException("PowerShell worker stopped.")); return; }
            if (line.StartsWith("LOG ")) Log?.Invoke(line[4..]);
            else if (line.StartsWith("RESULT ")) pending?.TrySetResult(line[7..]);
            else if (line.StartsWith("ERROR ")) pending?.TrySetException(new PowerShellException(line[6..]));
            else if (line.Trim().Length > 0) Log?.Invoke(line);   // e.g. WARNING: lines
        }

        /// <summary>Runs a script ($args holds <paramref name="args"/>) and returns its output as JSON.</summary>
        public async Task<string> RunAsync(string script, params object[] args)
        {
            await gate.WaitAsync();
            try
            {
                if (process.HasExited) throw new InvalidOperationException("PowerShell worker stopped.");
                pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                string request = JsonSerializer.Serialize(new { script, args });
                await process.StandardInput.WriteLineAsync(request);
                await process.StandardInput.FlushAsync();
                return await pending.Task;
            }
            finally { gate.Release(); }
        }

        public async Task<List<T>> RunAsync<T>(string script, params object[] args)
        {
            string json = await RunAsync(script, args);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }

        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            process.Dispose();
            gate.Dispose();
        }
    }

    public sealed class PowerShellException(string message) : Exception(message);
}
