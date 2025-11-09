using NUnit.Framework;
using NSubstitute;
using AudioRouter.Library.Audio.JackSharp;
using AudioRouter.Library.Core;
using Microsoft.Extensions.Logging;

namespace AudioRouter.UnitTests;

public class RouterServiceTests
{
    [Test]
    public void StartRoute_LogsAndReturnsTrue_WhenSuccessful()
    {
        var logger = Substitute.For<ILogger<JackAudioRouter>>();
        var router = new JackAudioRouter(logger);

    var result = router.StartRoute(new RouteRequest("input1", "output1"));

    Assert.That(result, Is.True);
    Assert.That(router.IsRouting, Is.True);
    }

    [Test]
    public void StopRoute_StopsRouting()
    {
        var logger = Substitute.For<ILogger<JackAudioRouter>>();
        var router = new JackAudioRouter(logger);
        router.StartRoute(new RouteRequest("input1", "output1"));

        var result = router.StopRoute();

    Assert.That(result, Is.True);
    Assert.That(router.IsRouting, Is.False);
    }
}
