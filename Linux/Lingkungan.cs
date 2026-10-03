using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KevinBrowser.Linux;

/// <summary>Cara WebKit dan GTK menggambar halaman; env <c>KEVIN_BROWSER_GAMBAR</c>.</summary>
enum ModeGambar
{
    /// <summary>Bawaan: komposisi di GPU, tile halaman dirasterisasi CPU (Skia).</summary>
    Hemat,
    /// <summary><c>gpu</c>: semua di GPU.</summary>
    Gpu,
    /// <summary><c>cpu</c>: tanpa GPU sama sekali. Paling ringan, tapi halaman tonton YouTube hitam.</summary>
    Cpu,
}

static class Lingkungan
{
    // Terukur, binary rilis, Intel HD 4600 (PSS + buffer GPU, seluruh pohon proses):
    //
    //                 google.com   tonton YouTube
    //   Hemat           345 MB     tampil, 1034 MB
    //   Gpu             381 MB     tampil, 1159 MB
    //   Cpu             259 MB     HITAM (WebKitGTK 2.52; bukan MSE, WebGL,
    //                              atau akselerasi kanvas — sudah dicoba)
    public static ModeGambar Gambar { get; } = Environment.GetEnvironmentVariable("KEVIN_BROWSER_GAMBAR") switch
    {
        "gpu" => ModeGambar.Gpu,
        "cpu" => ModeGambar.Cpu,
        _ => ModeGambar.Hemat,
    };

    /// <summary>
    /// Env yang dibaca GTK dan proses WebKit. Harus lewat g_setenv:
    /// Environment.SetEnvironmentVariable milik .NET hanya mengubah salinan
    /// env di dalam runtime, tidak terlihat oleh getenv() di kode C (dan
    /// proses anak WebKit mewarisi env dari sisi C). Nilai yang sudah diset
    /// pemakai tidak ditimpa.
    /// </summary>
    public static void Siapkan()
    {
        // Heap JavaScript. Menurut kode JSC (Heap::proportionalHeapSize), heap
        // yang lebih kecil dari ¼ RAM boleh tumbuh 2× sebelum GC penuh; di
        // YouTube heap yang hidup 65–150 MB sempat membengkak ke 250–300 MB
        // (JSC_logGC=1). Dengan RAM "256 MB", heap di atas 64 MB tumbuh 1,5×
        // dan di atas 128 MB 1,24×. Terukur, YouTube desktop dijeda, 3×
        // masing-masing, PSS+GPU rata-rata 771 → 727 MB (715–819 → 721–735),
        // CPU diam sama (±10%). Sengaja
        // bilangan bulat: opsi JSC berangka pecahan dibaca menurut locale, dan
        // di locale berdesimal koma "1.5" terbaca 1 (GC tanpa henti, halaman
        // tidak pernah selesai dimuat; terjadi).
        GLib.Functions.Setenv("JSC_forceRAMSize", (256L * 1024 * 1024).ToString(), false);

        // Video di atas 720p dilaporkan tidak didukung (MediaCapabilities),
        // jadi situs memilih yang lebih kecil. Layar laptop kecil tidak
        // menampilkan lebih dari itu. Terukur, YouTube dipaksa 1080p: CPU
        // pohon proses 222%; dengan batas ini YouTube turun ke 720p, 153%.
        // @30: video 60 fps turun ke resolusi yang punya versi 30 fps.
        GLib.Functions.Setenv("WEBKIT_GST_VIDEO_DECODING_LIMIT", "1280x720@30", false);

        switch (Gambar)
        {
            case ModeGambar.Hemat:
                GLib.Functions.Setenv("WEBKIT_SKIA_ENABLE_CPU_RENDERING", "1", false);
                // Tanpa GL sink GStreamer, kecuali ada decoder H.264 hardware
                // (lihat PenguraiVideo). Terukur, YouTube VP9 1080p, buffer
                // GPU proses web: dengan GL sink 136–149 MB saat tab aktif
                // dan ±215 MB saat tab di latar (turun lagi saat dilihat);
                // tanpa 58–76 MB di semua keadaan. CPU pohon proses saat
                // video terlihat ±156% → ±164% satu inti. Video tetap tampil.
                // WEBKIT_GST_DMABUF_SINK_DISABLED tidak berpengaruh (sudah
                // dicoba). Tabel di atas diukur sebelum pengaturan ini.
                if (!PenguraiVideo.AdaH264)
                    GLib.Functions.Setenv("WEBKIT_GST_DISABLE_GL_SINK", "1", false);
                break;
            case ModeGambar.Cpu:
                // GTK ikut tanpa GL. Hanya aman kalau WebKit juga tanpa GPU:
                // halaman ber-GPU harus diunduh cairo lewat Vulkan, dan di
                // HD 4600 (driver hasvk) itu pernah segfault di detik.com.
                GLib.Functions.Setenv("GSK_RENDERER", "cairo", false);
                break;
        }
    }

    /// <summary>Paket apt yang pustakanya belum terpasang, atau null kalau lengkap.</summary>
    public static string? PaketHilang()
    {
        (string Pustaka, string Paket)[] perlu =
            [("libgtk-4.so.1", "libgtk-4-1"), ("libwebkitgtk-6.0.so.4", "libwebkitgtk-6.0-4")];
        foreach (var (pustaka, paket) in perlu)
            if (!NativeLibrary.TryLoad(pustaka, out _))
                return paket;
        return null;
    }

    /// <summary>
    /// Notifikasi desktop lewat notify-send, kalau ada. Dibuka dari menu,
    /// tulisan di stderr tidak terlihat siapa pun.
    /// </summary>
    public static void Beritahu(string judul, string isi)
    {
        var info = new ProcessStartInfo("notify-send") { UseShellExecute = false };
        info.ArgumentList.Add(judul);
        info.ArgumentList.Add(isi);
        try
        {
            using var proses = Process.Start(info);
            proses?.WaitForExit(2000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // notify-send tidak terpasang.
        }
    }

    public static long RamTotalMB() => Meminfo("MemTotal:") ?? 2048;

    /// <summary>
    /// RAM yang masih bisa dipakai tanpa swap (MemAvailable), setelah semua
    /// program lain di laptop.
    /// </summary>
    public static long RamTersediaMB() => Meminfo("MemAvailable:") ?? long.MaxValue;

    static long? Meminfo(string kunci)
    {
        foreach (var baris in File.ReadLines("/proc/meminfo"))
            if (baris.StartsWith(kunci, StringComparison.Ordinal))
                return long.Parse(baris.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) / 1024;
        return null;
    }
}
