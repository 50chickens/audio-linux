using SoundFlow.Backends.MiniAudio;
using SoundFlow.Structs;

namespace AudioLinux.Library.SNRCalibration
{
    public class SoundFlowDeviceEnumerator
    {
        public DeviceInfo[] GetAudioDevices()
        {
            var engine = new MiniAudioEngine();
            return engine.PlaybackDevices;
        }
    }
    // {
    //     private MiniAudioEngine _engine = new MiniAudioEngine();
    //     private AudioFormat? _format;

    //     public SoundFlow()
    //     {
    //         _engine = new MiniAudioEngine();

    //         _format = AudioFormat.DvdHq; // 48kHz, 2-channel, 32-bit float

    //     }
    //     public DeviceInfo[] GetAudioDevices()
    //     {
    //         return _engine.PlaybackDevices;
    //     }
    //     //public void Play()
    //     //{




    //     //    var defaultDevice = _engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
    //     //    using var playbackDevice = engine.InitializePlaybackDevice(defaultDevice, format);

    //     //    // 4. Create a SoundPlayer, passing the engine and format context.
    //     //    // Make sure you replace "path/to/your/audiofile.wav" with the actual path.
    //     //    using var dataProvider = new StreamDataProvider(engine, format, File.OpenRead("path/to/your/audiofile.wav"));
    //     //    var player = new SoundPlayer(engine, format, dataProvider);

    //     //    // 5. Add the player to the device's master mixer.
    //     //    playbackDevice.MasterMixer.AddComponent(player);

    //     //    // 6. Start the device to begin the audio stream.
    //     //    playbackDevice.Start();

    //     //    // 7. Start the player.
    //     //    player.Play();

    //     //    Console.WriteLine($"Playing audio on '{playbackDevice.Info?.Name}'... Press any key to stop.");
    //     //    Console.ReadKey();

    //     //    // 8. Stop the device, which also stops the audio stream.
    //     //    playbackDevice.Stop();
    //     //}

    // }
}
