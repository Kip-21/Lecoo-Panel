using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal sealed class CliResult
{
    internal int? ExitCode;
    internal string Output = "";
    internal string Error = "";
    internal bool Success { get { return ExitCode == 0 && string.IsNullOrEmpty(Error); } }
    internal string Explanation
    {
        get
        {
            if (Error.Length != 0) return Error;
            if (Output.Length != 0) return Output;
            return ExitCode.HasValue ? "Код завершения CLI: " + ExitCode.Value : "CLI не завершилась.";
        }
    }
    internal string Diagnostic
    {
        get
        {
            return string.Join(Environment.NewLine, new[]
            {
                ExitCode.HasValue ? "Код завершения: " + ExitCode.Value : "Код завершения недоступен.",
                Output, Error.Length == 0 ? "" : "Ошибка: " + Error
            }.Where(x => x.Length > 0));
        }
    }
}

internal sealed class CliFields
{
    private readonly Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    internal CliFields(string output)
    {
        foreach (string line in output.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            if (value.Length > 0 && !fields.ContainsKey(key)) fields.Add(key, value);
        }
    }
    internal string Get(params string[] aliases)
    {
        foreach (string key in aliases)
        {
            string value;
            if (fields.TryGetValue(key, out value)) return value;
        }
        return "—";
    }
}

internal sealed class LecooClient
{
    private const string ServiceName = "LecooControlDaemon";
    internal static readonly string[] ReadCommands = { "temps", "fans", "battery", "charge" };
    private readonly string folder;

    internal LecooClient(string folder) { this.folder = folder; }

    internal ServiceControllerStatus? CurrentService()
    {
        using (var controller = new ServiceController(ServiceName))
        {
            try { return controller.Status; }
            catch (InvalidOperationException ex)
            {
                var win32 = ex.InnerException as System.ComponentModel.Win32Exception;
                if (win32 != null && win32.NativeErrorCode == 1060) return null;
                throw new InvalidOperationException("Не удалось проверить службу Lecoo. " + (win32 == null ? ex.Message : win32.Message), ex);
            }
        }
    }

    private static void OpenServices()
    {
        using (Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "services.msc"))
            { UseShellExecute = true })) { }
    }

    internal bool SetUpService()
    {
        ServiceControllerStatus? state;
        try { state = CurrentService(); }
        catch (InvalidOperationException)
        {
            OpenServices();
            return false;
        }
        if (state == ServiceControllerStatus.Running)
        {
            OpenServices();
            return false;
        }
        if (state.HasValue && state != ServiceControllerStatus.Stopped)
            throw new InvalidOperationException("Служба меняет состояние. Повторите через несколько секунд.");

        var installer = Path.Combine(folder, "install.bat");
        if (!state.HasValue && !File.Exists(installer))
            throw new FileNotFoundException("Поместите LecooPanel.exe рядом с install.bat из архива Lecoo.");

        var start = new ProcessStartInfo
        {
            FileName = state.HasValue ? Path.Combine(Environment.SystemDirectory, "sc.exe") : installer,
            Arguments = state.HasValue ? "start " + ServiceName : "",
            WorkingDirectory = folder, UseShellExecute = true, Verb = "runas"
        };
        using (var process = Process.Start(start))
        {
            if (process != null)
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Операция со службой завершилась с кодом " + process.ExitCode + ".");
            }
        }
        if (state.HasValue)
            using (var controller = new ServiceController(ServiceName))
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
        return true;
    }

    internal Dictionary<string, CliResult> ReadSnapshot()
    {
        var result = new Dictionary<string, CliResult>();
        foreach (var name in ReadCommands) result.Add(name, Cli(name));
        return result;
    }

    internal CliResult Cli(string arguments)
    {
        var path = Path.Combine(folder, "lecoo-ctrl.exe");
        if (!File.Exists(path))
            return new CliResult { Error = "Поместите LecooPanel.exe рядом с lecoo-ctrl.exe." };

        var start = new ProcessStartInfo(path, arguments)
        {
            WorkingDirectory = folder, CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(3500))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    return new CliResult { Error = "Служба Lecoo не ответила вовремя." };
                }
                if (!Task.WaitAll(new Task[] { stdout, stderr }, 1000))
                    return new CliResult { ExitCode = process.ExitCode, Error = "Не удалось прочитать ответ CLI." };

                return new CliResult { ExitCode = process.ExitCode, Output = Clean(stdout.Result), Error = Clean(stderr.Result) };
            }
        }
        catch (System.ComponentModel.Win32Exception ex) { return new CliResult { Error = ex.GetBaseException().Message }; }
        catch (InvalidOperationException ex) { return new CliResult { Error = ex.GetBaseException().Message }; }
        catch (IOException ex) { return new CliResult { Error = ex.GetBaseException().Message }; }
        catch (AggregateException ex) { return new CliResult { Error = ex.GetBaseException().Message }; }
    }

    private static string Clean(string value)
    {
        value = Regex.Replace(value, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        return Regex.Replace(value.Trim(), @"\r?\n", Environment.NewLine);
    }
}
