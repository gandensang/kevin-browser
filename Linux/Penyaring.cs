using System.Diagnostics;
using System.Text.Json;

namespace KevinBrowser.Linux;

/// <summary>
/// Pemblokir iklan dan pelacak: daftar filter Adblock diunduh seminggu
/// sekali, diubah ke aturan WebKit, dikompilasi WebKit, lalu dipasang ke
/// UserContentManager setiap tab. Pembuang parameter pelacak di alamat
/// (<see cref="PembersihAlamat"/>) terpisah, tetapi ikut saklar <see cref="Nyala"/>.
/// </summary>
/// <remarks>
/// Unduh, ubah, dan kompilasi berjalan di PROSES ANAK (binary ini sendiri
/// dengan <see cref="ArgPerbarui"/>), berprioritas rendah. Terukur: WebKit
/// memakai ~250 MB tambahan selama beberapa detik untuk mengompilasi
/// EasyList; di proses anak, memori itu kembali ke sistem begitu prosesnya
/// selesai. Proses browser hanya memuat hasil jadinya: WebKit memetakan
/// berkasnya dari disk (mmap, dibagi semua proses web), bukan menyalinnya.
/// Tiap daftar jadi filter sendiri: puncak kompilasinya lebih kecil, dan
/// satu daftar yang gagal tidak mematikan yang lain.
/// </remarks>
static class Penyaring
{
    public const string ArgPerbarui = "--perbarui-penyaring";

    /// <summary>
    /// KEVIN_BROWSER_PENYARING=0 mematikan semuanya, termasuk pembuang
    /// parameter pelacak (untuk membandingkan).
    /// </summary>
    public static bool Nyala { get; } = Environment.GetEnvironmentVariable("KEVIN_BROWSER_PENYARING") != "0";

    static string folder = "";
    static WebKit.UserContentFilterStore? gudang;
    static readonly Dictionary<string, IntPtr> filter = [];

    public static bool SedangMemperbarui { get; private set; }

    /// <summary>Filter berganti (selesai diperbarui): pasang ulang ke tab yang hidup.</summary>
    public static event Action? Berubah;

    public static StatusPenyaring? Status =>
        !Nyala ? null : new StatusPenyaring(SedangMemperbarui, InfoFilter.Baca(BerkasInfo(folder)), filter.Count, Penyimpanan.UkuranFolder(folder));

    /// <summary>WebKit yang sedang dipakai, mis. "2.52.6".</summary>
    static string VersiWebKit =>
        $"{WebKit.Functions.GetMajorVersion()}.{WebKit.Functions.GetMinorVersion()}.{WebKit.Functions.GetMicroVersion()}";

    public static void Mulai(string folderData)
    {
        folder = Path.Combine(folderData, "penyaring");
        if (!Nyala)
            return;
        Directory.CreateDirectory(folder);
        gudang = WebKit.UserContentFilterStore.New(Path.Combine(folder, "terkompilasi"));

        // Hasil kompilasi WebKit versi lain tidak dimuat di proses ini. Kalau
        // format berkasnya berubah, WebKit mengompilasinya ulang di proses
        // yang memuatnya. Terukur, nomor format ketiga berkas diturunkan satu:
        // proses browser mengompilasi ulang semuanya sendiri, memori anonimnya
        // naik dari ±70 MB ke puncak ±290 MB selama ±10 detik dan sesudahnya
        // tetap ±45 MB lebih besar. Jadi proses anak yang mengerjakannya;
        // selama itu halaman belum tersaring.
        var info = InfoFilter.Baca(BerkasInfo(folder));
        if (info is not null && info.VersiWebKit != VersiWebKit)
        {
            Catat.Tulis($"penyaring dari WebKit {info.VersiWebKit ?? "lama"}, sekarang {VersiWebKit}: dikompilasi ulang di proses anak");
            PerbaruiBilaPerlu(null);
            return;
        }

        // Ditunggu di sini, sebelum jendela dan tab pertama dibuat, supaya
        // halaman pertama ikut tersaring. Terukur 12–26 ms: berkasnya hanya
        // dipetakan. Kalau tidak ditunggu, lanjutan await (prioritas idle di
        // GirCore) baru jalan setelah GTK selesai menggambar jendela pertama:
        // terukur 1,2 detik, dan halaman pertama lolos dari penyaring.
        var muat = MuatSemua();
        if (!JalankanLoopSampai(muat, TimeSpan.FromSeconds(3)))
            Console.Error.WriteLine("[kevin-browser] penyaring belum termuat setelah 3 detik; jalan terus tanpa menunggu");
        PerbaruiBilaPerlu(muat);
    }

