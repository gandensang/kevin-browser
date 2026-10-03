using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KevinBrowser;

namespace Uji;

public sealed partial class UjiPengaturan : IDisposable
{
    static readonly DateTimeOffset T0 = LayananPalsu.Sekarang;

    readonly LayananPalsu layanan = new();

    public void Dispose() => layanan.Dispose();

    async Task<string> Html(string uri)
    {
        var (isi, _) = await HalamanBawaan.Ambil(uri, layanan);
        return Encoding.UTF8.GetString(isi);
    }

    [GeneratedRegex("name=\"token\" value=\"([0-9A-F]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex("href=\"(kevin://riwayat\\?hapus=[^\"]+)\"")]
    private static partial Regex TautanHapus();

    const string KonfirmasiRiwayatDanCookie =
        "kevin://pengaturan?langkah=konfirmasi&rentang=jam&jenis=riwayat&jenis=cookie";

    [Fact]
    public async Task MenampilkanJumlahRiwayatDanUkuran()
    {
        layanan.Riwayat.Catat("https://a.id/", "A", T0);
        layanan.Riwayat.Catat("https://b.id/", "B", T0);
        var html = await Html("kevin://pengaturan");
        Assert.Contains("2 kunjungan tersimpan", html);
        Assert.Contains("76 MB", html);
        Assert.Contains("3,0 MB", html);
    }

    [Fact]
    public async Task KonfirmasiMenyebutYangAkanDihapus()
    {
        var html = await Html(KonfirmasiRiwayatDanCookie);
        Assert.Contains("Riwayat dan cookie dari 1 jam terakhir akan dihapus. Anda akan keluar dari semua situs.", html);
        Assert.Matches(Token(), html);
        Assert.Empty(layanan.Dihapus);
    }

    [Fact]
    public async Task HapusDenganTokenSah()
    {
        layanan.Riwayat.Catat("https://lama.id/", "lama", T0.AddHours(-2));
        layanan.Riwayat.Catat("https://baru.id/", "baru", T0.AddMinutes(-5));
        var token = Token().Match(await Html(KonfirmasiRiwayatDanCookie)).Groups[1].Value;

        var html = await Html($"kevin://pengaturan?langkah=hapus&rentang=jam&jenis=riwayat&jenis=cookie&token={token}");

        Assert.Contains("Selesai: riwayat dan cookie dari 1 jam terakhir sudah dihapus.", html);
        Assert.Equal([(JenisData.Cookie, (TimeSpan?)TimeSpan.FromHours(1))], layanan.Dihapus);
        Assert.Equal(["lama"], layanan.Riwayat.Baca().Select(k => k.Judul));
    }

    [Fact]
    public async Task TokenTidakBisaDipakaiDuaKali()
    {
        var token = Token().Match(await Html(KonfirmasiRiwayatDanCookie)).Groups[1].Value;
        var hapus = $"kevin://pengaturan?langkah=hapus&rentang=semua&jenis=cache&token={token}";

        await Html(hapus);
        var kedua = await Html(hapus);

        Assert.Contains("sudah dipakai atau kedaluwarsa", kedua);
        Assert.Single(layanan.Dihapus);
    }

    [Fact]
    public async Task TokenKaranganDitolak()
    {
        layanan.Riwayat.Catat("https://a.id/", "A", T0);
        var html = await Html("kevin://pengaturan?langkah=hapus&rentang=semua&jenis=riwayat&jenis=cookie&token=ABCDEF");
        Assert.Contains("Tidak ada yang dihapus", html);
        Assert.Empty(layanan.Dihapus);
        Assert.Equal(1, layanan.Riwayat.Jumlah());
    }

    [Fact]
    public async Task TanpaPilihanTidakAdaKonfirmasi() =>
        Assert.Contains("Pilih dulu data yang ingin dihapus.", await Html("kevin://pengaturan?langkah=konfirmasi&rentang=jam"));

    [Fact]
    public async Task RiwayatDikelompokkanPerHari()
    {
        layanan.Riwayat.Catat("https://a.id/", "Hari ini", T0.AddHours(-1));
        layanan.Riwayat.Catat("https://b.id/", "Kemarin", T0.AddDays(-1));
        layanan.Riwayat.Catat("https://c.id/", "Senin", T0.AddDays(-3));
        var html = await Html("kevin://riwayat");
        Assert.Contains("<h2>Hari ini</h2>", html);
        Assert.Contains("<h2>Kemarin</h2>", html);
        Assert.Contains("<h2>Senin, 28 September 2026</h2>", html);
        Assert.Contains("<span class=\"jam\">08.00</span>", html);
    }

    [Fact]
    public async Task JudulDariSitusDiEscape()
    {
        layanan.Riwayat.Catat("https://jahat.id/", "<img src=x onerror=alert(1)>", T0);
        var html = await Html("kevin://riwayat");
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<img src=x", html);
    }

    [Fact]
    public async Task HanyaHttpYangJadiTautan()
    {
        // Berkas riwayat yang diubah tangan.
        File.WriteAllText(layanan.Riwayat.Berkas, $"{T0.ToUnixTimeMilliseconds()}\tjavascript:alert(1)\tjebakan\n");
        var html = await Html("kevin://riwayat");
        Assert.Contains("jebakan", html);
        Assert.DoesNotContain("href=\"javascript:", html);
    }

    [Fact]
    public async Task HapusSatuKunjunganLewatTautannya()
    {
        layanan.Riwayat.Catat("https://a.id/", "A", T0.AddSeconds(-1));
        layanan.Riwayat.Catat("https://b.id/", "B", T0);
        var tautan = WebUtility.HtmlDecode(TautanHapus().Match(await Html("kevin://riwayat")).Groups[1].Value);

        var html = await Html(tautan);

        Assert.Contains("Satu kunjungan dihapus dari riwayat.", html);
        Assert.Equal(["A"], layanan.Riwayat.Baca().Select(k => k.Judul));
    }

