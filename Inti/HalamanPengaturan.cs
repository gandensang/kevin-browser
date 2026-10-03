using static System.Net.WebUtility;

namespace KevinBrowser;

/// <summary>
/// kevin://pengaturan: bahasa, isi penyimpanan, dan hapus data penjelajahan.
/// Semua lewat formulir GET, tanpa JavaScript. Menghapus tiga langkah:
/// pilih → konfirmasi (dengan token sekali pakai) → hapus. Mengganti bahasa
/// juga memakai token sekali pakai.
/// </summary>
static class HalamanPengaturan
{
    static readonly (string Kode, string Label, string LabelInggris, TimeSpan? Rentang)[] PilihanRentang =
    [
        ("jam", "1 jam terakhir", "the last hour", TimeSpan.FromHours(1)),
        ("hari", "24 jam terakhir", "the last 24 hours", TimeSpan.FromDays(1)),
        ("semua", "semua waktu", "all time", null),
    ];

    static readonly (string Kode, JenisData Jenis, string Label, string LabelInggris, string Singkat, string SingkatInggris,
        string? Keterangan, string? KeteranganInggris, bool Dicentang)[] PilihanJenis =
    [
        ("riwayat", JenisData.Riwayat, "Riwayat", "History", "riwayat", "history", null, null, true),
        ("cache", JenisData.Cache, "Cache", "Cache", "cache", "cache",
            "salinan halaman untuk mempercepat", "copies of pages to load them faster", true),
        ("cookie", JenisData.Cookie, "Cookie dan login", "Cookies and logins", "cookie", "cookies",
            "keluar dari semua situs", "signs you out of all sites", false),
        ("situs", JenisData.DataSitus, "Data situs lain", "Other site data", "data situs", "site data",
            "penyimpanan dan service worker", "storage and service workers", false),
    ];

    public static async Task<string> Buat(Kueri kueri, ILayanan layanan)
    {
        // Bahasa diganti dulu, supaya seluruh halaman hasilnya sudah dalam
        // bahasa baru.
        if (kueri["bahasa"] is "id" or "en" && TokenSekali.Pakai(kueri["token"]))
        {
            layanan.Preferensi.Bahasa = kueri["bahasa"] == "en" ? Bahasa.Inggris : Bahasa.Indonesia;
            return Utama(layanan, layanan.Preferensi.Teks["Bahasa diganti ke Bahasa Indonesia.", "Language changed to English."]);
        }

        var t = layanan.Preferensi.Teks;
        var langkah = kueri["langkah"];
        var rentang = PilihanRentang.FirstOrDefault(p => p.Kode == kueri["rentang"]);
        var jenis = kueri.Semua("jenis")
            .Select(kode => PilihanJenis.FirstOrDefault(p => p.Kode == kode).Jenis)
            .Aggregate(JenisData.Kosong, (a, b) => a | b);

        if (langkah is "konfirmasi" or "hapus" && (rentang.Kode is null || jenis == JenisData.Kosong))
            return Utama(layanan, t["Pilih dulu data yang ingin dihapus.", "Choose the data to clear first."]);

        var labelRentang = t[rentang.Label, rentang.LabelInggris];
        if (langkah == "konfirmasi")
            return Konfirmasi(t, rentang.Kode!, labelRentang, jenis);

        if (langkah == "hapus")
        {
            if (!TokenSekali.Pakai(kueri["token"]))
                return Utama(layanan, t["Permintaan ini sudah dipakai atau kedaluwarsa. Tidak ada yang dihapus.",
                    "This request was already used or has expired. Nothing was cleared."]);

            if (jenis.HasFlag(JenisData.Riwayat))
                layanan.Riwayat.Hapus(rentang.Rentang is { } r ? layanan.Waktu.GetUtcNow() - r : null);
            if ((jenis & ~JenisData.Riwayat) is var lainnya and not JenisData.Kosong)
                await layanan.HapusData(lainnya, rentang.Rentang);
            return Utama(layanan, t[$"Selesai: {Uraian(t, jenis)} dari {labelRentang} sudah dihapus.",
                $"Done: {Uraian(t, jenis)} from {labelRentang} cleared."]);
        }

        return Utama(layanan, null);
    }

