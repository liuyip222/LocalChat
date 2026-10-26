using System.IO;
using LocalChat;

var root = Path.Combine(Path.GetTempPath(), "LocalChatTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    assertions++;
    Console.WriteLine("PASS: " + description);
}
try
{
    var legacySettings = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"EnterToSend\":false}")!;
    Check(legacySettings.DefaultWindowWidth == 1080 && legacySettings.DefaultWindowHeight == 800, "legacy configuration gets default startup dimensions");
    legacySettings.DefaultWindowWidth = 640;
    legacySettings.DefaultWindowHeight = 720;
    var roundtripSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(legacySettings))!;
    Check(roundtripSettings.DefaultWindowWidth == 640 && roundtripSettings.DefaultWindowHeight == 720 && !roundtripSettings.EnterToSend, "startup dimensions serialize alongside input preferences");
    var store = new ChatStore(Path.Combine(root, "data"));
    var source = Path.Combine(root, "原始资料.txt");
    await File.WriteAllTextAsync(source, "附件内容 unchanged");
    var id = await store.SendAsync("你好，合同资料\n第二行", [new(source, "合同.txt")]);
    File.Delete(source);
    var all = store.Query(new());
    Check(all.Count == 1 && all[0].Id == id, "message roundtrip");
    var a = all[0].Attachments.Single();
    var fakeProgramDirectory = Path.Combine(root, "program");
    Directory.CreateDirectory(fakeProgramDirectory);
    var fakeProgram = Path.Combine(fakeProgramDirectory, "LocalChat.exe");
    await File.WriteAllTextAsync(fakeProgram, "fixture, not an executable");
    var unrelatedFile = Path.Combine(fakeProgramDirectory, "keep.txt");
    await File.WriteAllTextAsync(unrelatedFile, "unrelated file");
    var uninstallPlan = Uninstaller.BuildPlan(new Settings { DataDirectory = store.Root, ExportDirectory = Path.Combine(root, "exports") }, fakeProgram, Path.Combine(root, "config"), Path.Combine(root, "temp"));
    Check(uninstallPlan.DataFiles.Concat(uninstallPlan.ConfigFiles).Concat(uninstallPlan.ProgramFiles).Concat(uninstallPlan.EmptyDirectories).All(path => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), "uninstall fixture never inspects real user data or caches");
    Check(uninstallPlan.DataFiles.Contains(a.FullPath) && uninstallPlan.ProgramFiles.Contains(fakeProgram), "uninstall plan includes stored attachments and exact program");
    Check(!uninstallPlan.ProgramFiles.Contains(unrelatedFile) && File.Exists(fakeProgram) && File.Exists(a.FullPath), "uninstall planning preserves unrelated files and does not execute deletion");
    Check(File.ReadAllText(a.FullPath) == "附件内容 unchanged", "original can be deleted after import");
    Check(a.Hash.Length == 64 && a.Size > 0, "hash and size");
    Check(store.Query(new("合同")).Count == 1, "Chinese substring search");
    Check(store.Query(new("合同.txt")).Count == 1, "attachment filename search");
    Check(store.Query(new("%_")).Count == 0, "search does not interpret wildcards");
    Check(store.Query(new(Kind: "image")).Count == 0, "type filtering");
    Check(store.Query(new(Kind: "file")).Count == 1, "file filter");
    Check(store.Query(new(From: DateTime.Today, Until: DateTime.Today.AddDays(1))).Count == 1, "local calendar date boundaries");
    Check(store.Query(new(From: DateTime.Today.AddDays(1))).Count == 0, "future date excluded");
    var exported = await ChatStore.ExportAsync(a, Path.Combine(root, "exports"));
    var duplicate = await ChatStore.ExportAsync(a, Path.Combine(root, "exports"));
    Check(exported != duplicate && File.ReadAllText(duplicate) == File.ReadAllText(a.FullPath), "non-overwriting exports");
    store.Edit(id, "更新后的中文内容");
    Check(store.Query(new("更新"))[0].Text == "更新后的中文内容", "edit and search index consistency");
    for (var i = 0; i < 65; i++) await store.SendAsync("消息" + i, []);
    var latest = store.Query(new());
    var older = store.Query(new(), latest[0].Id);
    Check(latest.Count == 60 && older.Count == 6 && latest.Select(m => m.Id).Intersect(older.Select(m => m.Id)).Count() == 0, "keyset pagination");
    var rejected = false;
    try { await store.SendAsync("不应存在", [new(Path.Combine(root, "missing"), "missing.txt")]); } catch (FileNotFoundException) { rejected = true; }
    Check(rejected && store.Query(new("不应存在")).Count == 0, "failed import leaves no message");
    Check(!Directory.EnumerateFiles(store.Root, "*.importing", SearchOption.AllDirectories).Any(), "failed import cleans staging files");
    await File.WriteAllTextAsync(source, "rollback fixture");
    var beforeFiles = Directory.EnumerateFiles(Path.Combine(store.Root, "attachments"), "*", SearchOption.AllDirectories).Count();
    rejected = false;
    try { await store.SendAsync("部分导入回滚", [new(source, "valid.txt"), new(Path.Combine(root, "missing"), "invalid.txt")]); } catch (FileNotFoundException) { rejected = true; }
    Check(rejected && store.Query(new("部分导入回滚")).Count == 0 && Directory.EnumerateFiles(Path.Combine(store.Root, "attachments"), "*", SearchOption.AllDirectories).Count() == beforeFiles, "later import failure removes earlier copied originals");
    rejected = false;
    try { await store.MigrateAsync(Path.Combine(store.Root, "nested")); } catch (IOException) { rejected = true; }
    Check(rejected, "reject nested migration");
    rejected = false;
    try { await store.MigrateAsync(Path.Combine(root, "exports")); } catch (IOException) { rejected = true; }
    Check(rejected, "reject nonempty migration destination");
    var migrated = await store.MigrateAsync(Path.Combine(root, "migrated"));
    var migratedAttachment = migrated.Query(new(), id + 1).Single().Attachments.Single();
    Check(migrated.Query(new(), limit: 100).Count == 66 && File.Exists(migratedAttachment.FullPath), "migration preserves records and attachments");
    Check(File.Exists(a.FullPath), "migration preserves old data");
    migrated.Delete(id);
    Check(migrated.Query(new(), id + 1).Count == 0 && !File.Exists(migratedAttachment.FullPath), "delete removes message and unreferenced original");
    Check(File.Exists(exported), "delete keeps exported copy");
    rejected = false;
    try { store.Resolve("../escape"); } catch (IOException) { rejected = true; }
    Check(rejected, "attachment path containment");
    Console.WriteLine($"SUCCESS: {assertions} assertions; temporary fixture: {root}");
}
finally
{
    // Test fixture only; never delete user-selected data directories.
    if (Path.GetFileName(root).StartsWith("LocalChatTests-") && Path.GetDirectoryName(root) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)) Directory.Delete(root, true);
}
