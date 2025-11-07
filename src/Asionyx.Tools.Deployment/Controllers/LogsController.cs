using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Asionyx.Tools.Deployment.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly ILogger<LogsController> _logger;
    private readonly string _logPath;

    public LogsController(ILogger<LogsController> logger, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
    {
        _logger = logger;
        _logPath = System.IO.Path.Combine(env.ContentRootPath ?? System.AppContext.BaseDirectory, "logs", "deployment.log");
    }

    // Simple API to query the JSON log file.
    // filter: semicolon-separated expressions like "Level==error;payload.Message contains upload"
    // sort: field:asc|desc (e.g. datestamp:desc)
    // limit: max number of results
    [HttpGet]
    public IActionResult Query([FromQuery] string? filter, [FromQuery] string? sort, [FromQuery] int? limit)
    {
    if (!System.IO.File.Exists(_logPath)) return NotFound(new { Error = "Log file not found", Path = _logPath });

        IEnumerable<string> lines;
        try
        {
            lines = System.IO.File.ReadLines(_logPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read log file");
            return StatusCode(500, new { Error = ex.Message });
        }

        var jsons = new List<JsonElement>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                jsons.Add(doc.RootElement.Clone());
            }
            catch
            {
                // ignore unparsable lines
            }
        }

        // apply filters (AND semantics)
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var clauses = filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var clause in clauses)
            {
                jsons = jsons.Where(j => EvaluateClause(j, clause)).ToList();
            }
        }

        // sorting
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var parts = sort.Split(':', 2);
            var field = parts[0];
            var dir = parts.Length > 1 ? parts[1] : "asc";
            jsons = dir.Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? jsons.OrderByDescending(j => GetSortKey(j, field)).ToList()
                : jsons.OrderBy(j => GetSortKey(j, field)).ToList();
        }

        if (limit.HasValue) jsons = jsons.Take(limit.Value).ToList();

        return Ok(jsons);
    }

    private static string? GetSortKey(JsonElement j, string field)
    {
        var v = GetJsonValueByPath(j, field);
        if (v == null) return null;
        if (v.Value.ValueKind == JsonValueKind.String) return v.Value.GetString();
        return v.Value.ToString();
    }

    private static JsonElement? GetJsonValueByPath(JsonElement root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonElement current = root;
        foreach (var p in parts)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(p, out var next)) return null;
            current = next;
        }
        return current;
    }

    private static bool EvaluateClause(JsonElement j, string clause)
    {
        // Supported forms: field==value, field!=value, field contains value
        // Also supports OR inside a clause (using ' OR ') and timestamp comparisons like datestamp>=2025-11-07 10:00:00
        var trimmed = clause.Trim();

        // OR support: split by ' OR ' (case-insensitive)
        var orParts = trimmed.Split(new[] { " OR ", " or " }, StringSplitOptions.RemoveEmptyEntries);
        if (orParts.Length > 1)
        {
            foreach (var p in orParts)
            {
                if (EvaluateClause(j, p)) return true;
            }
            return false;
        }
        if (trimmed.Contains("=="))
        {
            var parts = trimmed.Split(new[] { "==" }, 2, StringSplitOptions.None);
            var left = parts[0].Trim(); var right = parts[1].Trim().Trim('\'', '"');
            var v = GetJsonValueByPath(j, left);
            return v != null && v.Value.ValueKind == JsonValueKind.String && string.Equals(v.Value.GetString(), right, StringComparison.OrdinalIgnoreCase);
        }
        if (trimmed.Contains("!="))
        {
            var parts = trimmed.Split(new[] { "!=" }, 2, StringSplitOptions.None);
            var left = parts[0].Trim(); var right = parts[1].Trim().Trim('\'', '"');
            var v = GetJsonValueByPath(j, left);
            return !(v != null && v.Value.ValueKind == JsonValueKind.String && string.Equals(v.Value.GetString(), right, StringComparison.OrdinalIgnoreCase));
        }
        // timestamp comparisons
        if (trimmed.Contains(">=") || trimmed.Contains("<=") || trimmed.Contains(">") || trimmed.Contains("<"))
        {
            // support forms like datestamp>=2025-11-07 10:00:00
            string op = null;
            if (trimmed.Contains(">=")) op = ">=";
            else if (trimmed.Contains("<=")) op = "<=";
            else if (trimmed.Contains(">")) op = ">";
            else if (trimmed.Contains("<")) op = "<";
            if (op != null)
            {
                var parts = trimmed.Split(new[] { op }, 2, StringSplitOptions.None);
                var left = parts[0].Trim(); var right = parts[1].Trim().Trim('\'', '"');
                var v = GetJsonValueByPath(j, left);
                if (v == null || v.Value.ValueKind != JsonValueKind.String) return false;
                if (!DateTime.TryParse(v.Value.GetString(), out var actual)) return false;
                if (!DateTime.TryParse(right, out var expected)) return false;
                return op switch
                {
                    ">=" => actual >= expected,
                    "<=" => actual <= expected,
                    ">" => actual > expected,
                    "<" => actual < expected,
                    _ => false
                };
            }
        }
        if (trimmed.Contains(" contains ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(new[] { " contains " }, 2, StringSplitOptions.None);
            var left = parts[0].Trim(); var right = parts[1].Trim().Trim('\'', '"');
            var v = GetJsonValueByPath(j, left);
            return v != null && v.Value.ValueKind == JsonValueKind.String && v.Value.GetString()?.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // unknown clause -> default false
        return false;
    }
}
