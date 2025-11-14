using System;
using System.Diagnostics;
using System.Linq;
using Docker.DotNet.Models;
using NUnit.Framework;

/// <summary>
/// Shared helper used by integration tests that require a Docker host and an image to exist.
/// This helper will assert-fail when Docker or the build step is unavailable so tests are
/// deterministic and fail loudly on misconfigured runners.
/// </summary>
public static class RequiresHostHelper
{
    public static void EnsureHostOrFail(string imageName = "audio-linux/ci-systemd-trixie:local", string dockerfilePath = "build/ci-systemd-trixie.Dockerfile")
    {
    // Prefer an existing DOCKER_HOST if provided. Otherwise probe common endpoints
    // so Windows test runs can reach Docker Desktop's engine (npipe) or a tcp-exposed daemon.
        var prev = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
        string chosenEndpoint = null;
        var attempted = new System.Collections.Generic.List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(prev))
            {
                chosenEndpoint = prev;
            }
            else
            {
                // Common endpoints to try (order matters): Docker Desktop Linux engine npipe, default npipe, tcp localhost, unix socket.
                var candidates = new[] {
                    "npipe://./pipe/dockerDesktopLinuxEngine",
                    "npipe://./pipe/docker_engine",
                    "tcp://localhost:2375",
                    "unix:///var/run/docker.sock"
                };

                foreach (var candidate in candidates)
                {
                    attempted.Add(candidate);
                    try
                    {
                        // Try to ping using this endpoint
                        var cfg = new Docker.DotNet.DockerClientConfiguration(new Uri(candidate));
                        using var client = cfg.CreateClient();
                        var ping = client.System.PingAsync();
                        if (ping.Wait(TimeSpan.FromSeconds(5)))
                        {
                            chosenEndpoint = candidate;
                            break;
                        }
                    }
                    catch
                    {
                        // ignore and try next
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(chosenEndpoint))
            {
                Assert.Fail($"Docker daemon not reachable (tried: {string.Join(", ", attempted)}) - RequiresHost tests must run against a reachable Docker daemon.");
            }

            // Ensure the process-level DOCKER_HOST points to the chosen endpoint so 'docker' CLI and child processes use the same endpoint
            Environment.SetEnvironmentVariable("DOCKER_HOST", chosenEndpoint, EnvironmentVariableTarget.Process);

            // Verify the image exists locally. The build of the systemd test image is expected
            // to be performed by the repository pre-flight script (build-deploy-and-run.ps1).
            // If the image is missing the tests fail and instruct the developer/CI to run the
            // pre-flight checks which will build/start the required container in the configured Docker engine.
            try
            {
                var dockerConfig = new Docker.DotNet.DockerClientConfiguration(new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process)));
                using var client = dockerConfig.CreateClient();
                var images = client.Images.ListImagesAsync(new ImagesListParameters { All = true }).GetAwaiter().GetResult();
                var found = images.Any(i => (i.RepoTags ?? new System.Collections.Generic.List<string>()).Contains(imageName));
                if (!found)
                {
                    // Image not found — fail tests and instruct caller to run repository pre-flight which will
                    // build the image in the configured Docker engine and start the test container. This keeps
                    // engine-specific interactions out of test code and centralizes environment setup in the PowerShell pre-flight.
                    var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "..", "..", "..", ".."));
                    var absDockerfile = System.IO.Path.Combine(repoRoot, dockerfilePath);
                    var hint = "Run build-deploy-and-run.ps1 pre-flight to build/start the required test image and container.";
                    if (!System.IO.File.Exists(absDockerfile))
                    {
                        Assert.Fail($"Required dockerfile '{absDockerfile}' not found; cannot build '{imageName}'. {hint}");
                    }

                    Assert.Fail($"Required docker image '{imageName}' not found locally. {hint}");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to verify docker image: {ex.Message} - RequireHost tests must be run after repository pre-flight.");
            }
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(prev))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", prev, EnvironmentVariableTarget.Process);
            }
        }
    }

    // Backwards-compat wrapper used by existing tests
    public static void EnsureHostOrIgnore() => EnsureHostOrFail();
}
