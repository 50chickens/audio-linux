using AudioLinux.Library.SNRCalibration;

namespace AudioLinux.ConsoleApp.SNRCalibration;

internal static class Program
{
    private static void Main(string[] args)
    {
        ListDevices();
    }
    private static void ListDevices()
    {
        var soundFlowDeviceEnumerator = new SoundFlowDeviceEnumerator();
        var audioDevices = soundFlowDeviceEnumerator.GetAudioDevices();
        foreach (var device in audioDevices)
        {
            Console.WriteLine($"Device: {device.Name}, Default: {device.IsDefault}");
        }
    }
}