    [Fact]
    public async Task CariDiRiwayat()
    {
        layanan.Riwayat.Catat("https://lichess.org/", "Main catur", T0.AddSeconds(-1));
        layanan.Riwayat.Catat("https://detik.com/", "Berita", T0);
        var html = await Html("kevin://riwayat?q=catur");
        Assert.Contains("Main catur", html);
        Assert.DoesNotContain("Berita", html);
        Assert.Contains("1 kunjungan cocok dengan “catur”.", html);
    }

    [Fact]
    public async Task PrivasiMenampilkanDaftarPemblokir()
    {
        var info = new InfoFilter(T0.AddDays(-2), new() { ["easylist"] = 52674, ["abpindo"] = 4225, ["lama"] = 9 });
        layanan.Penyaring = new StatusPenyaring(SedangMemperbarui: false, info, FilterAktif: 2, Ukuran: 59L * 1024 * 1024);
        var html = await Html("kevin://pengaturan");
        Assert.Contains("<td>Daftar pemblokir iklan dan pelacak</td><td class=\"angka\">59 MB</td>", html);
        // Id yang tidak ada lagi di DaftarFilter tidak ikut dihitung.
        Assert.Contains("Iklan dan pelacak diblokir: 56.899 aturan dari daftar iklan (EasyList), "
            + "iklan situs Indonesia (ABPindo), diperbarui Selasa, 29 September 2026.", html);
        Assert.Contains("<code>fbclid</code>", html);
    }

    [Fact]
    public async Task GantiBahasaKeInggrisDenganToken()
    {
        var token = Token().Match(await Html("kevin://pengaturan")).Groups[1].Value;

        var html = await Html($"kevin://pengaturan?bahasa=en&token={token}");

        // Seluruh halaman hasilnya, termasuk kerangka dan menu, sudah Inggris.
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("Language changed to English.", html);
        Assert.Contains("<h1>Settings</h1>", html);
        Assert.Contains("<a href=\"kevin://pengaturan\" aria-current=\"page\">Settings</a>", html);
        Assert.Contains("value=\"en\" checked", html);
        Assert.Equal(Bahasa.Inggris, new Preferensi(layanan.Preferensi.Berkas).Bahasa);

        var lagi = await Html($"kevin://pengaturan?bahasa=id&token={token}");   // token yang sama
        Assert.DoesNotContain("Bahasa diganti", lagi);
        Assert.Equal(Bahasa.Inggris, layanan.Preferensi.Bahasa);
    }

    [Fact]
    public async Task GantiBahasaTanpaTokenSahDiabaikan()
    {
        var html = await Html("kevin://pengaturan?bahasa=en&token=ABCDEF");
        Assert.Contains("<html lang=\"id\">", html);
        Assert.Equal(Bahasa.Indonesia, layanan.Preferensi.Bahasa);
    }

    [Fact]
    public async Task PengaturanDalamBahasaInggris()
    {
        layanan.Preferensi.Bahasa = Bahasa.Inggris;
        layanan.Riwayat.Catat("https://a.id/", "A", T0);
        layanan.Riwayat.Catat("https://b.id/", "B", T0);
        var info = new InfoFilter(T0.AddDays(-2), new() { ["easylist"] = 52674, ["abpindo"] = 4225 });
        layanan.Penyaring = new StatusPenyaring(SedangMemperbarui: false, info, FilterAktif: 2, Ukuran: 59L * 1024 * 1024);

        var html = await Html("kevin://pengaturan");
        Assert.Contains("2 visits saved.", html);
        Assert.Contains("3.0 MB", html);
        Assert.Contains("Ads and trackers are blocked: 56,899 rules from the lists ads (EasyList), "
            + "Indonesian site ads (ABPindo), updated Tuesday, 29 September 2026.", html);

        Assert.Contains("History and cookies from the last hour will be cleared. You will be signed out of all sites.",
            await Html(KonfirmasiRiwayatDanCookie));
    }

    [Fact]
    public async Task RiwayatDalamBahasaInggris()
    {
        layanan.Preferensi.Bahasa = Bahasa.Inggris;
        layanan.Riwayat.Catat("https://a.id/", "A", T0.AddHours(-1));
        layanan.Riwayat.Catat("https://b.id/", "B", T0.AddDays(-1));
        layanan.Riwayat.Catat("https://c.id/", "C", T0.AddDays(-3));
        var html = await Html("kevin://riwayat");
        Assert.Contains("<h2>Today</h2>", html);
        Assert.Contains("<h2>Yesterday</h2>", html);
        Assert.Contains("<h2>Monday, 28 September 2026</h2>", html);
        Assert.Contains("<span class=\"jam\">08:00</span>", html);
        Assert.Contains("3 visits, kept for up to 90 days.", html);
    }

    [Fact]
    public async Task PrivasiSaatDaftarBelumAdaAtauDimatikan()
    {
        layanan.Penyaring = new StatusPenyaring(SedangMemperbarui: true, Info: null, FilterAktif: 0, Ukuran: 0);
        Assert.Contains("Daftar pemblokir iklan dan pelacak belum siap. Sedang mengunduh daftar terbaru…",
            await Html("kevin://pengaturan"));

        layanan.Penyaring = null;
        Assert.Contains("Pemblokir iklan dan pelacak dimatikan.", await Html("kevin://pengaturan"));
    }
}
