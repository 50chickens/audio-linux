using NUnit.Framework;
using AudioRouter.Library.Core;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AudioRouter.Library.Audio.SoundFlow.UnitTests;

public class SoundFlowRouterTests
{
    [Test]
    public void StartStop_Works()
    {
        var logger = Substitute.For<ILogger<SoundFlowAudioRouter>>();
        var router = new SoundFlowAudioRouter(logger);

        Assert.IsFalse(router.IsRouting);

        var started = router.StartRoute(new RouteRequest("in", "out"));
        Assert.IsTrue(started);
        Assert.IsTrue(router.IsRouting);

        var stopped = router.StopRoute();
        Assert.IsTrue(stopped);
        Assert.IsFalse(router.IsRouting);
    }
}
