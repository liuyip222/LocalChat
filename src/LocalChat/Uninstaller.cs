using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalChat;

public sealed record UninstallPlan(string[] DataFiles, string[] ConfigFiles, string[] ProgramFiles, string[] EmptyDirectories);

public static class Uninstaller
{
    private static readonly Regex OwnedName = new("^[0-9a-f]{32}(\\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string PendingPath => Path.Combine(Path.GetDirectoryName(Settings.ConfigPath)!, "uninstall.pending");

    public static bool IsRunning()
    {
        if (!File.Exists(PendingPath)) return false;
        try
        {
            if (int.TryParse(File.ReadAllText(PendingPath), out var id))
            {
                try
                {
                    using var worker = Process.GetProcessById(id);
                    if (!worker.HasExited && worker.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs((File.GetLastWriteTime(PendingPath) - worker.StartTime).TotalSeconds) < 30) return true;
                }
                catch (ArgumentException) { }
            }
            File.Delete(PendingPath); // Recover a marker left by a terminated helper or a restart.
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    // Build a file-by-file plan. Never recursively remove a user-selected directory.
    public static UninstallPlan BuildPlan(Settings settings, string executable, string? configDirectory = null, string? temporaryDirectory = null)
    {
        var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        configDirectory ??= Path.GetDirectoryName(Settings.ConfigPath)!;
        temporaryDirectory ??= Path.GetTempPath();
        var configPath = Path.Combine(configDirectory, "settings.json");
        var defaultData = Path.Combine(configDirectory, "Data");
        var roots = (settings.PreviousDataDirectories ?? []).Append(settings.DataDirectory).Append(defaultData).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var rootValue in roots)
        {
            var root = ValidatePath(rootValue);
            if (!Directory.Exists(root)) continue;
            if (!root.Equals(Path.GetFullPath(settings.DataDirectory), StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(root, "localchat.db"))) continue;
            foreach (var name in new[] { "localchat.db", "localchat.db-wal", "localchat.db-shm", "localchat.db-journal" }) AddFile(data, Path.Combine(root, name));
            CollectOwned(Path.Combine(root, "attachments"), data, directories);
            CollectOwned(Path.Combine(root, "cache"), data, directories);
            directories.Add(root);
        }
        var clipboardRoot = Path.Combine(temporaryDirectory, "LocalChat");
        CollectOwned(clipboardRoot, data, directories);
        CollectOwned(Path.Combine(temporaryDirectory, ".net", "LocalChat"), data, directories, allFiles: true);
        var program = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        executable = ValidatePath(executable);
        if (!Path.GetFileName(executable).Equals("LocalChat.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("请从发布版 LocalChat.exe 使用卸载功能，开发宿主不执行自删除。");
        // Only these published files belong to the app; preserve sources and unrelated files.
        AddFile(program, executable);
        AddFile(program, Path.ChangeExtension(executable, ".pdb"));
        directories.Add(Path.GetDirectoryName(executable)!);
        directories.Add(configDirectory);
        return new(data.ToArray(), [ValidatePath(configPath), ValidatePath(configPath + ".tmp")], program.ToArray(), directories.OrderByDescending(p => p.Length).ToArray());
    }

    private static void AddFile(HashSet<string> files, string path)
    {
        path = ValidatePath(path);
        if (File.Exists(path)) files.Add(path);
    }

    private static void CollectOwned(string root, HashSet<string> files, HashSet<string> directories, bool allFiles = false)
    {
        root = ValidatePath(root);
        if (!Directory.Exists(root)) return;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ValidatePath(directory);
            directories.Add(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ValidatePath(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
                else if (allFiles || OwnedName.IsMatch(Path.GetFileName(entry))) files.Add(Path.GetFullPath(entry));
            }
        }
    }

    public static string ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("卸载路径必须是绝对路径。");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Equals(Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new IOException("不能将磁盘根目录作为卸载目标。");
        for (string? cursor = full; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("卸载目标包含链接或重定向目录，请先处理该目录：" + cursor);
        return full;
    }

    public static async Task StartAsync(UninstallPlan plan)
    {
        var folder = Path.Combine(Path.GetTempPath(), "LocalChat-Uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "uninstall.ps1");
        var ready = Path.Combine(folder, "ready");
        var error = Path.Combine(folder, "error.txt");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan)));
        var contents = Script.Replace("__PAYLOAD__", payload).Replace("__PARENT__", Environment.ProcessId.ToString())
            .Replace("__MARKER__", Convert.ToBase64String(Encoding.UTF8.GetBytes(PendingPath)));
        await File.WriteAllTextAsync(script, contents, new UTF8Encoding(true));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-File"); info.ArgumentList.Add(script);
        using var process = Process.Start(info) ?? throw new IOException("无法启动卸载清理程序。");
        for (var i = 0; i < 150; i++)
        {
            if (File.Exists(ready)) return;
            if (File.Exists(error)) throw new IOException(await File.ReadAllTextAsync(error));
            if (process.HasExited) throw new IOException("卸载清理程序未能启动（可能受系统脚本策略限制）。应用和数据未卸载。");
            await Task.Delay(100);
        }
        // The worker also times out without deleting data if the main process remains open.
        throw new IOException("卸载程序准备超时，当前应用未退出，尚未执行清理。请稍后重试。");
    }

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $folder = $PSScriptRoot
        $ready = Join-Path $folder 'ready'
        $errorFile = Join-Path $folder 'error.txt'
        $marker = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MARKER__'))
        $plan = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PAYLOAD__')) | ConvertFrom-Json
        $parentId = __PARENT__
        function Assert-Path([string]$path) {
            $full = [IO.Path]::GetFullPath($path).TrimEnd('\')
            if ($full -ne $path -or $full -eq [IO.Path]::GetPathRoot($path).TrimEnd('\')) { throw "不安全的卸载路径：$path" }
            $cursor = $full
            while ($cursor) {
                if (Test-Path -LiteralPath $cursor) {
                    $item = Get-Item -LiteralPath $cursor -Force
                    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "卸载路径包含链接：$cursor" }
                }
                $cursor = [IO.Path]::GetDirectoryName($cursor)
            }
        }
        function Remove-File([string]$path) {
            Assert-Path $path
            for ($attempt = 0; $attempt -lt 15; $attempt++) {
                if (!(Test-Path -LiteralPath $path)) { return }
                if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "预期为文件：$path" }
                try { Remove-Item -LiteralPath $path -Force; return }
                catch { if ($attempt -eq 14) { throw }; Start-Sleep -Milliseconds 500 }
            }
        }
        try {
            Add-Type -AssemblyName PresentationFramework
            foreach ($path in @($plan.DataFiles) + @($plan.ConfigFiles) + @($plan.ProgramFiles) + @($plan.EmptyDirectories) + @($marker)) { Assert-Path $path }
            $parent = Get-Process -Id $parentId -ErrorAction Stop
            [IO.File]::WriteAllText($marker, $PID.ToString())
            [IO.File]::WriteAllText($ready, 'ready')
            if (!$parent.WaitForExit(30000)) { throw '主程序尚未退出，清理已取消；没有删除任何数据。' }
            foreach ($path in $plan.DataFiles) { Remove-File $path }
            # Keep the executable and configuration if data removal fails, so uninstall can be retried.
            foreach ($path in $plan.ProgramFiles) { Remove-File $path }
            foreach ($path in $plan.ConfigFiles) { Remove-File $path }
            Remove-File $marker
            foreach ($path in $plan.EmptyDirectories) {
                Assert-Path $path
                if ((Test-Path -LiteralPath $path -PathType Container) -and !(Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) {
                    Remove-Item -LiteralPath $path -Force
                }
            }
            [System.Windows.MessageBox]::Show('LocalChat 已卸载，聊天记录和内部附件已清理。已导出的文件不受影响。', '卸载完成') | Out-Null
        }
        catch {
            [IO.File]::WriteAllText($errorFile, $_.Exception.Message)
            if (Test-Path -LiteralPath $marker -PathType Leaf) { Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue }
            try { [System.Windows.MessageBox]::Show("卸载未完成：$($_.Exception.Message)`n详情：$errorFile", 'LocalChat 卸载失败') | Out-Null } catch { }
        }
        finally {
            if (Test-Path -LiteralPath $ready -PathType Leaf) { Remove-Item -LiteralPath $ready -Force -ErrorAction SilentlyContinue }
            # Delete only our exact helper script; never recursively delete the temporary folder.
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            if (!(Get-ChildItem -LiteralPath $folder -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $folder -Force -ErrorAction SilentlyContinue }
        }
        """;
}
