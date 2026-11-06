using System;
using System.IO;
using NAudio.Wave;

namespace VelvetDairy.Game;

/// <summary>
/// Milking sounds, played on your own computer only (nobody else hears them).
/// The stall sounds are cut from "Milk Pump Step02" by Erdie on Freesound (freesound.org/s/49877).
/// The pump loops while a stall milks you; farmhand strokes make a squirt into the pail.
/// </summary>
public sealed class Sounds : IDisposable
{
    private readonly Plugin plugin;
    private readonly string dir;

    /// <summary>
    /// Your own recordings go here (it survives plugin updates). A file named like a built-in sound
    /// (pump_loop, pump_start, pump_stop, squirt) as .wav or .mp3 is used instead of the built-in one.
    /// </summary>
    public string CustomDir { get; }

    public static readonly string[] Names = { "pump_start", "pump_loop", "pump_stop", "squirt" };
    private WaveOutEvent? loopOut;
    private AudioFileReader? loopReader;
    private bool wasStall;

    private FarmSave F => plugin.Config.Farm;

    public Sounds(Plugin plugin)
    {
        this.plugin = plugin;
        dir = Path.Combine(Plugin.PluginInterface.AssemblyLocation.Directory?.FullName ?? "", "Assets", "sounds");
        CustomDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "sounds");
        plugin.Game.Stroked += () => Play("squirt.wav", 0.8f);
    }

    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private double loopAt = -1;   // when the loop should take over from the start sound

    /// <summary>
    /// Called every frame. Stall milking plays: start (motor spinning up) → the loop for as long as it lasts → stop (winding down).
    /// The start, loop and stop are cut from one recording so each one carries on exactly where the last left off.
    /// </summary>
    public void Update()
    {
        var now = clock.Elapsed.TotalSeconds;
        var stall = plugin.Game.Active?.Kind == SessionKind.Stall && F.SoundOn;
        if (stall && !wasStall)
        {
            var startLength = Play("pump_start.wav");
            loopAt = now + Math.Max(0, startLength - 0.03);
        }
        else if (!stall && wasStall)
        {
            loopAt = -1;
            StopLoop();
            if (F.SoundOn) Play("pump_stop.wav");
        }

        if (stall && loopAt >= 0 && now >= loopAt)
        {
            loopAt = -1;
            StartLoop("pump_loop.wav");
        }

        wasStall = stall;
        if (loopReader != null) loopReader.Volume = Volume(1f);
    }

    private float Volume(float k) => Math.Clamp(F.SoundVolume * k, 0f, 1f);

    /// <summary>Your recording if you added one, otherwise the built-in sound.</summary>
    private string? Resolve(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        foreach (var ext in new[] { ".wav", ".mp3" })
        {
            var custom = Path.Combine(CustomDir, stem + ext);
            if (File.Exists(custom)) return custom;
        }

        var builtIn = Path.Combine(dir, file);
        return File.Exists(builtIn) ? builtIn : null;
    }

    public bool IsCustom(string stem) =>
        File.Exists(Path.Combine(CustomDir, stem + ".wav")) || File.Exists(Path.Combine(CustomDir, stem + ".mp3"));

    /// <summary>Makes the custom sounds folder and opens it in Explorer.</summary>
    public void OpenCustomFolder()
    {
        try
        {
            Directory.CreateDirectory(CustomDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CustomDir) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Velvet Dairy couldn't open the sounds folder");
        }
    }

    /// <summary>Plays a sound once. Returns its length in seconds (0 if nothing played).</summary>
    public double Play(string file, float k = 1f)
    {
        if (!F.SoundOn) return 0;
        var path = Resolve(file);
        if (path == null) return 0;
        try
        {
            var reader = new AudioFileReader(path) { Volume = Volume(k) };
            var length = reader.TotalTime.TotalSeconds;
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                reader.Dispose();
            };
            output.Play();
            return length;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, $"Velvet Dairy couldn't play {file}");
            return 0;
        }
    }

    private void StartLoop(string file)
    {
        StopLoop();
        var path = Resolve(file);
        if (path == null) return;
        try
        {
            loopReader = new AudioFileReader(path) { Volume = Volume(1f) };
            loopOut = new WaveOutEvent();
            loopOut.Init(new LoopStream(loopReader));
            loopOut.Play();
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, $"Velvet Dairy couldn't loop {file}");
            StopLoop();
        }
    }

    private void StopLoop()
    {
        try
        {
            loopOut?.Stop();
            loopOut?.Dispose();
            loopReader?.Dispose();
        }
        catch
        {
            // already gone
        }

        loopOut = null;
        loopReader = null;
    }

    public void Dispose() => StopLoop();

    /// <summary>Plays a sound over and over without a gap.</summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream source;

        public LoopStream(WaveStream source) => this.source = source;

        public override WaveFormat WaveFormat => source.WaveFormat;
        public override long Length => long.MaxValue / 2;

        public override long Position
        {
            get => source.Position;
            set => source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = source.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (source.Position == 0) break; // empty file
                    source.Position = 0;
                }

                total += read;
            }

            return total;
        }
    }
}
