using Microsoft.AspNetCore.Mvc;
using Asionyx.Tools.Deployment.Services;
using System.IO.Compression;

namespace Asionyx.Tools.Deployment.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IAuditStore _audit;

    public FilesController(IAuditStore audit)
    {
        _audit = audit;
    }

    public record UploadMetadata(string TargetDir, string FileName);

    // Accept multipart/form-data uploads. One form field 'metadata' should contain a JSON blob
    // with TargetDir and FileName. The file part should be sent as a file (e.g., archive.zip).
    [HttpPost("upload")]
    public async Task<IActionResult> UploadMultipart()
    {
        if (!Request.HasFormContentType)
            return BadRequest("Content type must be multipart/form-data");

        var form = await Request.ReadFormAsync();

        var metadataJson = form["metadata"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(metadataJson))
            return BadRequest("Missing 'metadata' form field (JSON)");

        UploadMetadata? meta;
        try
        {
            meta = System.Text.Json.JsonSerializer.Deserialize<UploadMetadata>(metadataJson);
        }
        catch (Exception ex)
        {
            return BadRequest($"Invalid metadata JSON: {ex.Message}");
        }

        if (meta == null || string.IsNullOrWhiteSpace(meta.TargetDir) || string.IsNullOrWhiteSpace(meta.FileName))
            return BadRequest("Metadata must contain TargetDir and FileName");

        if (form.Files.Count == 0)
            return BadRequest("No file uploaded");

        var file = form.Files[0];
        Directory.CreateDirectory(meta.TargetDir);
        var filePath = Path.Combine(meta.TargetDir, Path.GetFileName(meta.FileName));

        try
        {
            using var stream = System.IO.File.Create(filePath);
            await file.CopyToAsync(stream);

            if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ZipFile.ExtractToDirectory(filePath, meta.TargetDir, true);
                }
                catch (Exception ex)
                {
                    await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "ExtractZip", "Failed", ex.Message, DateTime.UtcNow));
                    return StatusCode(500, new { Error = "Failed to extract zip", Details = ex.Message });
                }
            }

            await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "UploadFiles", "Success", filePath, DateTime.UtcNow));
            return Ok(new { Saved = filePath });
        }
        catch (Exception ex)
        {
            await _audit.AppendAsync(new AuditEntry(Guid.NewGuid().ToString(), "UploadFiles", "Failed", ex.Message, DateTime.UtcNow));
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}
