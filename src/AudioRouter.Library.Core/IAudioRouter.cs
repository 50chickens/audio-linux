namespace AudioRouter.Library.Core
{
    public record RouteRequest(string FromDevice, string ToDevice);

    public interface IAudioRouter
    {
        bool StartRoute(RouteRequest request);
        bool StopRoute(RouteRequest? request = null);
        bool IsRouting { get; }
    }
}
