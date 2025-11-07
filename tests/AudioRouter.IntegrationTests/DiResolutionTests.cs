using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using AudioRouter;

namespace AudioRouter.IntegrationTests;

public class DiResolutionTests
{
    [Test]
    public void AllControllersAndConstructableTypesResolve()
    {
        using var factory = new WebApplicationFactory<Program>();
        // Ensure host built
        using var client = factory.CreateClient();

        var sp = factory.Services;
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider;

        var asm = typeof(Program).Assembly;

        var failures = new List<string>();

        // 1) Ensure all controllers can be constructed via DI (ActivatorUtilities) — controllers are not registered as services by default
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

        // 2) Try to construct public classes in the app assembly using ActivatorUtilities so constructor dependencies are satisfied
        var candidateTypes = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Any())
            // limit to application namespaces to avoid framework/system types
            .Where(t => t.Namespace != null && (t.Namespace.StartsWith("AudioRouter") || t.Namespace.StartsWith("AudioRouter.")))
            .ToList();

        foreach (var t in candidateTypes)
        {
            // skip Program and controller types (already checked)
            if (t == typeof(Program)) continue;
            if (typeof(ControllerBase).IsAssignableFrom(t)) continue;

            // Skip simple DTOs / records that only have primitive/string/value-type ctor params
            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var anyResolvableCtor = ctors.Any(ctor => ctor.GetParameters().Any(p => !IsPrimitiveOrString(p.ParameterType)));
            if (!anyResolvableCtor)
            {
                // nothing DI can/should resolve here (e.g. record RouteRequest(string, string))
                continue;
            }

            try
            {
                // Attempt to instantiate via DI-resolving constructor parameters
                ActivatorUtilities.CreateInstance(provider, t);
            }
            catch (Exception ex)
            {
                failures.Add($"ActivatorUtilities failed for {t.FullName}: {ex.Message}");
            }
        }

        if (failures.Any())
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
