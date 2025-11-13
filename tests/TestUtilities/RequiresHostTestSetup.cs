using System;
using NUnit.Framework;
using Docker.DotNet;

// Global SetUpFixture to ensure DOCKER_HOST is set for test processes that may run RequiresHost tests.
// This is linked into test assemblies that contain RequiresHost tests so it executes before any Testcontainers usage.
[SetUpFixture]
public class RequiresHostTestSetup
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var prev = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(prev))
        {
            TestContext.WriteLine($"[RequiresHostTestSetup] DOCKER_HOST already set: {prev}");
            return;
        }

        string chosen = null;
        var candidates = new[] {
            "npipe://./pipe/dockerDesktopLinuxEngine",
            "npipe://./pipe/docker_engine",
            "tcp://localhost:2375",
            "unix:///var/run/docker.sock"
        };

        foreach (var c in candidates)
        {
            try
            {
                var cfg = new Docker.DotNet.DockerClientConfiguration(new Uri(c));
                using var client = cfg.CreateClient();
                var ping = client.System.PingAsync();
                if (ping.Wait(TimeSpan.FromSeconds(3)))
                {
                    chosen = c;
                    break;
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", chosen, EnvironmentVariableTarget.Process);
            TestContext.WriteLine($"[RequiresHostTestSetup] Set DOCKER_HOST={chosen} for process");
        }
        else
        {
            TestContext.WriteLine("[RequiresHostTestSetup] Could not determine a Docker endpoint; leaving DOCKER_HOST unset");
        }
    }
}
