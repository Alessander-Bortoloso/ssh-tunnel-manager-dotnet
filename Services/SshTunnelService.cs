using System.Diagnostics;
using System.Text;
using SshTunnelManager.Models;

namespace SshTunnelManager.Services;

public sealed class SshTunnelService : IDisposable
{
    private Process? _process;
    private bool _disposed;
    private readonly object _sync = new();

    public event Action<string>? LogReceived;
    public event Action<bool, string>? StateChanged;
    public event Action? TunnelReady;
    public bool IsRunning
    {
        get { lock (_sync) return _process is { HasExited: false }; }
    }

    public async Task ConnectAsync(ConnectionSettings settings, string appDirectory, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SshTunnelService));
        if (IsRunning) throw new InvalidOperationException("Já existe um processo SSH em execução.");
        Validate(settings);

        var keyPath = ResolvePath(settings.KeyPath, appDirectory);
        if (!File.Exists(keyPath))
            throw new FileNotFoundException($"A chave SSH não foi encontrada: {keyPath}. Ajuste o caminho na aba Settings.", keyPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(settings.SshExecutable) ? "ssh.exe" : settings.SshExecutable.Trim(),
            WorkingDirectory = appDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        // ArgumentList avoids quoting/injection bugs when paths contain spaces.
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(keyPath);
        startInfo.ArgumentList.Add("-N");
        startInfo.ArgumentList.Add("-T");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("-L");
        startInfo.ArgumentList.Add($"127.0.0.1:{settings.LocalPort}:{settings.RemoteHost}:{settings.RemotePort}");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ExitOnForwardFailure=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ServerAliveInterval=30");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ServerAliveCountMax=3");
        startInfo.ArgumentList.Add($"{settings.User}@{settings.Host}");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var readySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyText = $"Local forwarding listening on 127.0.0.1 port {settings.LocalPort}";

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            LogReceived?.Invoke(e.Data);
            if (e.Data.Contains(readyText, StringComparison.OrdinalIgnoreCase))
                readySignal.TrySetResult(true);
            if (e.Data.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("REMOTE HOST IDENTIFICATION HAS CHANGED", StringComparison.OrdinalIgnoreCase))
                errorSignal.TrySetResult(e.Data);
        };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke(e.Data); };
        process.Exited += (_, _) =>
        {
            string reason = process.ExitCode == 0 ? "Processo SSH encerrado." : $"Processo SSH terminou com código {process.ExitCode}.";
            StateChanged?.Invoke(false, reason);
            if (!readySignal.Task.IsCompleted)
                errorSignal.TrySetResult(reason);
        };

        try
        {
            if (!process.Start()) throw new InvalidOperationException("Não foi possível iniciar ssh.exe.");
            lock (_sync) _process = process;
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            LogReceived?.Invoke($"Iniciando túnel para {settings.User}@{settings.Host}; 127.0.0.1:{settings.LocalPort} → {settings.RemoteHost}:{settings.RemotePort}.");
            StateChanged?.Invoke(false, "Conectando…");

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
            var completed = await Task.WhenAny(readySignal.Task, errorSignal.Task, process.WaitForExitAsync(cancellationToken), timeoutTask);
            if (completed == readySignal.Task)
            {
                if (process.HasExited) throw new InvalidOperationException("O processo SSH encerrou antes de confirmar o túnel.");
                StateChanged?.Invoke(true, "Conectado");
                TunnelReady?.Invoke();
                LogReceived?.Invoke("Túnel SSH estabelecido.");
                return;
            }

            if (completed == errorSignal.Task)
                throw new InvalidOperationException(await errorSignal.Task);
            if (completed == timeoutTask)
                throw new TimeoutException("O SSH não confirmou o encaminhamento em 25 segundos. Verifique os Logs e a conectividade.");

            throw new InvalidOperationException($"O processo SSH encerrou durante a conexão (código {process.ExitCode}). Consulte os Logs.");
        }
        catch
        {
            await DisconnectAsync("Conexão não estabelecida; processo encerrado.");
            throw;
        }
    }

    public async Task DisconnectAsync(string message = "Desconectado pelo usuário.")
    {
        Process? process;
        lock (_sync) { process = _process; _process = null; }
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"Aviso ao encerrar SSH: {ex.Message}");
        }
        finally
        {
            process.Dispose();
            LogReceived?.Invoke(message);
            StateChanged?.Invoke(false, "Desconectado");
        }
    }

    private static void Validate(ConnectionSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Host) || s.Host.Equals("MEU-IP", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Informe o IP ou hostname do servidor na aba Settings.");
        if (string.IsNullOrWhiteSpace(s.User)) throw new InvalidOperationException("Informe o usuário SSH.");
        if (s.LocalPort is < 1 or > 65535 || s.RemotePort is < 1 or > 65535)
            throw new InvalidOperationException("As portas devem estar entre 1 e 65535.");
        if (string.IsNullOrWhiteSpace(s.RemoteHost)) throw new InvalidOperationException("Informe o host remoto do encaminhamento.");
        if (!Uri.TryCreate(s.BrowserUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Informe uma URL HTTP ou HTTPS válida para abrir no navegador.");
    }

    private static string ResolvePath(string path, string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Informe o caminho da chave privada na aba Settings.");
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(appDirectory, path));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var process = _process;
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
    }
}