    public static void PasangKe(WebKit.UserContentManager pengelola)
    {
        foreach (var f in filter.Values)
            Asli.webkit_user_content_manager_add_filter(pengelola.Handle.DangerousGetHandle(), f);
    }

    // muat null: tidak ada yang dimuat, langsung perbarui.
    static async void PerbaruiBilaPerlu(Task<bool>? muat)
    {
        try
        {
            var lengkap = muat is not null && await muat;
            var info = InfoFilter.Baca(BerkasInfo(folder));
            if (!lengkap || info is null || info.Diperbarui < DateTimeOffset.UtcNow - DaftarFilter.UmurMaks)
                await PerbaruiLewatProsesAnak();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[kevin-browser] penyaring: {e}");
        }
    }

    // true kalau semua filter yang diharapkan berhasil dimuat.
    static async Task<bool> MuatSemua()
    {
        // Sekaligus: tiap await menunggu satu giliran loop GTK.
        var id = DaftarFilter.Semua.Select(d => d.Id).ToArray();
        var hasil = await Task.WhenAll(id.Select(i => Asli.MuatFilter(gudang!.Handle.DangerousGetHandle(), i)));
        var lengkap = true;
        for (var i = 0; i < id.Length; i++)
        {
            var baru = hasil[i].Filter;
            if (baru == IntPtr.Zero)
            {
                lengkap = false;   // belum ada, atau dikompilasi WebKit versi lain
                continue;
            }
            if (filter.Remove(id[i], out var lama))
                Asli.webkit_user_content_filter_unref(lama);
            filter[id[i]] = baru;
        }
        Catat.Tulis($"penyaring dimuat: {string.Join(", ", filter.Keys)}");
        Berubah?.Invoke();
        return lengkap;
    }

