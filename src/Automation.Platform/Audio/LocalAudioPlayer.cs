using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Automation.Platform.Audio;

public static class LocalAudioPlayer
{
    public static async Task PlayAsync(string filename, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filename);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Audio file not found.", fullPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            if (!Path.GetExtension(fullPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Windows audio currently supports WAV files.");
            using var registration = cancellationToken.Register(() => PlaySound(null, IntPtr.Zero, 0));
            var played = await Task.Run(() => PlaySound(fullPath, IntPtr.Zero, 0x00020000 | 0x00000002), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!played) throw new InvalidOperationException("Windows could not play this WAV file.");
            return;
        }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Audio playback is available on Windows and Linux.");
        var player = new[] { "/usr/bin/pw-play", "/usr/bin/paplay", "/usr/bin/ffplay" }.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Install pw-play, paplay or ffplay to play audio.");
        var start = new ProcessStartInfo(player) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        if (player.EndsWith("ffplay", StringComparison.Ordinal))
        {
            start.ArgumentList.Add("-nodisp"); start.ArgumentList.Add("-autoexit"); start.ArgumentList.Add("-loglevel"); start.ArgumentList.Add("error");
            start.ArgumentList.Add("-i");
        }
        start.ArgumentList.Add(fullPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Audio player could not start.");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("Audio playback failed: " + await error);
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);
}
