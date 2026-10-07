using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KevinBrowser;

namespace Uji;

[Collection(Koleksi.Halaman)]
public sealed partial class UjiHalamanSerap : IDisposable
{
    static readonly string Materi = string.Join(" ", Enumerable.Repeat("Gerak parabola terdiri dari GLB di sumbu x dan GLBB di sumbu y.", 5));

    readonly LayananPalsu layanan = new();

    public void Dispose() => layanan.Dispose();

    async Task<string> Html(string uri, string? post = null)
    {
        var (isi, _) = await HalamanBawaan.Ambil(uri, layanan, post);
        return Encoding.UTF8.GetString(isi);
    }

    static string Post(params (string Kunci, string Nilai)[] isi) =>
        string.Join('&', isi.Select(p => $"{Uri.EscapeDataString(p.Kunci)}={Uri.EscapeDataString(p.Nilai).Replace("%20", "+")}"));

    [GeneratedRegex("name=\"token\" value=\"([0-9A-F]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex("http-equiv=\"refresh\" content=\"0; url=([^\"]+)\"")]
    private static partial Regex Pindah();

    static string Ambil(Regex pola, string html) => WebUtility.HtmlDecode(pola.Match(html).Groups[1].Value);

    void Unduhan(string nama, string isi) => File.WriteAllText(Path.Combine(layanan.FolderUnduhan, nama), isi);

    void PasangKunci() => layanan.PengaturanAi.Simpan("deepseek-v4-pro", "sk-uji-1234567890");

    // Halaman kemajuan memuat ulang dirinya; tunggu sampai berhenti.
    async Task<string> TungguSelesai(string alamat)
    {
        for (var i = 0; i < 500; i++)
        {
            var html = await Html(alamat);
            if (!html.Contains("http-equiv=\"refresh\"", StringComparison.Ordinal))
                return html;
            await Task.Delay(10);
        }
        throw new TimeoutException(alamat);
    }

    async Task<string> MulaiSerap(string alamat, params (string, string)[] isian)
    {
        var form = await Html(alamat);
        var hasil = await Html(alamat, Post([("aksi", "mulai"), ("token", Ambil(Token(), form)), .. isian]));
        var kerja = Ambil(Pindah(), hasil);
        Assert.StartsWith("kevin://belajar?serap&kerja=", kerja);
        return await TungguSelesai(kerja);
    }

    [Fact]
    public async Task BerandaBelajarMenunjukkanSerapan()
    {
        var html = await Html("kevin://belajar");
        Assert.Contains("href=\"kevin://belajar?serap\"", html);
        Assert.Contains("href=\"kevin://belajar?ai\"", html);
    }

    [Fact]
    public async Task TanpaKunciDiarahkanKePengaturan()
    {
        var html = await Html("kevin://belajar?serap");
        Assert.Contains("Belum ada kunci API DeepSeek", html);
        Assert.Contains("href=\"kevin://belajar?ai\"", html);
    }

    [Fact]
    public async Task SimpanKunciDanLihatSaldo()
    {
        var form = await Html("kevin://belajar?ai");
        Assert.Contains("<strong>Belum ada kunci API</strong>", form);
        Assert.Contains("type=\"password\"", form);

        var hasil = await Html("kevin://belajar?ai", Post(("aksi", "simpan"), ("token", Ambil(Token(), form)),
            ("model", "deepseek-flash"), ("kunci", " sk-uji-1234567890 ")));
        Assert.Contains("Pengaturan Asisten AI disimpan.", hasil);
        Assert.Contains("<code>sk-…7890</code>", hasil);
        Assert.Contains("Saldo: 4,87 USD.", hasil);
        Assert.DoesNotContain("sk-uji-1234567890", hasil);   // kunci utuh tidak pernah tampil
        Assert.Equal(("deepseek-flash", "sk-uji-1234567890"), (layanan.PengaturanAi.Model, layanan.PengaturanAi.Kunci));

        var hapus = Token().Matches(hasil)[0].Groups[1].Value;   // formulir hapus lebih dulu
        var akhir = await Html("kevin://belajar?ai", Post(("aksi", "hapus"), ("token", hapus)));
        Assert.Contains("Kunci API dihapus", akhir);
        Assert.Null(layanan.PengaturanAi.Kunci);
    }

