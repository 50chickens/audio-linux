namespace AudioRouter.Services
{
    public record RouteRequest(string FromDevice, string ToDevice);

    public interface IAudioRouter
    {
        /// <summary>
        /// Start routing audio from one device to another.
        /// </summary>
        /// <returns>True if started successfully</returns>
        bool StartRoute(RouteRequest request);

        /// <summary>
        /// Stop the active route for the given devices (or all if devices null/empty)
        /// </summary>
        bool StopRoute(RouteRequest? request = null);

        bool IsRouting { get; }
    }
}
