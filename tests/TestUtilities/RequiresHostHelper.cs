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
        // so Windows test runs can reach Docker Desktop's WSL engine (npipe) or a tcp-exposed daemon.
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

            // Verify or build the image
            try
            {
                var dockerConfig = new Docker.DotNet.DockerClientConfiguration(new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process)));
                using var client = dockerConfig.CreateClient();
                var images = client.Images.ListImagesAsync(new ImagesListParameters { All = true }).GetAwaiter().GetResult();
                var found = images.Any(i => (i.RepoTags ?? new System.Collections.Generic.List<string>()).Contains(imageName));
                if (!found)
                {
                    var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "..", "..", "..", ".."));
                    var absDockerfile = System.IO.Path.Combine(repoRoot, dockerfilePath);
                    if (!System.IO.File.Exists(absDockerfile))
                    {
                        Assert.Fail($"Required dockerfile '{absDockerfile}' not found; cannot build '{imageName}' - RequiresHost tests must be able to build the image.");
                    }

                    // Build the image. On Windows prefer to run the docker CLI inside WSL so the build and resulting image
                    // are created inside the WSL-hosted daemon. On non-Windows just invoke docker directly.
                    ProcessStartInfo psi;
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        // Find a running WSL distro (exclude docker-desktop) to run the docker CLI within.
                        string distro = null;
                        try
                        {
                            var listInfo = new ProcessStartInfo("wsl", "-l -v") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                            using var li = Process.Start(listInfo);
                            var outp = li?.StandardOutput.ReadToEnd() ?? string.Empty;
                            li?.WaitForExit();
                            var lines = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            // lines include header; pick first running distro not named docker-desktop
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("NAME", StringComparison.OrdinalIgnoreCase)) continue;
                                // columns separated by spaces; last column is VERSION, second last STATE
                                // simpler: find token "Running"
                                if (trimmed.Contains("Running") && !trimmed.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("docker-desktop-data", StringComparison.OrdinalIgnoreCase))
                                {
                                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    distro = parts[0];
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // ignore and fall back to calling docker directly
                        }

                        if (!string.IsNullOrWhiteSpace(distro))
                        {
                            // wsl -d <distro> -- docker build ...
                            var buildArgs = $"-d {distro} -- docker build -t {imageName} -f \"{absDockerfile}\" \"{System.IO.Path.GetDirectoryName(absDockerfile)}\"";
                            psi = new ProcessStartInfo("wsl", buildArgs)
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                        }
                        else
                        {
                            // No suitable WSL distro found; call docker directly and rely on DOCKER_HOST being set
                            psi = new ProcessStartInfo("docker", $"build -t {imageName} -f \"{absDockerfile}\" \"{System.IO.Path.GetDirectoryName(absDockerfile)}\"")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                        }
                    }
                    else
                    {
                        psi = new ProcessStartInfo("docker", $"build -t {imageName} -f \"{absDockerfile}\" \"{System.IO.Path.GetDirectoryName(absDockerfile)}\"")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                    }

                    using var p = Process.Start(psi);
                    if (p == null)
                    {
                        Assert.Fail($"Failed to start 'docker build' to create {imageName}; RequiresHost tests must be able to build the image.");
                    }

                    var stdout = p.StandardOutput.ReadToEnd();
                    var stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        TestContext.WriteLine("docker build failed:\n" + stdout + "\n" + stderr);
                        Assert.Fail($"docker build for {imageName} failed with exit code {p.ExitCode}; RequiresHost tests must be able to build the image.");
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to verify/build docker image: {ex.Message} - RequiresHost tests must be able to verify or build the image.");
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
