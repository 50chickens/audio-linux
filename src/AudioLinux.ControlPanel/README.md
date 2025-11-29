# AudioLinux Control Panel (basic)

This is a minimal control-panel program that enumerates ALSA cards and simple mixer controls using `aplay`/`amixer` and displays textual meters in the terminal.

It references the local `SoundFlow` and `UILayout` projects so you can extend the UI with those libraries (build them first).

Quick start

1. Build SoundFlow and UILayout projects first (they are referenced by this project):

```bash
dotnet build /home/pistomp/SoundFlow/SoundFlow.sln
dotnet build /home/pistomp/UILayout/UILayout.sln
```

2. Run the control panel

```bash
cd /home/pistomp/audio-linux/src/AudioLinux.ControlPanel
dotnet run
```

Notes and next steps

- This project uses `amixer`/`aplay` command-line tools. Ensure `alsa-utils` is installed on your system.
- The current UI is console-based to get a working meter quickly. You can extend `Program.cs` to render a graphical UI using `UILayout.Skia.SDL` or any other UILayout backend now that the project references it.
- For accurate, low-latency level meters, integrate SoundFlow's capture APIs (once built) to read PCM samples and compute RMS/peak levels.
