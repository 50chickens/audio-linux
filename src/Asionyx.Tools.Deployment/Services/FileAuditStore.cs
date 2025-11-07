using System.Collections.Concurrent;
using System.Text.Json;

namespace Asionyx.Tools.Deployment.Services;

public class FileAuditStore : IAuditStore
{
    private readonly string _path;
    private readonly ConcurrentQueue<AuditEntry> _queue = new();

    public FileAuditStore()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.jsonl");
    }

    public Task AppendAsync(AuditEntry entry)
    {
        var line = JsonSerializer.Serialize(entry);
        // append line
        File.AppendAllText(_path, line + "\n");
        _queue.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync()
    {
        var list = new List<AuditEntry>();
        if (File.Exists(_path))
        {
            foreach (var l in File.ReadAllLines(_path))
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<AuditEntry>(l);
                    if (e != null) list.Add(e);
                }
                catch { }
            }
        }
        return Task.FromResult((IReadOnlyList<AuditEntry>)list);
    }
}
