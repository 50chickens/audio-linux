using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Reflection;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("AudioLinux Control Panel - ALSA/AMixer polling\n");

        // Show that SoundFlow and UILayout assemblies are referenced (basic check)
        TryListReferencedTypes();

        var cards = GetAlsaCards();
        if (!cards.Any())
        {
            Console.WriteLine("No ALSA cards found (check that ALSA is available).\n");
            return;
        }

        var cardControls = new Dictionary<int, List<string>>();
        foreach (var c in cards)
        {
            var controls = GetControlsForCard(c.Id);
            cardControls[c.Id] = controls;
        }

        Console.WriteLine("Press Ctrl+C to quit.");

        while (true)
        {
            Console.Clear();
            Console.WriteLine("AudioLinux Control Panel - ALSA Controls and Meters\n");
            foreach (var c in cards)
            {
                Console.WriteLine($"Card {c.Id}: {c.Name}");
                var controls = cardControls[c.Id];
                if (!controls.Any())
                {
                    Console.WriteLine("  (no simple controls found)");
                    continue;
                }
                foreach (var ctrl in controls)
                {
                    var val = GetControlPercent(c.Id, ctrl);
                    var meter = MakeMeter(val);
                    Console.WriteLine($"  {ctrl}: {val,3}% {meter}");
                }
                Console.WriteLine();
            }

            await Task.Delay(500);
        }
    }

    static void TryListReferencedTypes()
    {
        Console.WriteLine("Checking referenced assemblies (SoundFlow / UILayout):");
        try
        {
            var sfPath = Path.GetFullPath("../../../SoundFlow/Src/bin/Debug/net6.0/SoundFlow.dll");
            if (File.Exists(sfPath))
            {
                var asm = Assembly.LoadFrom(sfPath);
                Console.WriteLine($"  SoundFlow: {asm.FullName} -- {asm.GetTypes().Length} types");
            }
            else
            {
                Console.WriteLine("  SoundFlow assembly not built yet. Build solution to enable deeper integration.");
            }

            var uiPath = Path.GetFullPath("../../../UILayout/UILayout.Skia.SDL/bin/Debug/net6.0/UILayout.Skia.SDL.dll");
            if (File.Exists(uiPath))
            {
                var asm = Assembly.LoadFrom(uiPath);
                Console.WriteLine($"  UILayout: {asm.FullName} -- {asm.GetTypes().Length} types");
            }
            else
            {
                Console.WriteLine("  UILayout assembly not built yet. Build UILayout projects to enable UI components.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (error inspecting assemblies) {ex.Message}");
        }

        Console.WriteLine();
    }

    record AlsCard(int Id, string Name);

    static List<AlsCard> GetAlsaCards()
    {
        var outp = RunCmd("aplay", "-l");
        var cards = new List<AlsCard>();
        if (string.IsNullOrWhiteSpace(outp)) return cards;

        // Lines like: card 0: sndrpihifiberry [snd_rpi_hifiberry_dacplus], device 0: ...
        var rx = new Regex(@"card\s+(\d+):\s+([^\[]+)\[?([^\]]*)\]?", RegexOptions.IgnoreCase);
        foreach (var ln in outp.Split('\n'))
        {
            var m = rx.Match(ln);
            if (m.Success)
            {
                if (int.TryParse(m.Groups[1].Value, out var id))
                {
                    var name = (m.Groups[2].Value + " " + m.Groups[3].Value).Trim();
                    cards.Add(new AlsCard(id, name));
                }
            }
        }
        return cards;
    }

    static List<string> GetControlsForCard(int card)
    {
        var outp = RunCmd("amixer", $"-c {card} scontrols");
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(outp)) return list;
        // Lines like: Simple mixer control 'Master',0
        var rx = new Regex("'(?<name>[^']+)'", RegexOptions.Compiled);
        foreach (var ln in outp.Split('\n'))
        {
            var m = rx.Match(ln);
            if (m.Success)
            {
                list.Add(m.Groups["name"].Value);
            }
        }
        return list.Distinct().ToList();
    }

    static int GetControlPercent(int card, string control)
    {
        var outp = RunCmd("amixer", $"-c {card} get '{control.Replace("'", "'\\''")}'");
        if (string.IsNullOrWhiteSpace(outp)) return 0;
        // find all occurrences of [NN%]
        var rx = new Regex("\\[(\\d{1,3})%\\]");
        var m = rx.Matches(outp);
        if (m.Count == 0) return 0;
        // average if multiple channels
        var sum = 0;
        foreach (Match mm in m) sum += int.Parse(mm.Groups[1].Value);
        return sum / m.Count;
    }

    static string MakeMeter(int percent)
    {
        var len = 20;
        var filled = (int)Math.Round(percent / 100.0 * len);
        return "[" + new string('#', filled) + new string('-', len - filled) + "]";
    }

    static string RunCmd(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return outp ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
