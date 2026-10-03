using System.Diagnostics;

namespace KevinBrowser.Linux;

/// <summary>
/// Mematikan proses web WebKit yang yatim: diluncurkan untuk "prewarm" tapi
/// tidak akan pernah dipakai.
/// </summary>
/// <remarks>
/// Bug WebKitGTK (2.52; kodenya masih sama di cabang main): setelah swap
/// proses pertama, WebKit memanaskan satu proses cadangan tiap halaman
/// selesai dimuat. Saat cadangan itu mau dipakai, tryTakePrewarmedProcess()
/// mengeluarkannya dari daftar cadangan lalu menolaknya karena sandbox
/// bubblewrap aktif — prosesnya tidak dipakai, tidak dimatikan. Terukur:
/// +1 proses (~15 MB) tiap pindah situs, 5 proses setelah 6 situs.
///
/// Ciri proses yang belum pernah dipakai apa pun: tidak memetakan satu font
/// pun (proses halaman, bahkan about:blank dan dokumen gambar, minimal 1)
/// dan hanya punya 2 soket — belum tersambung ke proses jaringan, yang
/// dibutuhkan halaman maupun service worker (halaman: 4 soket). Proses muda
/// dilewati supaya tidak mendahului proses yang baru diluncurkan untuk halaman.
/// </remarks>
static class Pemulung
{
    const uint JedaDetik = 30;
    const double UmurMinimalDetik = 20;

    public static void Mulai() =>
        GLib.Functions.TimeoutAddSeconds(GLib.Constants.PRIORITY_LOW, JedaDetik, () =>
        {
            Sapu();
            return true;
        });

    static void Sapu()
    {
        var saya = Environment.ProcessId;
        var detikPerTick = 1.0 / 100;   // USER_HZ di Linux x86-64
        var uptime = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);

        var induk = new Dictionary<int, int>();
        var calon = new List<(int Pid, double Umur)>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid))
                continue;
            string stat;
            try { stat = File.ReadAllText(Path.Combine(dir, "stat")); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            // "pid (comm) state ppid …" — comm bisa berisi spasi/kurung, jadi
            // potong dari kurung tutup terakhir.
            var tutup = stat.LastIndexOf(')');
            var nama = stat[(stat.IndexOf('(') + 1)..tutup];
            var kolom = stat[(tutup + 2)..].Split(' ');
            induk[pid] = int.Parse(kolom[1]);
            if (nama == "WebKitWebProces")
                calon.Add((pid, uptime - long.Parse(kolom[19]) * detikPerTick));
        }

        foreach (var (pid, umur) in calon)
        {
            // Proses web ada di bawah bwrap → bwrap → kevin-browser.
            if (umur < UmurMinimalDetik || !Keturunan(pid, saya, induk))
                continue;
            try
            {
                if (PunyaFont(pid) || JumlahSoket(pid) > 2)
                    continue;
                Catat.Tulis($"proses web yatim dimatikan: pid {pid}, umur {umur:0} dtk");
                using var proses = Process.GetProcessById(pid);
                proses.Kill();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                // Proses sudah keluar di tengah jalan.
            }
        }
    }

    static bool Keturunan(int pid, int leluhur, Dictionary<int, int> induk)
    {
        for (var i = 0; i < 6 && induk.TryGetValue(pid, out var p); i++, pid = p)
            if (p == leluhur)
                return true;
        return false;
    }

    static bool PunyaFont(int pid)
    {
        foreach (var baris in File.ReadLines($"/proc/{pid}/maps"))
            if (baris.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                || baris.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                || baris.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
                || baris.EndsWith(".pfb", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static int JumlahSoket(int pid) =>
        Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd")
            .Count(fd => new FileInfo(fd).LinkTarget?.StartsWith("socket:", StringComparison.Ordinal) == true);
}