    [Fact]
    public async Task KunciTidakWajarDitolak()
    {
        var form = await Html("kevin://belajar?ai");
        var hasil = await Html("kevin://belajar?ai", Post(("aksi", "simpan"), ("token", Ambil(Token(), form)),
            ("model", "deepseek-v4-pro"), ("kunci", "bukan kunci")));
        Assert.Contains("tidak wajar", hasil);
        Assert.Null(layanan.PengaturanAi.Kunci);
    }

    [Fact]
    public async Task KunciDitolakDeepSeek()
    {
        PasangKunci();
        layanan.Jaringan.Jawab = _ => JaringanPalsu.Galat(401, "Authentication Fails");
        Assert.Contains("DeepSeek menolak kunci ini.", await Html("kevin://belajar?ai"));
    }

    [Fact]
    public async Task SerapBerkasTeks()
    {
        PasangKunci();
        Unduhan("materi.txt", Materi);

        var daftar = await Html("kevin://belajar?serap");
        Assert.Contains("href=\"kevin://belajar?serap&amp;berkas=materi.txt\"", daftar);
        var konfirmasi = await Html("kevin://belajar?serap&berkas=materi.txt");
        Assert.Contains("huruf teks", konfirmasi);
        Assert.Contains("Perkiraan kasar:", konfirmasi);
        Assert.Matches("biaya ±\\$0,0\\d\\d dengan deepseek-v4-pro, (±\\$0,0\\d\\d|&lt;\\$0,001) dengan deepseek-flash", konfirmasi);
        Assert.Contains("<option value=\"deepseek-v4-pro\" selected>", konfirmasi);
        Assert.DoesNotContain("disabled", konfirmasi);

        var selesai = await MulaiSerap("kevin://belajar?serap&berkas=materi.txt", ("mapel-baru", "Fisika"));
        Assert.Contains("<h1>Selesai</h1>", selesai);
        Assert.Contains("2 catatan baru dari materi.txt.", selesai);
        Assert.Contains("href=\"kevin://belajar?m=fisika&amp;c=2026-10-gerak-parabola\"", selesai);
        Assert.Contains("Token: 4.600 masuk (1.200 dari cache), 800 keluar", selesai);
        Assert.StartsWith("Sumber: materi.txt", layanan.Catatan.Baca("fisika", "2026-10-gerak-parabola"));

        // Dokumen yang sama tidak diserap dua kali, kecuali diminta ("Serap lagi").
        Assert.Contains("sudah diserap", await Html("kevin://belajar?serap"));
        var sudah = await Html("kevin://belajar?serap&berkas=materi.txt");
        Assert.Contains("sudah pernah diserap", sudah);
        Assert.Contains("href=\"kevin://belajar?serap&amp;berkas=materi.txt&amp;lagi\"", sudah);
        Assert.Single(layanan.Jaringan.Permintaan, p => p.Alamat.EndsWith("/chat/completions", StringComparison.Ordinal));

        var lagi = await MulaiSerap("kevin://belajar?serap&berkas=materi.txt&lagi", ("mapel", "fisika"), ("model", "deepseek-flash"));
        Assert.Contains("<h1>Selesai</h1>", lagi);
        Assert.StartsWith("Sumber: materi.txt (diolah deepseek-flash)", layanan.Catatan.Baca("fisika", "2026-10-gerak-parabola-2"));
        Assert.StartsWith("Sumber: materi.txt (diolah deepseek-v4-pro)", layanan.Catatan.Baca("fisika", "2026-10-gerak-parabola"));
        Assert.Equal(2, layanan.Catatan.BacaSumber()!.Split('\n').Count(b => b.StartsWith("| materi.txt |", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TanpaKunciTombolSerapMati()
    {
        Unduhan("materi.txt", Materi);
        var konfirmasi = await Html("kevin://belajar?serap&berkas=materi.txt");
        Assert.Contains("type=\"submit\" disabled", konfirmasi);
    }

    [Fact]
    public async Task PdfDibacaPerHalaman()
    {
        PasangKunci();
        Unduhan("handout.pdf", Materi + "---" + Materi);
        Assert.Contains("2 halaman", await Html("kevin://belajar?serap&berkas=handout.pdf"));
    }

    [Fact]
    public async Task PembacaPdfBelumTerpasang()
    {
        layanan.PdfKeTeks = (_, _) => throw new FileNotFoundException("pdftotext");
        Unduhan("handout.pdf", "apa saja");
        Assert.Contains("sudo apt install poppler-utils", await Html("kevin://belajar?serap&berkas=handout.pdf"));
    }

    [Fact]
    public async Task PdfHasilPindaian()
    {
        layanan.PdfKeTeks = (_, _) => Task.FromResult<string?>(" \f \f ");
        Unduhan("pindaian.pdf", "apa saja");
        Assert.Contains("Hampir tidak ada teks", await Html("kevin://belajar?serap&berkas=pindaian.pdf"));
    }

    [Fact]
    public async Task TempelTeks()
    {
        PasangKunci();
        var selesai = await MulaiSerap("kevin://belajar?serap&tempel",
            ("judul", "Catatan papan tulis"), ("mapel-baru", "Fisika"), ("teks", Materi));
        Assert.Contains("<h1>Selesai</h1>", selesai);
        Assert.StartsWith("Sumber: tempelan: Catatan papan tulis", layanan.Catatan.Baca("fisika", "2026-10-gerak-parabola"));
        Assert.Contains("| tempelan: Catatan papan tulis |", layanan.Catatan.BacaSumber());
    }

    [Fact]
    public async Task IsianKurangTidakMemulai()
    {
        PasangKunci();
        var form = await Html("kevin://belajar?serap&tempel");
        var hasil = await Html("kevin://belajar?serap&tempel",
            Post(("aksi", "mulai"), ("token", Ambil(Token(), form)), ("judul", "Papan tulis"), ("teks", Materi)));
        Assert.Contains("Pilih atau tulis dulu mata pelajarannya.", hasil);
        Assert.Contains(Materi, hasil);   // teksnya tidak hilang
        Assert.Empty(layanan.Jaringan.Permintaan);
    }

    [Fact]
    public async Task SaldoHabis()
    {
        PasangKunci();
        layanan.Jaringan.Jawab = p => p.Alamat.EndsWith("/chat/completions", StringComparison.Ordinal)
            ? JaringanPalsu.Galat(402, "Insufficient Balance")
            : (200, [JaringanPalsu.Saldo]);
        Unduhan("materi.txt", Materi);
        var hasil = await MulaiSerap("kevin://belajar?serap&berkas=materi.txt", ("mapel-baru", "Fisika"));
        Assert.Contains("<h1>Gagal</h1>", hasil);
        Assert.Contains("Saldo DeepSeek habis", hasil);
        Assert.Null(layanan.Catatan.BacaSumber());
    }

    [Fact]
    public async Task Batalkan()
    {
        PasangKunci();
        layanan.Jaringan.Tahan = new TaskCompletionSource();
        Unduhan("materi.txt", Materi);
        var form = await Html("kevin://belajar?serap&berkas=materi.txt");
        var mulai = await Html("kevin://belajar?serap&berkas=materi.txt",
            Post(("aksi", "mulai"), ("token", Ambil(Token(), form)), ("mapel-baru", "Fisika")));
        var kerja = Ambil(Pindah(), mulai);

        var proses = await Html(kerja);
        Assert.Contains("<h1>Menyerap…</h1>", proses);
        Assert.Contains("http-equiv=\"refresh\" content=\"3\"", proses);

        await Html(kerja, Post(("aksi", "batal")));
        Assert.Contains("<h1>Dibatalkan</h1>", await TungguSelesai(kerja));
        Assert.Empty(layanan.Catatan.Semua());
    }

    [Theory]
    [InlineData("kevin://belajar?serap&berkas=..%2Fasisten.tsv")]
    [InlineData("kevin://belajar?serap&berkas=tidak-ada.pdf")]
    public async Task BerkasDiLuarUnduhanDitolak(string uri) =>
        Assert.Contains("Berkas itu tidak ada di folder unduhan.", await Html(uri));

    [Fact]
    public async Task PekerjaanTidakDikenal() =>
        Assert.Contains("Pekerjaan ini tidak ditemukan", await Html("kevin://belajar?serap&kerja=ABC"));
}
