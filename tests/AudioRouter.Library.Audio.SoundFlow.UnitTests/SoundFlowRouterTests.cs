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

    Assert.That(router.IsRouting, Is.False);

    var started = router.StartRoute(new RouteRequest("in", "out"));
    Assert.That(started, Is.True);
    Assert.That(router.IsRouting, Is.True);

    var stopped = router.StopRoute();
    Assert.That(stopped, Is.True);
    Assert.That(router.IsRouting, Is.False);
    }
}
