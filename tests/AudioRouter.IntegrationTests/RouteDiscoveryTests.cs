using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace AudioRouter.IntegrationTests;

public class RouteDiscoveryTests
{
    [Test]
    public void Controllers_HaveRouteAndActionsHaveHttpMethodAttributes()
    {
        var asm = typeof(Program).Assembly;
        var controllerTypes = asm.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic).ToList();
        Assert.IsNotEmpty(controllerTypes, "No controllers found in AudioRouter assembly");

        foreach (var ct in controllerTypes)
        {
            // Controller should have a Route attribute (e.g., [Route("api/[controller]")])
            var classRoute = ct.GetCustomAttribute<RouteAttribute>();
            Assert.IsNotNull(classRoute, $"Controller {ct.FullName} does not declare a Route attribute");

            // Ensure there's at least one action with an HttpMethod attribute
            var actionMethods = ct.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(NonActionAttribute)))
                .ToList();

            Assert.IsNotEmpty(actionMethods, $"Controller {ct.FullName} has no public action methods");

            var hasHttpMethod = actionMethods.Any(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute || a is RouteAttribute));
            Assert.IsTrue(hasHttpMethod, $"Controller {ct.FullName} has no actions with HTTP method or Route attributes");
        }
    }
}
