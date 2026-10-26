using System.IO;
using System.Windows;

namespace LocalChat;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Uninstaller.IsRunning())
        {
            MessageBox.Show("LocalChat 正在卸载，请等待清理完成后再操作。", "LocalChat");
            Shutdown();
            return;
        }
        instance = new Mutex(true, "LocalChat.Desktop.SingleInstance", out var first);
        if (!first) { MessageBox.Show("LocalChat 已经运行，请切换到已打开的窗口。", "LocalChat"); Shutdown(); return; }
        try
        {
            var settings = Settings.Load();
            var dataOption = Array.IndexOf(e.Args, "--data-dir");
            if (dataOption >= 0 && dataOption + 1 < e.Args.Length)
                settings.DataDirectory = Path.GetFullPath(e.Args[dataOption + 1]);
            var store = new ChatStore(settings.DataDirectory);
            var window = new MainWindow(settings, store);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "LocalChat"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
