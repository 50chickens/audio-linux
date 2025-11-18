using System;
using NUnit.Framework;

// Fail any test in this assembly that runs longer than 5 minutes (300000ms).
// This prevents hung container tests from blocking CI indefinitely.
[assembly: Timeout(300000)]

// Global test setup for Testcontainers-based integration tests in this assembly.
[SetUpFixture]
public class TestSetup
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Ensure Testcontainers and Docker.DotNet read the desired Docker endpoint.
        // This must run before any Testcontainers usage in the process.
        const string dockerHost = "tcp://localhost:2375";
        var prev = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
        if (string.IsNullOrEmpty(prev))
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", dockerHost, EnvironmentVariableTarget.Process);
            TestContext.WriteLine($"[TestSetup] Set DOCKER_HOST={dockerHost} for process");
        }
        else
        {
            TestContext.WriteLine($"[TestSetup] DOCKER_HOST already set for process: {prev}");
        }
    }
}
