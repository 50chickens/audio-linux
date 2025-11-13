namespace Asionyx.Service.Deployment.Linux.Models
{
    public class ServiceSettings
    {
        // Data folder path; can contain '~' which will be expanded at runtime to the current user's home directory
        public string DataFolder { get; set; } = "~/.Asionyx.Service.Deployment.Linux";

        // Api key used for authenticated endpoints. Stored in Asionyx.Service.Deployment.Linux.json in the DataFolder.
        public string? ApiKey { get; set; }
    }
}
