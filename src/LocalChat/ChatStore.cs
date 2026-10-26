using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace LocalChat;

public sealed class ChatStore
{
    public const int PageSize = 60;
    public string Root { get; }
    public ChatStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "attachments"));
        Directory.CreateDirectory(Path.Combine(Root, "cache"));
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT NOT NULL,
                created_at TEXT NOT NULL, updated_at TEXT);
            CREATE INDEX IF NOT EXISTS idx_messages_time ON messages(created_at, id);
            CREATE TABLE IF NOT EXISTS attachments (
                id TEXT PRIMARY KEY, relative_path TEXT NOT NULL UNIQUE,
                size_bytes INTEGER NOT NULL, kind TEXT NOT NULL, sha256 TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS message_attachments (
                message_id INTEGER NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
                attachment_id TEXT NOT NULL REFERENCES attachments(id),
                original_name TEXT NOT NULL, sort_order INTEGER NOT NULL,
                PRIMARY KEY(message_id, attachment_id));
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "localchat.db"), ForeignKeys = true, Pooling = false }.ToString());
        db.Open();
        return db;
    }
    public string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("附件路径超出数据目录。");
        return full;
    }
    public async Task<long> SendAsync(string text, IReadOnlyList<PendingFile> files, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(text) && files.Count == 0) throw new ArgumentException("消息不能为空。");
        var imported = new List<Attachment>();
        var stagedPaths = new List<string>();
        var now = DateTime.UtcNow;
        try
        {
            foreach (var file in files)
            {
                var id = Guid.NewGuid().ToString("N");
                var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                var relative = Path.Combine("attachments", now.ToString("yyyy/MM", CultureInfo.InvariantCulture), id + ext);
                var destination = Resolve(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temp = destination + ".importing";
                stagedPaths.Add(temp);
                stagedPaths.Add(destination);
                progress?.Report("正在导入：" + file.Name);
                long copied = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                {
                    var buffer = new byte[131072];
                    var lastReport = Environment.TickCount64;
                    int count;
                    while ((count = await input.ReadAsync(buffer)) != 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count));
                        hash.AppendData(buffer, 0, count);
                        copied += count;
                        if (Environment.TickCount64 - lastReport > 200)
                        {
                            progress?.Report($"正在导入：{file.Name} · {Attachment.FormatSize(copied)} / {Attachment.FormatSize(input.Length)}");
                            lastReport = Environment.TickCount64;
                        }
                    }
                    await output.FlushAsync();
                    output.Flush(true);
                }
                File.Move(temp, destination);
                imported.Add(new Attachment { Id = id, RelativePath = relative, FullPath = destination, OriginalName = file.Name, Size = copied, Kind = DetectKind(ext), Hash = Convert.ToHexString(hash.GetHashAndReset()) });
            }
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO messages(text,created_at) VALUES($text,$time); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$time", now.ToString("O"));
            var messageId = (long)cmd.ExecuteScalar()!;
            for (var i = 0; i < imported.Count; i++)
            {
                var a = imported[i];
                using var insert = db.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO attachments VALUES($id,$path,$size,$kind,$hash);
                    INSERT INTO message_attachments VALUES($message,$id,$name,$order);
                    """;
                insert.Parameters.AddWithValue("$id", a.Id);
                insert.Parameters.AddWithValue("$path", a.RelativePath);
                insert.Parameters.AddWithValue("$size", a.Size);
                insert.Parameters.AddWithValue("$kind", a.Kind);
                insert.Parameters.AddWithValue("$hash", a.Hash);
                insert.Parameters.AddWithValue("$message", messageId);
                insert.Parameters.AddWithValue("$name", a.OriginalName);
                insert.Parameters.AddWithValue("$order", i);
                insert.ExecuteNonQuery();
            }
            tx.Commit();
            return messageId;
        }
        catch
        {
            foreach (var path in stagedPaths) { try { File.Delete(path); } catch (IOException) { } }
            throw;
        }
    }
    public static string DetectKind(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".heic" or ".avif" => "image",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" or ".m4v" or ".mpeg" or ".mpg" => "video",
        _ => "file"
    };
    public List<Message> Query(SearchFilter filter, long? beforeId = null, int limit = PageSize)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT m.id,m.text,m.created_at FROM messages m
            WHERE ($before IS NULL OR m.id < $before)
            AND ($from IS NULL OR m.created_at >= $from)
            AND ($until IS NULL OR m.created_at < $until)
            AND ($keyword = '' OR instr(lower(m.text),lower($keyword)) > 0 OR EXISTS (
                SELECT 1 FROM message_attachments ma WHERE ma.message_id=m.id AND instr(lower(ma.original_name),lower($keyword))>0))
            AND ($kind='all' OR ($kind='text' AND m.text != '') OR EXISTS (
                SELECT 1 FROM message_attachments ma JOIN attachments a ON a.id=ma.attachment_id
                WHERE ma.message_id=m.id AND a.kind=$kind))
            ORDER BY m.id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$before", (object?)beforeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$from", (object?)filter.From?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$until", (object?)filter.Until?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$keyword", filter.Keyword.Trim());
        cmd.Parameters.AddWithValue("$kind", filter.Kind);
        cmd.Parameters.AddWithValue("$limit", limit);
        var results = new List<Message>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) results.Add(new Message { Id = reader.GetInt64(0), Text = reader.GetString(1), CreatedUtc = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
        foreach (var message in results)
        {
            using var attachments = db.CreateCommand();
            attachments.CommandText = "SELECT a.id,a.relative_path,a.size_bytes,a.kind,a.sha256,ma.original_name FROM attachments a JOIN message_attachments ma ON a.id=ma.attachment_id WHERE ma.message_id=$id ORDER BY ma.sort_order";
            attachments.Parameters.AddWithValue("$id", message.Id);
            using var reader = attachments.ExecuteReader();
            while (reader.Read()) message.Attachments.Add(new Attachment { Id = reader.GetString(0), RelativePath = reader.GetString(1), Size = reader.GetInt64(2), Kind = reader.GetString(3), Hash = reader.GetString(4), OriginalName = reader.GetString(5), FullPath = Resolve(reader.GetString(1)) });
        }
        results.Reverse();
        return results;
    }
    public void Edit(long id, string text)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE messages SET text=$text,updated_at=$time WHERE id=$id";
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
    public void Delete(long id)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        var paths = new List<string>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM messages WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT relative_path FROM attachments WHERE NOT EXISTS(SELECT 1 FROM message_attachments ma WHERE ma.attachment_id=attachments.id)";
            using (var reader = cmd.ExecuteReader()) while (reader.Read()) paths.Add(Resolve(reader.GetString(0)));
            cmd.CommandText = "DELETE FROM attachments WHERE NOT EXISTS(SELECT 1 FROM message_attachments ma WHERE ma.attachment_id=attachments.id)";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        foreach (var path in paths) { try { File.Delete(path); } catch (IOException) { } }
    }
    public static async Task<string> ExportAsync(Attachment attachment, string directory)
    {
        Directory.CreateDirectory(directory);
        var name = Path.GetFileName(attachment.OriginalName);
        for (var i = 0; ; i++)
        {
            var dest = Path.Combine(directory, i == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
            FileStream output;
            try { output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true); }
            catch (IOException) when (File.Exists(dest)) { continue; }
            try
            {
                await using (output)
                await using (var input = File.OpenRead(attachment.FullPath)) await input.CopyToAsync(output);
                return dest;
            }
            catch { File.Delete(dest); throw; }
        }
    }
    public async Task<ChatStore> MigrateAsync(string destination)
    {
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var source = Root.TrimEnd(Path.DirectorySeparatorChar);
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)) return this;
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("新旧数据目录不能互相包含。");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("请选择一个空文件夹，避免覆盖已有数据。");
        Directory.CreateDirectory(destination);
        using (var db = Open())
        using (var cmd = db.CreateCommand()) { cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; cmd.ExecuteNonQuery(); }
        foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (path.EndsWith("-wal") || path.EndsWith("-shm") || path.EndsWith(".importing")) continue;
            var target = Path.Combine(destination, Path.GetRelativePath(Root, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = File.OpenRead(path);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await input.CopyToAsync(output);
        }
        // Keep the original directory intact until the user independently removes it.
        return new ChatStore(destination);
    }
}