    // Menjalankan loop GLib (callback WebKit dan lanjutan await datang lewat
    // loop itu) sampai tugas selesai atau batas waktunya habis.
    static bool JalankanLoopSampai(Task tugas, TimeSpan batas)
    {
        var habis = false;
        var jam = GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, (uint)batas.TotalMilliseconds, () =>
        {
            habis = true;
            return false;
        });
        var konteks = GLib.MainContext.Default();
        while (!tugas.IsCompleted && !habis)
            konteks.Iteration(true);
        if (!habis)
            GLib.Functions.SourceRemove(jam);
        return tugas.IsCompleted;
    }

    static async Task PerbaruiLewatProsesAnak()
    {
        if (SedangMemperbarui || Environment.ProcessPath is not { } diriSendiri)
            return;
        SedangMemperbarui = true;
        try
        {
            var info = new ProcessStartInfo(diriSendiri) { UseShellExecute = false };
            info.ArgumentList.Add(ArgPerbarui);
            info.ArgumentList.Add(folder);
            using var anak = Process.Start(info)!;
            try
            {
                anak.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Sudah selesai duluan, atau tidak diizinkan; tidak penting.
            }
            await anak.WaitForExitAsync();
            Catat.Tulis($"pembaruan penyaring selesai, kode keluar {anak.ExitCode}");
        }
        finally
        {
            SedangMemperbarui = false;
        }
        // Proses anak yang gagal di tengah jalan bisa meninggalkan berkas
        // dari WebKit lama (lihat Mulai).
        if (InfoFilter.Baca(BerkasInfo(folder))?.VersiWebKit == VersiWebKit)
            await MuatSemua();
        else
            Console.Error.WriteLine("[kevin-browser] penyaring belum dikompilasi ulang untuk WebKit ini; tidak dimuat");
    }

    // ── Proses anak ─────────────────────────────────────────────────────

    /// <summary>
    /// Isi proses anak: tanpa GTK dan tanpa jendela. Daftar yang gagal
    /// diunduh tetap memakai hasil kompilasi lamanya.
    /// </summary>
    public static int Perbarui(string folderPenyaring)
    {
        // Jaring pengaman: apa pun yang macet (jaringan, WebKit), proses anak
        // tidak boleh tinggal di memori setelah browsernya ditutup.
        using var batas = new Timer(_ => Environment.Exit(2), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);

        Directory.CreateDirectory(folderPenyaring);
        // `using` juga menjaga pembungkusnya tetap hidup: kalau GC memungutnya,
        // gudang aslinya ikut dibebaskan dan WebKit menolak kompilasi berikutnya
        // tanpa pernah memanggil callback (terjadi di build AOT).
        using var gudangAnak = WebKit.UserContentFilterStore.New(Path.Combine(folderPenyaring, "terkompilasi"));
        var json = Path.Combine(folderPenyaring, "sementara.json");
        var aturan = InfoFilter.Baca(BerkasInfo(folderPenyaring))?.Aturan ?? [];

        using var sesi = Soup.Session.New();
        sesi.SetTimeout(60);
        sesi.SetUserAgent("kevin-browser");
        var baru = new HashSet<string>();
        foreach (var (id, _, _, url) in DaftarFilter.Semua)
        {
            var mentah = Path.Combine(folderPenyaring, id + ".txt");
            if (!Unduh(sesi, url, mentah))
                continue;

            var jam = Stopwatch.StartNew();
            HasilKonversi? hasil = null;
            TulisJson(json, j =>
            {
                var konverter = new KonverterAdblock(j);
                konverter.Tambah(File.ReadLines(mentah));
                hasil = konverter.Selesai();
                return hasil.Aturan;
            });
            File.Delete(mentah);
            var waktuUbah = jam.ElapsedMilliseconds;
            jam.Restart();
            if (Kompilasi(gudangAnak, id, json))
            {
                aturan[id] = hasil!.Aturan;
                baru.Add(id);
                Console.Error.WriteLine($"[kevin-browser] {id}: {hasil.Aturan} aturan, ubah {waktuUbah} ms, kompilasi {jam.ElapsedMilliseconds} ms");
            }
        }
        File.Delete(json);

        // Daftar yang gagal diunduh tetap memakai hasil lamanya. Kalau itu
        // dari WebKit versi lain, memuatnya di sini membuat WebKit
        // mengompilasinya ulang dari sumber JSON yang ikut tersimpan di
        // berkasnya (terlihat: berkas ditulis ulang dengan nomor format baru),
        // di proses anak ini, bukan di proses browser.
        foreach (var (id, _, _, _) in DaftarFilter.Semua)
            if (!baru.Contains(id) && !Muat(gudangAnak, id))
                aturan.Remove(id);
        new InfoFilter(DateTimeOffset.UtcNow, aturan, VersiWebKit).Tulis(BerkasInfo(folderPenyaring));
        return 0;
    }

    static int TulisJson(string berkas, Func<Utf8JsonWriter, int> isi)
    {
        using var aliran = File.Create(berkas);
        using var json = new Utf8JsonWriter(aliran);
        json.WriteStartArray();
        var jumlah = isi(json);
        json.WriteEndArray();
        return jumlah;
    }

    static bool Unduh(Soup.Session sesi, string url, string tujuan)
    {
        try
        {
            using var pesan = Soup.Message.New("GET", url);
            if (pesan is null)
                return false;
            using var isi = sesi.SendAndRead(pesan, null);
            if (pesan.GetStatus() != Soup.Status.Ok)
            {
                Console.Error.WriteLine($"[kevin-browser] unduh {url}: status {pesan.GetStatus()}");
                return false;
            }
            Asli.TulisBytes(isi.Handle.DangerousGetHandle(), tujuan);
            return true;
        }
        catch (GLib.GException e)
        {
            Console.Error.WriteLine($"[kevin-browser] unduh {url}: {e.Message}");
            return false;
        }
    }

    static bool Kompilasi(WebKit.UserContentFilterStore gudangAnak, string id, string json) =>
        Berhasil("kompilasi", id, Tunggu(Asli.SimpanFilter(gudangAnak.Handle.DangerousGetHandle(), id, json)));

    static bool Muat(WebKit.UserContentFilterStore gudangAnak, string id) =>
        Berhasil("muat", id, Tunggu(Asli.MuatFilter(gudangAnak.Handle.DangerousGetHandle(), id)));

    static bool Berhasil(string kerja, string id, (IntPtr Filter, string? Galat) hasil)
    {
        if (hasil.Filter == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[kevin-browser] {kerja} {id} gagal: {hasil.Galat}");
            return false;
        }
        Asli.webkit_user_content_filter_unref(hasil.Filter);
        return true;
    }

    // Menunggu tugas WebKit sambil menjalankan loop GLib (callback-nya datang
    // lewat loop itu).
    static T Tunggu<T>(Task<T> tugas)
    {
        var konteks = GLib.MainContext.Default();
        while (!tugas.IsCompleted)
            konteks.Iteration(true);
        return tugas.Result;
    }

    static string BerkasInfo(string folderPenyaring) => Path.Combine(folderPenyaring, "info.json");
}
