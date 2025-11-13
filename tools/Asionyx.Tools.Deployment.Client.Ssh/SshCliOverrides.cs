using CommandLine;

public class SshCliOverrides
{
    [Option("ssh-host")] public string? Host { get; set; }
    [Option("ssh-port")] public int? Port { get; set; }
    [Option("ssh-user")] public string? User { get; set; }
    [Option("ssh-key")] public string? KeyPath { get; set; }
}
