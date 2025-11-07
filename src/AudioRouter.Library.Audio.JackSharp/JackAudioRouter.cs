using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using AudioRouter.Library.Core;

namespace AudioRouter.Library.Audio.JackSharp
{
    public class JackAudioRouter : IAudioRouter
    {
        private readonly ILogger<JackAudioRouter> _logger;
        private Assembly? _jackAssembly;
        private object? _jackClientInstance;
        private MethodInfo? _connectMethod;
        private MethodInfo? _activateMethod;
        private MethodInfo? _deactivateMethod;
        private MethodInfo? _disposeMethod;

        public JackAudioRouter(ILogger<JackAudioRouter> logger)
        {
            _logger = logger;
        }

        public bool IsRouting { get; private set; }

        private bool TryLoadJackAssembly()
        {
            if (_jackAssembly != null) return true;

            try
            {
                _jackAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "JackSharpCore", StringComparison.OrdinalIgnoreCase));

                if (_jackAssembly == null)
                {
                    _jackAssembly = Assembly.Load("JackSharpCore");
                }

                if (_jackAssembly == null)
                {
                    _logger.LogWarning("JackSharpCore assembly not found in AppDomain.");
                    return false;
                }

                var clientType = _jackAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name.IndexOf("JackClient", StringComparison.OrdinalIgnoreCase) >= 0
                                         || t.Name.IndexOf("Jack", StringComparison.OrdinalIgnoreCase) >= 0 && t.GetMethods().Any(m => m.Name.IndexOf("Connect", StringComparison.OrdinalIgnoreCase) >= 0));

                if (clientType == null)
                {
                    _logger.LogWarning("No suitable Jack client type found in JackSharpCore assembly.");
                    return false;
                }

                var ctor = clientType.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(string))
                    ?? clientType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0);

                if (ctor == null)
                {
                    _logger.LogWarning("No usable constructor found for Jack client type {Type}", clientType.FullName);
                    return false;
                }

                _jackClientInstance = ctor.GetParameters().Length == 1
                    ? Activator.CreateInstance(clientType, new object[] { "audio-router" })
                    : Activator.CreateInstance(clientType);

                if (_jackClientInstance == null)
                {
                    _logger.LogWarning("Failed to instantiate Jack client type {Type}", clientType.FullName);
                    return false;
                }

                _connectMethod = clientType.GetMethods().FirstOrDefault(m => m.Name.IndexOf("Connect", StringComparison.OrdinalIgnoreCase) >= 0
                                                                             && m.GetParameters().Length == 2
                                                                             && m.GetParameters().All(p => p.ParameterType == typeof(string)));

                _activateMethod = clientType.GetMethods().FirstOrDefault(m => m.Name.IndexOf("Activate", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0);
                _deactivateMethod = clientType.GetMethods().FirstOrDefault(m => m.Name.IndexOf("Deactivate", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0);
                _disposeMethod = clientType.GetMethods().FirstOrDefault(m => m.Name.IndexOf("Dispose", StringComparison.OrdinalIgnoreCase) >= 0);

                _logger.LogInformation("JackSharpCore integration initialized. ClientType={Type}, ConnectMethod={Connect}, Activate={Activate}, Deactivate={Deactivate}, Dispose={Dispose}",
                    clientType.FullName,
                    _connectMethod?.Name ?? "(none)",
                    _activateMethod?.Name ?? "(none)",
                    _deactivateMethod?.Name ?? "(none)",
                    _disposeMethod?.Name ?? "(none)");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while trying to load JackSharpCore assembly or initialize client");
                return false;
            }
        }

        public bool StartRoute(RouteRequest request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            _logger.LogInformation("Starting audio route from {From} to {To}", request.FromDevice, request.ToDevice);

            try
            {
                if (!TryLoadJackAssembly())
                {
                    _logger.LogWarning("JackSharpCore not available; running in no-op mode.");
                    IsRouting = true;
                    return true;
                }

                if (_connectMethod != null && _jackClientInstance != null)
                {
                    _connectMethod.Invoke(_jackClientInstance, new object[] { request.FromDevice, request.ToDevice });
                }

                _activateMethod?.Invoke(_jackClientInstance, null);

                IsRouting = true;
                _logger.LogInformation("Audio route started (via JackSharpCore)");
                return true;
            }
            catch (TargetInvocationException tie)
            {
                _logger.LogError(tie.InnerException ?? tie, "Error invoking JackSharpCore methods");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start audio route");
                return false;
            }
        }

        public bool StopRoute(RouteRequest? request = null)
        {
            _logger.LogInformation("Stopping audio route for {From}->{To}", request?.FromDevice ?? "any", request?.ToDevice ?? "any");

            try
            {
                if (_deactivateMethod != null && _jackClientInstance != null)
                {
                    _deactivateMethod.Invoke(_jackClientInstance, null);
                }

                if (_disposeMethod != null && _jackClientInstance != null)
                {
                    _disposeMethod.Invoke(_jackClientInstance, null);
                }

                IsRouting = false;
                _logger.LogInformation("Audio route stopped");
                return true;
            }
            catch (TargetInvocationException tie)
            {
                _logger.LogError(tie.InnerException ?? tie, "Error invoking JackSharpCore stop/dispose methods");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop audio route");
                return false;
            }
        }
    }
}
