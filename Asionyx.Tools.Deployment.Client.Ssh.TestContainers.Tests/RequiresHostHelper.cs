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
    // Temporary set DOCKER_HOST to a common TCP endpoint if not set; tests in this repo expect tcp://localhost:2375 in many cases.
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

            // Verify the image exists locally. The repository pre-flight script (build-deploy-and-run.ps1)
            // is responsible for building the test image and starting the container. If the image is
            // missing we fail the test and instruct the caller to run pre-flight.
            try
            {
                var dockerConfig = new Docker.DotNet.DockerClientConfiguration();
                using var client = dockerConfig.CreateClient();
                var images = client.Images.ListImagesAsync(new ImagesListParameters { All = true }).GetAwaiter().GetResult();
                var found = images.Any(i => (i.RepoTags ?? new System.Collections.Generic.List<string>()).Contains(imageName));
                if (!found)
                {
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
                Assert.Fail($"Failed to verify docker image: {ex.Message} - RequiresHost tests must be run after repository pre-flight.");
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
