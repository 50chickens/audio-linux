public class SshOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string User { get; set; } = string.Empty;
    public string KeyPath { get; set; } = string.Empty;
    public string PublishDir { get; set; } = string.Empty;
}
