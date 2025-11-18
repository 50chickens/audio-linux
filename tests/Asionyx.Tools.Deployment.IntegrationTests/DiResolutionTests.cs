using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Asionyx.Tools.Deployment.IntegrationTests;

public class DiResolutionTests
{
    [Test]
    public void AllControllersAndConstructableTypesResolve()
    {
    var testKey = Guid.NewGuid().ToString("N");
    Environment.SetEnvironmentVariable("DEPLOY_API_KEY", testKey);

        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var sp = factory.Services;
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider;

        var asm = typeof(Program).Assembly;
        var failures = new List<string>();

        // Controllers: instantiate via ActivatorUtilities so constructor DI is validated
        var controllerTypes = asm.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);
        foreach (var ct in controllerTypes)
        {
            try
            {
                ActivatorUtilities.CreateInstance(provider, ct);
            }
            catch (Exception ex)
            {
                failures.Add($"Controller resolution failed: {ct.FullName}: {ex.Message}");
            }
        }

        // Try to instantiate public constructable types (skip DTOs)
        var candidateTypes = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Any())
            .Where(t => t.Namespace != null && (t.Namespace.StartsWith("Asionyx.Tools.Deployment") || t.Namespace.StartsWith("Asionyx.Tools.Deployment.")))
            .ToList();

        foreach (var t in candidateTypes)
        {
            if (t == typeof(Program)) continue;
            if (typeof(ControllerBase).IsAssignableFrom(t)) continue;

            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            // Skip types that act as middleware taking a RequestDelegate 'next' parameter — those cannot be constructed here
            if (ctors.Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(Microsoft.AspNetCore.Http.RequestDelegate))))
            {
                continue;
            }

            var anyResolvableCtor = ctors.Any(ctor => ctor.GetParameters().Any(p => !IsPrimitiveOrString(p.ParameterType)));
            if (!anyResolvableCtor) continue;

            try
            {
                ActivatorUtilities.CreateInstance(provider, t);
            }
            catch (Exception ex)
            {
                failures.Add($"ActivatorUtilities failed for {t.FullName}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail("DI resolution failures:\n" + string.Join("\n", failures));
        }
    }

    private static bool IsPrimitiveOrString(Type t)
    {
        if (t == typeof(string)) return true;
        if (t.IsPrimitive) return true;
        if (t.IsValueType && !t.IsEnum) return true;
        return false;
    }
}
