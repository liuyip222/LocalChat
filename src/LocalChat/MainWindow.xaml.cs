using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LocalChat;

public partial class MainWindow : Window
{
    private readonly Settings settings;
    private ChatStore store;
    private readonly ObservableCollection<Message> messages = [];
    private readonly ObservableCollection<Message> results = [];
    private readonly ObservableCollection<PendingFile> pending = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool busy;
    private bool uninstalling;
    private bool searchOpen;
    private bool hasOlder;
    private SearchFilter activeFilter = new();
    private long? resultsBefore;

    public MainWindow(Settings settings, ChatStore store)
    {
        this.settings = settings;
        this.store = store;
        InitializeComponent();
        ApplyStartupSize();
        SourceInitialized += (_, _) => WindowAppearance.MatchCaptionToBackground(this);
        Activated += (_, _) => WindowAppearance.MatchCaptionToBackground(this);
        MessagesList.ItemsSource = messages;
        PendingList.ItemsSource = pending;
        SearchResults.ItemsSource = results;
        DataObject.AddPastingHandler(Composer, OnPaste);
        UpdateHint();
        Loaded += async (_, _) => await RunAsync(async () => { await LoadLatestAsync(); Composer.Focus(); });
    }
    private void ApplyStartupSize()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(480, area.Width);
        MinHeight = Math.Min(600, area.Height);
        var width = settings.DefaultWindowWidth is >= 480 and <= 10000 ? settings.DefaultWindowWidth : 1080;
        var height = settings.DefaultWindowHeight is >= 600 and <= 10000 ? settings.DefaultWindowHeight : 800;
        Width = Math.Clamp(width, MinWidth, area.Width);
        Height = Math.Clamp(height, MinHeight, area.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
    private async Task RunAsync(Func<Task> operation)
    {
        if (!await gate.WaitAsync(0)) { Status.Text = "上一项操作尚未完成，请稍候。"; return; }
        busy = true;
        UpdateSendState();
        AddFileButton.IsEnabled = false;
        Composer.IsReadOnly = true;
        try { await operation(); }
        catch (Exception ex) { Status.Text = "操作失败：" + ex.Message; MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { busy = false; Composer.IsReadOnly = false; AddFileButton.IsEnabled = true; UpdateSendState(); gate.Release(); }
    }
    private async Task<List<Message>> LoadAsync(SearchFilter filter, long? before = null)
    {
        var currentStore = store;
        return await Task.Run(() =>
        {
            var list = currentStore.Query(filter, before);
            foreach (var m in list)
                foreach (var a in m.Attachments)
                    if (a.Kind == "image") a.Thumbnail = TryThumbnail(a, currentStore.Root);
            return list;
        });
    }
    private static BitmapSource? TryThumbnail(Attachment attachment, string root)
    {
        try
        {
            var cache = Path.Combine(root, "cache", attachment.Id + ".png");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 520;
            bitmap.UriSource = new Uri(File.Exists(cache) ? cache : attachment.FullPath);
            bitmap.EndInit();
            bitmap.Freeze();
            if (!File.Exists(cache))
            {
                try
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(cache);
                    encoder.Save(stream);
                }
                catch (IOException) { }
            }
            return bitmap;
        }
        catch { return null; } // Unsupported codecs remain downloadable file cards.
    }
    private async Task LoadLatestAsync()
    {
        var list = await LoadAsync(new());
        messages.Clear();
        foreach (var m in list) messages.Add(m);
        hasOlder = list.Count == ChatStore.PageSize;
        RefreshDates();
        if (messages.Count > 0) MessagesList.ScrollIntoView(messages[^1]);
        Status.Text = "";
        ReturnLatestButton.Visibility = Visibility.Collapsed;
    }
    private void RefreshDates()
    {
        DateTime? previous = null;
        foreach (var m in messages)
        {
            var day = m.CreatedUtc.ToLocalTime().Date;
            m.DateVisibility = previous == day ? Visibility.Collapsed : Visibility.Visible;
            previous = day;
        }
        MessagesList.Items.Refresh();
        LoadOlderButton.Visibility = hasOlder ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void OnLatest(object sender, RoutedEventArgs e) => await RunAsync(LoadLatestAsync);
    private async void OnLoadOlder(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (messages.Count == 0) return;
        var anchor = messages[0];
        var older = await LoadAsync(new(), anchor.Id);
        for (var i = older.Count - 1; i >= 0; i--) messages.Insert(0, older[i]);
        hasOlder = older.Count == ChatStore.PageSize;
        RefreshDates();
        MessagesList.ScrollIntoView(anchor);
        Status.Text = "";
    });
    private void AddPending(IEnumerable<string> paths)
    {
        if (busy) return;
        foreach (var path in paths)
            if (File.Exists(path) && !pending.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) pending.Add(new(path, Path.GetFileName(path)));
        UpdatePending();
    }
    private void UpdatePending() { PendingList.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible; UpdateSendState(); }
    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "选择发送给自己的文件", Filter = "所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) AddPending(dialog.FileNames);
    }
    private void OnRemovePending(object sender, RoutedEventArgs e)
    {
        if (busy || ((FrameworkElement)sender).DataContext is not PendingFile file) return;
        pending.Remove(file);
        DeleteTemp(file);
        UpdatePending();
    }
    private void OnDragOver(object sender, DragEventArgs e) { e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void OnDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPending(paths); e.Handled = true; }
    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (busy) { e.CancelCommand(); return; }
        try
        {
            if (e.SourceDataObject.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.SourceDataObject.GetData(DataFormats.FileDrop) is string[] files) AddPending(files);
                e.CancelCommand();
            }
            else if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image == null) return;
                var dir = Path.Combine(Path.GetTempPath(), "LocalChat", "clipboard");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(path)) encoder.Save(file);
                pending.Add(new(path, $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png", true));
                UpdatePending();
                e.CancelCommand();
            }
        }
        catch (Exception ex) { e.CancelCommand(); Status.Text = "粘贴失败：" + ex.Message; }
    }
    private void OnComposerChanged(object sender, TextChangedEventArgs e) => UpdateSendState();
    private void UpdateSendState() { if (SendButton != null) SendButton.IsEnabled = !busy && (!string.IsNullOrWhiteSpace(Composer.Text) || pending.Count > 0); }
    private void UpdateHint(bool? previewEnterToSend = null)
    {
        var enterToSend = previewEnterToSend ?? settings.EnterToSend;
        KeyboardHint.Text = enterToSend ? "Enter 发送 · Shift+Enter 换行" : "Ctrl+Enter 发送 · Enter 换行";
        SendButton.ToolTip = enterToSend ? "发送（Enter）" : "发送（Ctrl+Enter）";
    }
    private async void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (settings.EnterToSend ? Keyboard.Modifiers == ModifierKeys.None : Keyboard.Modifiers == ModifierKeys.Control))
        { e.Handled = true; if (SendButton.IsEnabled) await SendAsync(); }
    }
    private async void OnSend(object sender, RoutedEventArgs e) => await SendAsync();
    private Task SendAsync() => RunAsync(async () =>
    {
        var files = pending.ToList();
        var progress = new Progress<string>(message => Status.Text = message);
        await store.SendAsync(Composer.Text, files, progress);
        Composer.Clear();
        pending.Clear();
        foreach (var file in files) DeleteTemp(file);
        UpdatePending();
        await LoadLatestAsync();
        InvalidateSearch();
        Status.Text = "";
        Composer.Focus();
    });
    private static void DeleteTemp(PendingFile file) { if (file.Temporary) { try { File.Delete(file.Path); } catch (IOException) { } } }
    private static Attachment? GetAttachment(object sender) => ((FrameworkElement)sender).DataContext as Attachment;
    private void ShellOpen(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private async void OnOpenAttachment(object sender, RoutedEventArgs e) => await OpenAttachmentAsync(GetAttachment(sender));
    private async void OnPreview(object sender, MouseButtonEventArgs e) => await OpenAttachmentAsync(GetAttachment(sender));
    private Task OpenAttachmentAsync(Attachment? a) => RunAsync(() =>
    {
        if (a == null) return Task.CompletedTask;
        if (!File.Exists(a.FullPath)) throw new FileNotFoundException("附件原件不存在，可能已被外部移动或删除。", a.FullPath);
        if (a.Kind == "image" && a.Thumbnail != null)
        {
            try
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(a.FullPath); bitmap.EndInit();
                new Window { Owner = this, Title = a.OriginalName, Width = 900, Height = 700, Background = Brushes.DarkSlateGray, Content = new ScrollViewer { Content = new Image { Source = bitmap, Stretch = Stretch.Uniform }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
            }
            catch { ShellOpen(a.FullPath); }
        }
        else ShellOpen(a.FullPath);
        return Task.CompletedTask;
    });
    private async void OnSaveAttachment(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (GetAttachment(sender) is not { } a) return;
        Status.Text = "正在保存附件…";
        var path = await ChatStore.ExportAsync(a, settings.ExportDirectory);
        Status.Text = "已保存：" + path;
    });
    private async void OnSaveAsAttachment(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (GetAttachment(sender) is not { } a) return;
        var dialog = new SaveFileDialog { FileName = a.OriginalName, Filter = "所有文件|*.*", InitialDirectory = Directory.Exists(settings.ExportDirectory) ? settings.ExportDirectory : "" };
        if (dialog.ShowDialog(this) != true) return;
        if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(a.FullPath), StringComparison.OrdinalIgnoreCase)) throw new IOException("不能覆盖内部附件原件，请选择其他位置。");
        // Copy to a sibling temporary file so an interrupted export does not truncate an existing file.
        var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(a.FullPath))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true)) await input.CopyToAsync(output);
            File.Move(temp, dialog.FileName, true);
            Status.Text = "已另存为：" + dialog.FileName;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    });
    private async void OnLocateAttachment(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        if (GetAttachment(sender) is { } a) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{a.FullPath}\"") { UseShellExecute = true });
        return Task.CompletedTask;
    });
    private async void OnCopyMessage(object sender, RoutedEventArgs e) => await RunAsync(() => { if (((FrameworkElement)sender).DataContext is Message m && m.Text.Length > 0) { Clipboard.SetText(m.Text); Status.Text = "文字已复制"; } return Task.CompletedTask; });
    private async void OnDeleteMessage(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Message m || busy) return;
        await RunAsync(async () => { await Task.Run(() => store.Delete(m.Id)); await LoadLatestAsync(); InvalidateSearch(); });
    }
    private async void OnEditMessage(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Message m || busy) return;
        var text = new TextBox { Text = m.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(15), MinHeight = 180 };
        var save = new Button { Content = "保存修改", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(15) };
        var panel = new DockPanel(); DockPanel.SetDock(save, Dock.Bottom); panel.Children.Add(save); panel.Children.Add(text);
        var dialog = new Window { Owner = this, Title = "编辑消息", Width = 540, Height = 360, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(text.Text) && m.Attachments.Count == 0) { MessageBox.Show(dialog, "消息不能为空。"); return; } dialog.DialogResult = true; };
        if (dialog.ShowDialog() == true) await RunAsync(async () => { store.Edit(m.Id, text.Text); await LoadLatestAsync(); InvalidateSearch(); });
    }
    private void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        searchOpen = !searchOpen;
        UpdateSearchLayout();
        if (searchOpen) SearchText.Focus();
        else Composer.Focus();
    }
    private void OnCloseSearch(object sender, RoutedEventArgs e)
    {
        searchOpen = false;
        UpdateSearchLayout();
        Composer.Focus();
    }
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSearchLayout();
    private void UpdateSearchLayout()
    {
        if (SearchPanel == null || SearchColumn == null) return;
        // Narrow windows use an overlay instead of squeezing the chat or forcing a wider window.
        var overlay = ActualWidth < 800;
        SearchPanel.Visibility = searchOpen ? Visibility.Visible : Visibility.Collapsed;
        SearchColumn.Width = new GridLength(searchOpen && !overlay ? 300 : 0);
        Grid.SetColumn(SearchPanel, overlay ? 0 : 1);
        SearchPanel.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        SearchPanel.Width = overlay ? 300 : double.NaN;
    }
    private async void OnSearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(false); } }
    private async void OnSearch(object sender, RoutedEventArgs e) => await SearchAsync(false);
    private async void OnMoreResults(object sender, RoutedEventArgs e) => await SearchAsync(true);
    private Task SearchAsync(bool more) => RunAsync(async () =>
    {
        if (!more)
        {
            if (FromDate.SelectedDate > ToDate.SelectedDate) throw new ArgumentException("开始日期不能晚于结束日期。");
            activeFilter = new(SearchText.Text, FromDate.SelectedDate, ToDate.SelectedDate?.AddDays(1), (string)((ComboBoxItem)KindFilter.SelectedItem).Tag);
            resultsBefore = null; results.Clear();
        }
        var list = await Task.Run(() => store.Query(activeFilter, resultsBefore));
        resultsBefore = list.Count == 0 ? resultsBefore : list[0].Id;
        foreach (var m in list.AsEnumerable().Reverse()) results.Add(m);
        MoreResultsButton.Visibility = list.Count == ChatStore.PageSize ? Visibility.Visible : Visibility.Collapsed;
        SearchCount.Text = results.Count == 0 ? "没有找到匹配的记录" : $"已找到 {results.Count} 条 · 点击跳转";
    });
    private void OnResetSearch(object sender, RoutedEventArgs e) { SearchText.Clear(); FromDate.SelectedDate = null; ToDate.SelectedDate = null; KindFilter.SelectedIndex = 0; results.Clear(); SearchCount.Text = ""; MoreResultsButton.Visibility = Visibility.Collapsed; }
    private void InvalidateSearch() { results.Clear(); resultsBefore = null; MoreResultsButton.Visibility = Visibility.Collapsed; SearchCount.Text = "记录已变更，请重新搜索。"; }
    private async void OnSearchSelection(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is not Message target) return;
        await RunAsync(async () =>
        {
            var list = await LoadAsync(new(), target.Id + 1);
            messages.Clear(); foreach (var m in list) messages.Add(m);
            hasOlder = list.Count == ChatStore.PageSize; RefreshDates();
            var match = messages.FirstOrDefault(m => m.Id == target.Id);
            if (match != null) { MessagesList.SelectedItem = match; MessagesList.ScrollIntoView(match); }
            ReturnLatestButton.Visibility = Visibility.Visible;
            Status.Text = "";
        });
    }
    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new SettingsWindow(settings) { Owner = this };
        void PreviewInputMode(bool enterToSend) => UpdateHint(enterToSend);
        dialog.EnterToSendChanged += PreviewInputMode;
        try
        {
            if (dialog.ShowDialog() != true) return;
            if (dialog.UninstallRequested)
            {
                await UninstallAsync();
                return;
            }
            await RunAsync(async () =>
            {
                var nextStore = store;
                if (!Path.GetFullPath(dialog.DataDirectory).TrimEnd('\\').Equals(store.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    Status.Text = "正在迁移数据，请勿关闭窗口…";
                    nextStore = await store.MigrateAsync(dialog.DataDirectory);
                }
                var nextSettings = new Settings
                {
                    DataDirectory = nextStore.Root,
                    ExportDirectory = dialog.ExportDirectory,
                    EnterToSend = dialog.EnterToSend,
                    DefaultWindowWidth = dialog.DefaultWindowWidth,
                    DefaultWindowHeight = dialog.DefaultWindowHeight,
                    PreviousDataDirectories = (settings.PreviousDataDirectories ?? []).Concat(
                        ReferenceEquals(nextStore, store) ? [] : new[] { store.Root }).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
                Directory.CreateDirectory(nextSettings.ExportDirectory);
                nextSettings.Save();
                settings.DataDirectory = nextSettings.DataDirectory; settings.ExportDirectory = nextSettings.ExportDirectory; settings.EnterToSend = nextSettings.EnterToSend;
                settings.DefaultWindowWidth = nextSettings.DefaultWindowWidth;
                settings.DefaultWindowHeight = nextSettings.DefaultWindowHeight;
                settings.PreviousDataDirectories = nextSettings.PreviousDataDirectories;
                store = nextStore; UpdateHint(); await LoadLatestAsync(); Status.Text = "设置已保存。数据迁移后原目录仍保留，可自行确认后清理。";
            });
        }
        finally
        {
            dialog.EnterToSendChanged -= PreviewInputMode;
            // Restore the persisted mode on cancel/save failure; keep the new one after a successful save.
            UpdateHint();
        }
    }
    private async Task UninstallAsync()
    {
        if (MessageBox.Show(this,
            "确定彻底卸载 LocalChat？\n\n将退出应用并永久删除：\n• 当前程序文件\n• 当前及已登记历史目录中的聊天记录、内部附件和缓存\n• 应用设置和未发送的内容\n\n已保存/另存为导出的文件、开发源码不受影响。此操作不可撤销。",
            "卸载 LocalChat", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            Status.Text = "正在准备卸载…";
            var executable = Environment.ProcessPath ?? throw new IOException("无法识别当前程序路径。");
            var plan = await Task.Run(() => Uninstaller.BuildPlan(settings, executable));
            await Uninstaller.StartAsync(plan);
            uninstalling = true;
            Application.Current.Shutdown();
        });
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (uninstalling) { foreach (var file in pending) DeleteTemp(file); return; }
        if (busy) { e.Cancel = true; Status.Text = "正在处理，请完成后再关闭。"; return; }
        if ((!string.IsNullOrWhiteSpace(Composer.Text) || pending.Count > 0) && MessageBox.Show(this, "还有未发送的内容，确认退出并丢弃？", "退出 LocalChat", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        foreach (var file in pending) DeleteTemp(file);
    }
}

