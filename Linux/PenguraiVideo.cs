using System.Runtime.InteropServices;

namespace KevinBrowser.Linux;

/// <summary>
/// Penguraian video H.264 di GPU (VA-API). Kalau ada, YouTube dipaksa
/// mengirim H.264 (<see cref="SkripYouTube"/>) dan GL sink GStreamer
/// dinyalakan (<see cref="Lingkungan.Siapkan"/>).
/// </summary>
/// <remarks>
/// Terukur, YouTube 720p, Intel HD 4600 (CPU seluruh pohon proses, 100% =
/// satu inti):
///
///                                  tab dilihat   tab di latar
///   VP9, tanpa GL sink (dulu)      150–182%      ±45%
///   H.264 VA + GL sink             95–110%       ±18%
///   H.264 VA, tanpa GL sink        106–149%      ±41%
///   H.264 software + GL sink       114–147%      ±50%
///
/// PSS+GPU sama (832–848 vs 817–887 MB), buffer GPU tidak membengkak di
/// tab latar. Jadi keduanya harus bersama, dan hanya kalau decoder
/// hardware ada; tanpa itu H.264 tidak lebih hemat dari VP9.
///
/// Biaya pemeriksaannya ±13 ms saat start dan ±2 MB PSS di proses UI:
/// libva tidak melepas drivernya lagi (terukur di HD 4600: iHD_drv_video.so
/// 1,4 MB, yang dicoba dulu lalu gagal, dan i965_drv_video.so 0,4 MB).
/// </remarks>
static partial class PenguraiVideo
{
    // Penyedia vah264dec (gstreamer1.0-plugins-bad). Tanpa plugin ini WebKit
    // tidak bisa memakai VA-API walau drivernya ada.
    const string PluginVa = "/usr/lib/x86_64-linux-gnu/gstreamer-1.0/libgstva.so";

    // va.h
    const int ProfilH264Main = 6, ProfilH264High = 7, ProfilH264ConstrainedBaseline = 13;
    const int TitikMasukVld = 1;

    /// <summary>Env <c>KEVIN_BROWSER_H264</c> (0/1) menimpa hasil pemeriksaan.</summary>
    public static bool AdaH264 { get; } = Environment.GetEnvironmentVariable("KEVIN_BROWSER_H264") is { Length: > 0 } env
        ? env == "1"
        : PeriksaDanCatat();

    static bool PeriksaDanCatat()
    {
        var jam = System.Diagnostics.Stopwatch.StartNew();
        var ada = Periksa();
        Catat.Tulis($"decode H.264 hardware: {(ada ? "ada" : "tidak ada")} ({jam.ElapsedMilliseconds} ms)");
        return ada;
    }

    // Mode cpu tanpa GL sama sekali, dan video YouTube-nya hitam.
    public static bool PaksaH264 => AdaH264 && Lingkungan.Gambar != ModeGambar.Cpu;

    /// <summary>
    /// Untuk youtube.com, di dunia JS halaman sejak awal dokumen: VP9 dan AV1
    /// dilaporkan tidak didukung, sehingga YouTube memilih H.264 (seperti
    /// ekstensi h264ify). WebKit ini juga punya ManagedMediaSource.
    /// </summary>
    public const string SkripYouTube =
        """
        (() => {
          const tolak = t => /vp0?9|av01/i.test(String(t));
          for (const nama of ['MediaSource', 'ManagedMediaSource']) {
            const ms = window[nama];
            if (!ms) continue;
            const asli = ms.isTypeSupported.bind(ms);
            ms.isTypeSupported = t => !tolak(t) && asli(t);
          }
          const mc = navigator.mediaCapabilities;
          if (mc) {
            const asli = mc.decodingInfo.bind(mc);
            mc.decodingInfo = c => c && c.video && tolak(c.video.contentType)
              ? Promise.resolve({ supported: false, smooth: false, powerEfficient: false })
              : asli(c);
          }
          const bisa = HTMLMediaElement.prototype.canPlayType;
          HTMLMediaElement.prototype.canPlayType = function (t) { return tolak(t) ? '' : bisa.call(this, t); };
        })();
        """;

    public static readonly string[] SitusYouTube = ["https://*.youtube.com/*", "https://*.youtube-nocookie.com/*"];

    static bool Periksa()
    {
        if (!File.Exists(PluginVa) || !Directory.Exists("/dev/dri"))
            return false;
        // Tanpa ini libva mencetak beberapa baris ke stderr setiap inisialisasi,
        // juga di proses web nanti, termasuk "iHD_drv_video.so init failed" di
        // GPU Intel lama (driver iHD dicoba dulu sebelum i965).
        GLib.Functions.Setenv("LIBVA_MESSAGING_LEVEL", "0", false);
        try
        {
            foreach (var simpul in Directory.GetFiles("/dev/dri", "renderD*"))
                if (BisaH264(simpul))
                    return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // libva-drm2 tidak terpasang.
        }
        return false;
    }

    static unsafe bool BisaH264(string simpul)
    {
        var fd = open(simpul, O_RDWR | O_CLOEXEC);
        if (fd < 0)
            return false;
        try
        {
            var tampilan = vaGetDisplayDRM(fd);
            if (tampilan == IntPtr.Zero)
                return false;
            try
            {
                if (vaInitialize(tampilan, out _, out _) != 0)
                    return false;
                var titik = stackalloc int[Math.Max(vaMaxNumEntrypoints(tampilan), 1)];
                foreach (var profil in (ReadOnlySpan<int>)[ProfilH264Main, ProfilH264High, ProfilH264ConstrainedBaseline])
                {
                    if (vaQueryConfigEntrypoints(tampilan, profil, titik, out var jumlah) != 0)
                        continue;
                    for (var i = 0; i < jumlah; i++)
                        if (titik[i] == TitikMasukVld)
                            return true;
                }
                return false;
            }
            finally
            {
                vaTerminate(tampilan);
            }
        }
        finally
        {
            close(fd);
        }
    }

    const int O_RDWR = 2, O_CLOEXEC = 0x80000;

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string jalur, int bendera);

    [LibraryImport("libc")]
    private static partial int close(int fd);

    [LibraryImport("libva-drm.so.2")]
    private static partial IntPtr vaGetDisplayDRM(int fd);

    [LibraryImport("libva.so.2")]
    private static partial int vaInitialize(IntPtr tampilan, out int mayor, out int minor);

    [LibraryImport("libva.so.2")]
    private static partial int vaTerminate(IntPtr tampilan);

    [LibraryImport("libva.so.2")]
    private static partial int vaMaxNumEntrypoints(IntPtr tampilan);

    [LibraryImport("libva.so.2")]
    private static unsafe partial int vaQueryConfigEntrypoints(IntPtr tampilan, int profil, int* titikMasuk, out int jumlah);
}