    static string Utama(ILayanan layanan, string? pesan)
    {
        var t = layanan.Preferensi.Teks;
        var rentang = string.Concat(PilihanRentang.Select((p, i) =>
            $"""<label><input type="radio" name="rentang" value="{p.Kode}"{(i == 0 ? " checked" : "")}> {HtmlEncode(Kalimat(t[p.Label, p.LabelInggris]))}</label>"""));
        var jenis = string.Concat(PilihanJenis.Select(p =>
            $"""<label><input type="checkbox" name="jenis" value="{p.Kode}"{(p.Dicentang ? " checked" : "")}> {HtmlEncode(t[p.Label, p.LabelInggris])}""" +
            (p.Keterangan is { } k ? $""" <span class="catatan">· {HtmlEncode(t[k, p.KeteranganInggris!])}</span>""" : "") + "</label>"));

        return $"""
            <h1>{t["Pengaturan", "Settings"]}</h1>
            <p class="pembuka">{t["Bahasa, riwayat, dan data penjelajahan yang tersimpan di laptop ini.",
                "Language, history, and browsing data stored on this laptop."]}</p>
            {Pesan(pesan)}
            {PilihBahasa(t)}

            <h2>{t["Riwayat", "History"]}</h2>
            <p>{t[$"{layanan.Riwayat.Jumlah()} kunjungan tersimpan. Kunjungan yang lebih tua dari {Riwayat.Umur.Days} hari dibuang otomatis.",
                $"{layanan.Riwayat.Jumlah()} visits saved. Visits older than {Riwayat.Umur.Days} days are removed automatically."]}</p>
            <p><a class="tombol" href="{HalamanBawaan.Riwayat}">{t["Lihat riwayat", "View history"]}</a>
            <span class="catatan"><kbd>Ctrl</kbd>+<kbd>H</kbd></span></p>

            <h2>{t["Privasi", "Privacy"]}</h2>
            {Privasi(layanan)}

            <h2>{t["Penyimpanan", "Storage"]}</h2>
            <table>
              <tbody>
                <tr><td>{t["Cache", "Cache"]}</td><td class="angka">{t.Ukuran(layanan.UkuranCache())}</td></tr>
                <tr><td>{t["Cookie, login, dan data situs", "Cookies, logins, and site data"]}</td><td class="angka">{t.Ukuran(layanan.UkuranDataSitus())}</td></tr>
                {BarisPenyaring(t, layanan.Penyaring)}
              </tbody>
            </table>

            <h2>{t["Hapus data penjelajahan", "Clear browsing data"]}</h2>
            <form action="{HalamanBawaan.Pengaturan}" method="get">
              <input type="hidden" name="langkah" value="konfirmasi">
              <fieldset><legend>{t["Dari", "From"]}</legend>{rentang}</fieldset>
              <fieldset><legend>{t["Yang dihapus", "What to clear"]}</legend>{jenis}</fieldset>
              <button class="tombol utama" type="submit">{t["Hapus…", "Clear…"]}</button>
            </form>
            """;
    }

    // Judul dan nama pilihannya selalu dua bahasa, jadi tetap bisa ditemukan
    // oleh orang yang tidak mengerti bahasa yang sedang dipakai.
    static string PilihBahasa(Teks t) =>
        $"""
        <h2>Bahasa · Language</h2>
        <form class="bahasa" action="{HalamanBawaan.Pengaturan}" method="get">
          <input type="hidden" name="token" value="{TokenSekali.Buat()}">
          <label><input type="radio" name="bahasa" value="id"{(t.Inggris ? "" : " checked")}> Bahasa Indonesia</label>
          <label><input type="radio" name="bahasa" value="en"{(t.Inggris ? " checked" : "")}> English</label>
          <button class="tombol" type="submit">{t["Simpan", "Save"]}</button>
        </form>
        """;

    static string BarisPenyaring(Teks t, StatusPenyaring? status) => status is null ? "" :
        $"""<tr><td>{t["Daftar pemblokir iklan dan pelacak", "Ad and tracker block lists"]}</td><td class="angka">{t.Ukuran(status.Ukuran)}</td></tr>""";

