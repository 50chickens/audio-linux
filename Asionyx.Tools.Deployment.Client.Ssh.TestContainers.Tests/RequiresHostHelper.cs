using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using NUnit.Framework;

// Legacy/local copy retained for compatibility in this test project only.
// The canonical shared helper is located at tests/TestUtilities/RequiresHostHelper.cs.
public static class RequiresHostHelper_Legacy
{
    // Ensure a Docker host is reachable and the required image exists. If not, attempt to build it.
    // On failure the helper will call Assert.Ignore so calling tests are skipped rather than failing.
    public static void EnsureHostOrIgnore(string imageName = "audio-linux/ci-systemd-trixie:local", string dockerfilePath = "build/ci-systemd-trixie.Dockerfile")
    {
        // Temporary set DOCKER_HOST to the common WSL address if not set; tests in this repo expect tcp://localhost:2375 in many cases.
        var prev = Environment.GetEnvironmentVariable("DOCKER_HOST", EnvironmentVariableTarget.Process);
        try
        {
            if (string.IsNullOrWhiteSpace(prev))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://localhost:2375", EnvironmentVariableTarget.Process);
            }

            // Try to reach Docker using Docker.DotNet (Testcontainers / Docker.DotNet is already available in the test projects).
            try
            {
                var dockerConfig = new Docker.DotNet.DockerClientConfiguration();
                using var client = dockerConfig.CreateClient();
                // Ping to check responsiveness
                var pingTask = client.System.PingAsync();
                if (!pingTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    Assert.Fail("Docker daemon not reachable (ping timed out) - RequiresHost tests must run against a reachable Docker daemon.");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Docker daemon not reachable: {ex.Message} - RequiresHost tests must run against a reachable Docker daemon.");
            }

            // Verify the image exists locally. If not, try to build it using docker build.
            try
            {
                var dockerConfig = new Docker.DotNet.DockerClientConfiguration();
                using var client = dockerConfig.CreateClient();
                var images = client.Images.ListImagesAsync(new ImagesListParameters { All = true }).GetAwaiter().GetResult();
                var found = images.Any(i => (i.RepoTags ?? new System.Collections.Generic.List<string>()).Contains(imageName));
                if (!found)
                {
                    // Attempt to build the image from repo. Use Docker CLI (reliable and available on Docker Desktop/WSL setups).
                    var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "..", "..", "..", ".."));
                    var absDockerfile = System.IO.Path.Combine(repoRoot, dockerfilePath);
                    if (!System.IO.File.Exists(absDockerfile))
                    {
                            Assert.Fail($"Required dockerfile '{absDockerfile}' not found; cannot build '{imageName}' - RequiresHost tests must be able to build the image.");
                    }

                    var psi = new ProcessStartInfo("docker", $"build -t {imageName} -f \"{absDockerfile}\" \"{System.IO.Path.GetDirectoryName(absDockerfile)}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

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
            // restore previous DOCKER_HOST if it wasn't set originally
            if (string.IsNullOrWhiteSpace(prev))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", prev, EnvironmentVariableTarget.Process);
            }
        }
    }
}
