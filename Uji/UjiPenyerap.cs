using System.Text.Json;
using KevinBrowser;
using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiPenyerap : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("kevin-serap-uji-").FullName;
    readonly JaringanPalsu jaringan = new();
    readonly BukuCatatan buku;
    readonly PengaturanAi pengaturan;

    public UjiPenyerap()
    {
        buku = new BukuCatatan(Path.Combine(folder, "catatan"));
        pengaturan = new PengaturanAi(Path.Combine(folder, "data", "asisten.tsv"));
        pengaturan.Simpan("deepseek-v4-pro", "sk-uji-1234567890");
    }

    public void Dispose() => Directory.Delete(folder, true);

    // Jam palsu: Kamis 1 Oktober 2026 pukul 09.00 UTC, jam sibuk DeepSeek.
    Penyerap Penyerap => new(buku, pengaturan, new KlienAi(jaringan), new JamTetap(LayananPalsu.Sekarang));

    static Teks T => new(Bahasa.Indonesia);

    async Task<KeadaanSerap> Serap(string teks = "Isi handout tentang gerak parabola.")
    {
        var kerja = Penyerap.Mulai("handout.pdf", 2269, 1790173271, teks, "fisika", T);
        await kerja.Tugas;
        return kerja.Keadaan;
    }

    [Fact]
    public async Task SerapMenulisCatatanDanSumber()
    {
        var k = await Serap();

        Assert.Equal(TahapSerap.Selesai, k.Tahap);
        Assert.Equal(["2026-10-gerak-parabola", "2026-10-syarat-pakai"], k.Hasil.Select(c => c.Nama));
        Assert.Equal(["Gerak Parabola", "Syarat pakai rumus"], k.Hasil.Select(c => c.Judul));
        var isi = buku.Baca("fisika", "2026-10-gerak-parabola")!;
        Assert.StartsWith("Sumber: handout.pdf\n\n# Gerak Parabola", isi);
        Assert.Contains("[[2026-10-syarat-pakai]]", isi);   // tautan antarcatatan ikut nama berkasnya
        Assert.Equal("fisika/2026-10-gerak-parabola.md, fisika/2026-10-syarat-pakai.md", buku.SudahDiserap("handout.pdf", 2269, 1790173271));
        Assert.Null(buku.SudahDiserap("handout.pdf", 2270, 1790173271));
        Assert.Equal((1200 * 0.044 + 3400 * 1.32 + 800 * 3.96) / 1_000_000, k.Biaya, 12);
    }

    [Fact]
    public async Task PetunjukUntukAi()
    {
        Directory.CreateDirectory(Path.Combine(folder, "catatan", "fisika"));
        File.WriteAllText(Path.Combine(folder, "catatan", "fisika", "2026-09-newton.md"), "# Newton");

        await Serap("TEKS DOKUMEN");

        using var dok = JsonDocument.Parse(Assert.Single(jaringan.Permintaan).Isi!);
        var pesan = dok.RootElement.GetProperty("messages");
        Assert.Contains("MENILAI, BUKAN MENYALIN", pesan[0].GetProperty("content").GetString());
        Assert.Contains("(tambahan, bukan dari dokumen)", pesan[0].GetProperty("content").GetString());
        var pengguna = pesan[1].GetProperty("content").GetString()!;
        Assert.Contains("Mata pelajaran: Fisika", pengguna);
        Assert.Contains("Dokumen: handout.pdf", pengguna);
        Assert.Contains("2026-09-newton", pengguna);
        Assert.Contains("<<<DOKUMEN\nTEKS DOKUMEN\nDOKUMEN>>>", pengguna);
    }

    [Theory]
    [InlineData(402, "Saldo DeepSeek habis")]
    [InlineData(401, "Kunci API ditolak")]
    [InlineData(503, "sedang sibuk")]
    [InlineData(429, "Terlalu banyak permintaan")]
    public async Task GalatJadiPesan(int status, string pesan)
    {
        jaringan.Jawab = _ => JaringanPalsu.Galat(status, "x");
        var k = await Serap();
        Assert.Equal(TahapSerap.Gagal, k.Tahap);
        Assert.Contains(pesan, k.Galat);
        Assert.Empty(buku.Semua());
        Assert.False(buku.AdaSumber);
    }

    [Fact]
    public async Task JawabanTerpotong()
    {
        jaringan.Jawab = _ => (200, JaringanPalsu.Aliran("""{"catatan":[{"nama":"a","isi":"# A""", alasan: "length"));
        var k = await Serap();
        Assert.Equal(TahapSerap.Gagal, k.Tahap);
        Assert.Contains("terpotong", k.Galat);
        Assert.NotNull(k.Jawaban);   // token yang sudah terpakai tetap ditampilkan
    }

    [Fact]
    public async Task JawabanBukanCatatan()
    {
        jaringan.Jawab = _ => (200, JaringanPalsu.Aliran("""{"jawaban":"maaf"}"""));
        var k = await Serap();
        Assert.Equal(TahapSerap.Gagal, k.Tahap);
        Assert.Contains("tidak bisa dibaca jadi catatan", k.Galat);
    }

    [Fact]
    public async Task Dibatalkan()
    {
        jaringan.Tahan = new TaskCompletionSource();
        var kerja = Penyerap.Mulai("a.txt", 1, 1, "teks", "fisika", T);
        kerja.Batal.Cancel();
        await kerja.Tugas;
        Assert.Equal(TahapSerap.Dibatalkan, kerja.Keadaan.Tahap);
        Assert.Empty(buku.Semua());
    }

    [Fact]
    public void UraiJawaban()
    {
        Assert.Equal(2, PromptSerap.Urai(JaringanPalsu.CatatanBawaan)!.Count);
        Assert.Single(PromptSerap.Urai("```json\n{\"catatan\":[{\"nama\":\"a\",\"isi\":\"# A\"}]}\n```")!);
        Assert.Null(PromptSerap.Urai("bukan json"));
        Assert.Null(PromptSerap.Urai("""{"catatan":[]}"""));
        Assert.Null(PromptSerap.Urai("""{"catatan":[{"nama":"a","isi":"   "}]}"""));
        var banyak = "{\"catatan\":[" + string.Join(",", Enumerable.Range(0, 20).Select(i => $"{{\"nama\":\"c{i}\",\"isi\":\"# C{i}\"}}")) + "]}";
        Assert.Equal(12, PromptSerap.Urai(banyak)!.Count);
    }

    [Fact]
    public void TulisSerapanMenghindariNamaYangSudahAda()
    {
        Directory.CreateDirectory(Path.Combine(folder, "catatan", "fisika"));
        var lama = Path.Combine(folder, "catatan", "fisika", "2026-10-rumus.md");
        File.WriteAllText(lama, "# lama");

        var hasil = buku.TulisSerapan("fisika", [new("rumus", "# Rumus\n\n[[contoh|lihat contoh]]"), new("", "# Contoh Soal")], "a.pdf", new DateTime(2026, 10, 3));

        Assert.Equal(["2026-10-rumus-2", "2026-10-contoh-soal"], hasil.Select(c => c.Nama));
        Assert.Equal("# lama", File.ReadAllText(lama));
        Assert.Contains("[[contoh|lihat contoh]]", buku.Baca("fisika", "2026-10-rumus-2"));   // "contoh" bukan nama di kelompok ini
        Assert.Throws<IOException>(() => buku.TulisSerapan("../luar", [new("a", "# A")], "a.pdf", DateTime.Now));
    }

    [Fact]
    public void TambahSumberKeDaftarYangSudahAda()
    {
        Directory.CreateDirectory(Path.Combine(folder, "catatan"));
        var jalur = Path.Combine(folder, "catatan", BukuCatatan.BerkasSumber);
        File.WriteAllText(jalur, "# Dokumen\n\n| berkas | ukuran | waktu-ubah | diserap | jadi catatan |\n|---|---|---|---|---|\n| lama.pdf | 10 | 20 | 2026-09-23 | x.md |");

        buku.TambahSumber("baru.pdf", 30, 40, new DateTime(2026, 10, 3), []);

        Assert.EndsWith("| lama.pdf | 10 | 20 | 2026-09-23 | x.md |\n| baru.pdf | 30 | 40 | 2026-10-03 |  |\n", File.ReadAllText(jalur));
        Assert.Equal("x.md", buku.SudahDiserap("lama.pdf", 10, 20));
        Assert.Equal("", buku.SudahDiserap("baru.pdf", 30, 40));
    }

    [Fact]
    public void SumberBaruDiberiKepalaTabel()
    {
        buku.TambahSumber("a|b.pdf", 1, 2, new DateTime(2026, 10, 3), []);
        var isi = buku.BacaSumber()!;
        Assert.StartsWith("# Dokumen yang sudah diserap jadi catatan", isi);
        Assert.Contains("| berkas | ukuran | waktu-ubah | diserap | jadi catatan |", isi);
        Assert.Contains("| a/b.pdf | 1 | 2 | 2026-10-03 |", isi);
    }

    [Fact]
    public void PengaturanAiDisimpanRahasia()
    {
        var p = new PengaturanAi(Path.Combine(folder, "lain", "asisten.tsv"));
        Assert.Null(p.Kunci);
        Assert.Equal("deepseek-v4-pro", p.Model);

        p.Simpan("deepseek-flash", "sk-1234567890abcdef");
        Assert.Equal(("deepseek-flash", "sk-1234567890abcdef", "sk-…cdef"), (p.Model, p.Kunci, p.KunciTersamar));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(p.Berkas));

        p.Simpan("model-aneh", null);
        Assert.Equal(("deepseek-flash", "sk-1234567890abcdef"), (p.Model, p.Kunci));
        p.Simpan("deepseek-v4-pro", "ada spasi di sini");
        Assert.Equal(("deepseek-v4-pro", "sk-1234567890abcdef"), (p.Model, p.Kunci));

        p.HapusKunci();
        Assert.Null(p.Kunci);
        Assert.Equal("deepseek-v4-pro", p.Model);
    }

    [Fact]
    public void DokumenDiFolderUnduhan()
    {
        var unduhan = Directory.CreateDirectory(Path.Combine(folder, "unduhan")).FullName;
        void Buat(string nama, int menitLalu)
        {
            var jalur = Path.Combine(unduhan, nama);
            File.WriteAllText(jalur, "isi");
            File.SetLastWriteTimeUtc(jalur, DateTime.UtcNow.AddMinutes(-menitLalu));
        }
        Buat("lama.pdf", 60);
        Buat("baru.TXT", 1);
        Buat("catatan.md", 30);
        Buat("foto.jpg", 0);
        Buat(".rahasia.pdf", 0);
        File.CreateSymbolicLink(Path.Combine(unduhan, "tautan.pdf"), Path.Combine(unduhan, "lama.pdf"));

        Assert.Equal(["baru.TXT", "catatan.md", "lama.pdf"], DaftarDokumen.Terbaru(unduhan).Select(d => d.Nama));
        var dok = DaftarDokumen.Ambil(unduhan, "lama.pdf")!;
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(dok.Jalur)).ToUnixTimeSeconds(), dok.WaktuUbah);
        Assert.Null(DaftarDokumen.Ambil(unduhan, "../catatan/x.pdf"));
        Assert.Null(DaftarDokumen.Ambil(unduhan, "foto.jpg"));
        Assert.Null(DaftarDokumen.Ambil(unduhan, "tautan.pdf"));
        Assert.Null(DaftarDokumen.Ambil(unduhan, ".rahasia.pdf"));
        Assert.Empty(DaftarDokumen.Terbaru(Path.Combine(folder, "tidak-ada")));
    }
}
