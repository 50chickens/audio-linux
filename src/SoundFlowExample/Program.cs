internal class Program
{
    SoundFlow sf = new SoundFlow();
    private static void Main(string[] args)
    {
        var soundFlow = new SoundFlow();
        var devices = soundFlow.GetAudioDevices();
        foreach (var device in devices)
        {
            Console.WriteLine($"Device: {device.Name}, Default: {device.IsDefault}");
        }
    }
}