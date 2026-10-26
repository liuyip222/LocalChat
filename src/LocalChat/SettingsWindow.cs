using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LocalChat;

public sealed class SettingsWindow : Window
{
    private readonly TextBox data;
    private readonly TextBox export;
    private readonly CheckBox enter;
    private readonly TextBox windowWidth;
    private readonly TextBox windowHeight;
    public string DataDirectory => data.Text.Trim();
    public string ExportDirectory => export.Text.Trim();
    public bool EnterToSend => enter.IsChecked == true;
    public int DefaultWindowWidth => int.Parse(windowWidth.Text.Trim());
    public int DefaultWindowHeight => int.Parse(windowHeight.Text.Trim());
    public event Action<bool>? EnterToSendChanged;
    public bool UninstallRequested { get; private set; }
    public SettingsWindow(Settings settings)
    {
        Title = "LocalChat 设置"; Width = 650; Height = 660; MaxHeight = SystemParameters.WorkArea.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel { Margin = new Thickness(24) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var close = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            FontSize = 24,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            ToolTip = "返回聊天（不保存修改）",
            IsCancel = true
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭设置，返回聊天");
        close.Click += (_, _) => DialogResult = false;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = "应用设置", FontSize = 23, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var panel = new StackPanel();
        data = AddFolder(panel, "数据存储位置（聊天数据库和附件原件）", settings.DataDirectory);
        panel.Children.Add(new TextBlock { Text = "更改后复制现有数据到空目录；原目录保留，不自动删除。", FontSize = 11, Margin = new Thickness(0, 4, 0, 15) });
        export = AddFolder(panel, "默认保存位置（保存附件时复制到这里）", settings.ExportDirectory);
        enter = new CheckBox { Content = "Enter 发送，Shift+Enter 换行（关闭后使用 Ctrl+Enter 发送）", IsChecked = settings.EnterToSend, Margin = new Thickness(0, 22, 0, 16) };
        enter.Checked += (_, _) => EnterToSendChanged?.Invoke(true);
        enter.Unchecked += (_, _) => EnterToSendChanged?.Invoke(false);
        panel.Children.Add(enter);
        panel.Children.Add(new TextBlock { Text = "每次启动时的默认窗口大小", Margin = new Thickness(0, 4, 0, 10) });
        var dimensions = new StackPanel { Orientation = Orientation.Horizontal };
        dimensions.Children.Add(new TextBlock { Text = "宽度", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        windowWidth = new TextBox { Text = settings.DefaultWindowWidth.ToString(), Width = 100 };
        System.Windows.Automation.AutomationProperties.SetName(windowWidth, "默认窗口宽度");
        dimensions.Children.Add(windowWidth);
        dimensions.Children.Add(new TextBlock { Text = "高度", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 8, 0) });
        windowHeight = new TextBox { Text = settings.DefaultWindowHeight.ToString(), Width = 100 };
        System.Windows.Automation.AutomationProperties.SetName(windowHeight, "默认窗口高度");
        dimensions.Children.Add(windowHeight);
        panel.Children.Add(dimensions);
        panel.Children.Add(new TextBlock { Text = "逻辑像素，随系统缩放；最小 480 × 600，下次启动生效。", FontSize = 11, Margin = new Thickness(0, 8, 0, 18), TextWrapping = TextWrapping.Wrap });
        var save = new Button { Content = "保存设置", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            try
            {
                if (!Path.IsPathFullyQualified(DataDirectory) || !Path.IsPathFullyQualified(ExportDirectory)) throw new ArgumentException("请选择有效的绝对目录路径。");
                if (!int.TryParse(windowWidth.Text.Trim(), out var width) || width < 480 || width > 10000)
                    throw new ArgumentException("默认宽度请输入 480～10000 的整数（逻辑像素）。");
                if (!int.TryParse(windowHeight.Text.Trim(), out var height) || height < 600 || height > 10000)
                    throw new ArgumentException("默认高度请输入 600～10000 的整数（逻辑像素）。");
                _ = Path.GetFullPath(DataDirectory); _ = Path.GetFullPath(ExportDirectory); DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置无效"); }
        };
        panel.Children.Add(save);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 20, 0, 12) });
        var uninstall = new Button { Content = "卸载 LocalChat（包含记录）", Foreground = System.Windows.Media.Brushes.Firebrick, HorizontalAlignment = HorizontalAlignment.Left };
        uninstall.Click += (_, _) => { UninstallRequested = true; DialogResult = true; };
        panel.Children.Add(uninstall);
        panel.Children.Add(new TextBlock { Text = "删除当前程序、聊天记录、内部附件及配置；不会删除已导出的文件。", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = layout;
    }
    private TextBox AddFolder(StackPanel panel, string title, string value)
    {
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 6) });
        var row = new DockPanel(); var button = new Button { Content = "浏览…", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
        var input = new TextBox { Text = value }; row.Children.Add(input); panel.Children.Add(row);
        button.Click += (_, _) => { var dialog = new OpenFolderDialog { Title = title, InitialDirectory = Directory.Exists(input.Text) ? input.Text : "" }; if (dialog.ShowDialog(this) == true) input.Text = dialog.FolderName; };
        return input;
    }
}
