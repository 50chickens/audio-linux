using System;
using Microsoft.Extensions.Logging;
using AudioRouter.Library.Core;

namespace AudioRouter.Library.Audio.SoundFlow
{
    public class SoundFlowAudioRouter : IAudioRouter
    {
        private readonly ILogger<SoundFlowAudioRouter> _logger;
        private bool _isRouting;

        public SoundFlowAudioRouter(ILogger<SoundFlowAudioRouter> logger)
        {
            _logger = logger;
        }

        public bool IsRouting => _isRouting;

        public bool StartRoute(RouteRequest request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            _logger.LogInformation("[SoundFlow] Starting audio route from {From} to {To}", request.FromDevice, request.ToDevice);

            try
            {
                // TODO: Implement SoundFlow SDK calls here.
                _isRouting = true;
                _logger.LogInformation("[SoundFlow] Route started");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SoundFlow] Failed to start route");
                return false;
            }
        }

        public bool StopRoute(RouteRequest? request = null)
        {
            _logger.LogInformation("[SoundFlow] Stopping audio route for {From}->{To}", request?.FromDevice ?? "any", request?.ToDevice ?? "any");

            try
            {
                _isRouting = false;
                _logger.LogInformation("[SoundFlow] Route stopped");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SoundFlow] Failed to stop route");
                return false;
            }
        }
    }
}