    static string Privasi(ILayanan layanan)
    {
        var t = layanan.Preferensi.Teks;
        var cookie = t["Cookie pihak ketiga, alat utama pelacak lintas situs, selalu ditolak.",
            "Third-party cookies, the main tool for tracking you across sites, are always blocked."];
        if (layanan.Penyaring is not { } status)
            return $"<p>{t["Pemblokir iklan dan pelacak dimatikan.", "The ad and tracker blocker is turned off."]} {cookie}</p>";

        string blokir;
        if (status.Info is { } info && status.FilterAktif > 0)
        {
            var dipakai = DaftarFilter.Semua.Where(d => info.Aturan.ContainsKey(d.Id)).ToList();
            var jumlah = t.Angka(dipakai.Sum(d => info.Aturan[d.Id]));
            var daftar = HtmlEncode(string.Join(", ", dipakai.Select(d => t[d.Nama, d.NamaInggris])));
            var tanggal = t.Tanggal(TimeZoneInfo.ConvertTime(info.Diperbarui, layanan.Waktu.LocalTimeZone).Date);
            blokir = t[$"Iklan dan pelacak diblokir: {jumlah} aturan dari daftar {daftar}, diperbarui {tanggal}.",
                $"Ads and trackers are blocked: {jumlah} rules from the lists {daftar}, updated {tanggal}."];
        }
        else
            blokir = t["Daftar pemblokir iklan dan pelacak belum siap.", "The ad and tracker block lists are not ready yet."];
        if (status.SedangMemperbarui)
            blokir += t[" Sedang mengunduh daftar terbaru…", " Downloading the latest lists…"];

        var parameter = t[" Parameter pelacak di alamat (<code>utm_…</code>, <code>fbclid</code>, <code>gclid</code>, …) dibuang.",
            " Tracking parameters in addresses (<code>utm_…</code>, <code>fbclid</code>, <code>gclid</code>, …) are removed."];
        return $"<p>{blokir}{parameter} {cookie}</p>";
    }

    static string Konfirmasi(Teks t, string kodeRentang, string labelRentang, JenisData jenis)
    {
        var tersembunyi = string.Concat(PilihanJenis.Where(p => jenis.HasFlag(p.Jenis)).Select(p =>
            $"""<input type="hidden" name="jenis" value="{p.Kode}">"""));
        var peringatan = jenis.HasFlag(JenisData.Cookie)
            ? t[" Anda akan keluar dari semua situs.", " You will be signed out of all sites."]
            : "";

        return $"""
            <h1>{t["Hapus data?", "Clear data?"]}</h1>
            <p class="pembuka">{HtmlEncode(Kalimat(Uraian(t, jenis)))} {t["dari", "from"]} {HtmlEncode(labelRentang)} {t["akan dihapus.", "will be cleared."]}{peringatan}</p>
            <form class="tombol-tombol" action="{HalamanBawaan.Pengaturan}" method="get">
              <input type="hidden" name="langkah" value="hapus">
              <input type="hidden" name="rentang" value="{kodeRentang}">
              {tersembunyi}
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <button class="tombol bahaya" type="submit">{t["Ya, hapus", "Yes, clear"]}</button>
              <a class="tombol" href="{HalamanBawaan.Pengaturan}">{t["Batal", "Cancel"]}</a>
            </form>
            """;
    }

    internal static string Pesan(string? pesan) =>
        pesan is null ? "" : $"""<p class="pesan" role="status">{HtmlEncode(pesan)}</p>""";

    // "riwayat, cache, dan cookie" / "history, cache, and cookies"
    static string Uraian(Teks t, JenisData jenis)
    {
        var bagian = PilihanJenis.Where(p => jenis.HasFlag(p.Jenis)).Select(p => t[p.Singkat, p.SingkatInggris]).ToArray();
        return bagian.Length == 1 ? bagian[0]
            : string.Join(", ", bagian[..^1]) + (bagian.Length > 2 ? "," : "") + t[" dan ", " and "] + bagian[^1];
    }

    static string Kalimat(string teks) => char.ToUpperInvariant(teks[0]) + teks[1..];
}
