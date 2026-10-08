using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Hyprism.Core;

public enum ScanVerdict { Clean, Malware, BlockedByPolicy, Unavailable }

/// <summary>
/// Asks the installed antivirus (Microsoft Defender or any product that registers with Windows) to scan content
/// through AMSI, the Antimalware Scan Interface. Every file Hyprism downloads is scanned before it is unpacked,
/// run or saved.
/// </summary>
public static class Antivirus
{
    [DllImport("amsi.dll", CharSet = CharSet.Unicode)] static extern int AmsiInitialize(string appName, out IntPtr context);
    [DllImport("amsi.dll")] static extern void AmsiUninitialize(IntPtr context);
    [DllImport("amsi.dll")] static extern int AmsiOpenSession(IntPtr context, out IntPtr session);
    [DllImport("amsi.dll")] static extern void AmsiCloseSession(IntPtr context, IntPtr session);
    [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
    static extern int AmsiScanBuffer(IntPtr context, byte[] buffer, uint length, string contentName, IntPtr session, out int result);

    const int AmsiResultDetected = 32768;           // AMSI_RESULT_DETECTED and above = malware
    const int BlockedByAdminStart = 0x4000, BlockedByAdminEnd = 0x4FFF;

    /// <summary>Scans one buffer. <see cref="ScanVerdict.Unavailable"/> when no antivirus answers AMSI.</summary>
    public static ScanVerdict Scan(byte[] data, string name)
    {
        IntPtr ctx = IntPtr.Zero, session = IntPtr.Zero;
        try
        {
            if (AmsiInitialize("Hyprism", out ctx) != 0) return ScanVerdict.Unavailable;
            if (AmsiOpenSession(ctx, out session) != 0) return ScanVerdict.Unavailable;
            if (AmsiScanBuffer(ctx, data, (uint)data.Length, name, session, out var result) != 0) return ScanVerdict.Unavailable;
            return result >= AmsiResultDetected ? ScanVerdict.Malware
                : result is >= BlockedByAdminStart and <= BlockedByAdminEnd ? ScanVerdict.BlockedByPolicy
                : ScanVerdict.Clean;
        }
        catch (DllNotFoundException) { return ScanVerdict.Unavailable; }
        catch (EntryPointNotFoundException) { return ScanVerdict.Unavailable; }
        finally
        {
            if (session != IntPtr.Zero) AmsiCloseSession(ctx, session);
            if (ctx != IntPtr.Zero) AmsiUninitialize(ctx);
        }
    }

    /// <summary>
    /// Scans a download (and, for a zip, every file inside it). Throws if anything is flagged, so callers never
    /// unpack, run or save it. Reports the outcome to <paramref name="log"/>.
    /// </summary>
    public static void EnsureClean(byte[] data, string name, IProgress<string>? log = null)
    {
        var verdict = Scan(data, name);
        Throw(verdict, name);
        int scanned = 1;
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(new MemoryStream(data));
            foreach (var entry in zip.Entries.Where(e => e.Length > 0))
            {
                using var ms = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(ms);
                var v = Scan(ms.ToArray(), $"{name}/{entry.FullName}");
                Throw(v, $"{entry.FullName} inside {name}");
                if (v != ScanVerdict.Unavailable) scanned++;
                verdict = verdict == ScanVerdict.Unavailable ? v : verdict;
            }
        }
        log?.Report(verdict == ScanVerdict.Unavailable
            ? $"No antivirus answered the scan request for {name} (AMSI unavailable); continuing without a scan."
            : $"Antivirus scan: {name} is clean ({scanned} item{(scanned == 1 ? "" : "s")} checked)");
    }

    static void Throw(ScanVerdict v, string what)
    {
        if (v == ScanVerdict.Malware)
            throw new InvalidOperationException($"Your antivirus flagged {what} as malicious, so Hyprism did not use it.");
        if (v == ScanVerdict.BlockedByPolicy)
            throw new InvalidOperationException($"{what} is blocked by your organization's security policy.");
    }
}
