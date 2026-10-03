using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser;

/// <summary>
/// kevin://riwayat: kunjungan per hari, terbaru di atas, dengan pencarian dan
/// tombol hapus per kunjungan. Judul berasal dari situs luar dan halaman ini
/// bisa menghapus data, jadi semua teksnya di-escape.
/// </summary>
static class HalamanRiwayat
{
    // Batas yang ditampilkan sekali buka; yang lebih lama ditemukan lewat pencarian.
    const int TampilMaks = 300;

    public static string Buat(Kueri kueri, ILayanan layanan)
    {
        var t = layanan.Preferensi.Teks;
        string? pesan = null;
        if (long.TryParse(kueri["hapus"], out var id) && TokenSekali.Pakai(kueri["token"]))
        {
            layanan.Riwayat.HapusSatu(id);
            pesan = t["Satu kunjungan dihapus dari riwayat.", "One visit removed from history."];
        }

        var cari = kueri["q"]?.Trim() ?? "";
        var daftar = layanan.Riwayat.Baca(cari);
        var token = TokenSekali.Buat();
        var zona = layanan.Waktu.LocalTimeZone;
        var hariIni = TimeZoneInfo.ConvertTime(layanan.Waktu.GetUtcNow(), zona).Date;

        var isi = new StringBuilder($"""
            <h1>{t["Riwayat", "History"]}</h1>
            <form class="cari kiri" action="{HalamanBawaan.Riwayat}" method="get">
              <input type="search" name="q" value="{HtmlEncode(cari)}" placeholder="{t["Cari judul atau alamat…", "Search titles or addresses…"]}" aria-label="{t["Cari di riwayat", "Search history"]}">
              <button type="submit">{t["Cari", "Search"]}</button>
            </form>
            {HalamanPengaturan.Pesan(pesan)}
            <p class="catatan">{Ringkasan(t, cari, daftar.Count)} <a href="{HalamanBawaan.Pengaturan}">{t["Hapus data penjelajahan", "Clear browsing data"]}</a></p>
            """);

        if (daftar.Count == 0)
            isi.Append(cari.Length > 0
                ? $"<p>{t["Tidak ada yang cocok.", "No matches."]}</p>"
                : $"<p>{t["Belum ada riwayat.", "No history yet."]}</p>");

        DateTime? hari = null;
        foreach (var kunjungan in daftar.Take(TampilMaks))
        {
            var waktu = TimeZoneInfo.ConvertTime(kunjungan.Waktu, zona);
            if (waktu.Date != hari)
            {
                if (hari is not null)
                    isi.Append("</ul>");
                hari = waktu.Date;
                isi.Append($"<h2>{LabelHari(t, waktu.Date, hariIni)}</h2><ul class=\"riwayat\">");
            }
            isi.Append(Baris(t, kunjungan, waktu, token, cari));
        }
        if (hari is not null)
            isi.Append("</ul>");

        if (daftar.Count > TampilMaks)
            isi.Append($"<p class=\"catatan\">{t[$"Menampilkan {TampilMaks} kunjungan terbaru. Pakai pencarian untuk menemukan yang lebih lama.",
                $"Showing the {TampilMaks} most recent visits. Use search to find older ones."]}</p>");
        return isi.ToString();
    }

    static string Ringkasan(Teks t, string cari, int jumlah) => cari.Length > 0
        ? t[$"{jumlah} kunjungan cocok dengan “{HtmlEncode(cari)}”.", $"{jumlah} visits match “{HtmlEncode(cari)}”."]
        : t[$"{jumlah} kunjungan, disimpan paling lama {Riwayat.Umur.Days} hari.", $"{jumlah} visits, kept for up to {Riwayat.Umur.Days} days."];

    static string Baris(Teks t, Kunjungan k, DateTimeOffset waktu, string token, string cari)
    {
        var judul = k.Judul.Length > 0 ? k.Judul : k.Uri;
        var host = Uri.TryCreate(k.Uri, UriKind.Absolute, out var u) ? u.Host : "";
        var hapus = $"{HalamanBawaan.Riwayat}?hapus={k.Id}&token={token}"
            + (cari.Length > 0 ? "&q=" + Uri.EscapeDataString(cari) : "");
        var labelHapus = t["Hapus dari riwayat", "Remove from history"];

        // Hanya http(s) yang jadi tautan, walau berkasnya diubah tangan.
        var tautan = Riwayat.LayakDicatat(k.Uri)
            ? $"""<a class="judul" href="{HtmlEncode(k.Uri)}" title="{HtmlEncode(k.Uri)}">{HtmlEncode(judul)}</a>"""
            : $"""<span class="judul">{HtmlEncode(judul)}</span>""";

        return $"""<li><span class="jam">{t.Jam(waktu)}</span>{tautan}<span class="domain">{HtmlEncode(host)}</span>""" +
            $"""<a class="hapus" href="{HtmlEncode(hapus)}" title="{labelHapus}" aria-label="{labelHapus}">×</a></li>""";
    }

    static string LabelHari(Teks t, DateTime tanggal, DateTime hariIni) =>
        tanggal == hariIni ? t["Hari ini", "Today"]
        : tanggal == hariIni.AddDays(-1) ? t["Kemarin", "Yesterday"]
        : t.Tanggal(tanggal);
}
