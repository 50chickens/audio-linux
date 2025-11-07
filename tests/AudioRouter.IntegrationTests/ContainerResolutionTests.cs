using System;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using AudioRouter.Library.Core;

namespace AudioRouter.IntegrationTests;

public class ContainerResolutionTests
{
    [Test]
    public void AudioRouter_IAudioRouter_IsRegistered()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var sp = factory.Services;
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider;

        var router = provider.GetService(typeof(IAudioRouter));
        Assert.IsNotNull(router, "IAudioRouter should be registered in the DI container");
    }
}
