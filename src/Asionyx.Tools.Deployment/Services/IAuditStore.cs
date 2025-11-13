using System.Collections.Generic;
using System.Threading.Tasks;

namespace Asionyx.Service.Deployment.Linux.Services;

public record AuditEntry(string Id, string Action, string Status, string? Details, System.DateTime Timestamp);

public interface IAuditStore
{
    Task AppendAsync(AuditEntry entry);
    Task<IReadOnlyList<AuditEntry>> QueryAsync();
}
