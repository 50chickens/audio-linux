using CommandLine;

public class CliOverrides
{
    [Option("deploy-url")] public string? DeployUrl { get; set; }
    [Option("key")] public string? ApiKey { get; set; }
    [Option("target-url")] public string? TargetUrl { get; set; }
    [Option("publish-dir")] public string? PublishDir { get; set; }
}
