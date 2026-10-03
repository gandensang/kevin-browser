using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KevinBrowser;

namespace Uji;

[Collection(Koleksi.Halaman)]
public sealed partial class UjiHalamanTanya : IDisposable
{
    readonly LayananPalsu layanan = new();

    public UjiHalamanTanya()
    {
        var fisika = Directory.CreateDirectory(Path.Combine(layanan.Catatan.Folder, "fisika")).FullName;
        File.WriteAllText(Path.Combine(fisika, "2026-10-gaya-gesek.md"), "# Gaya gesek\n\nStatis bekerja sebelum benda bergerak.\n");
    }

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

    void PasangKunci() => layanan.PengaturanAi.Simpan("deepseek-v4-pro", "sk-uji-1234567890");

    // Jawaban DeepSeek berurutan, satu per permintaan; yang terakhir diulang.
    void Skenario(params (int, IEnumerable<string>)[] jawaban)
    {
        var i = 0;
        layanan.Jaringan.Jawab = _ => jawaban[Math.Min(i++, jawaban.Length - 1)];
    }

    // Halaman obrolan memuat ulang dirinya selama AI bekerja; tunggu sampai berhenti.
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

    // Kirim pertanyaan dari formulir di alamat itu; kembali ke alamat obrolan.
    async Task<string> Kirim(string alamat, string pertanyaan)
    {
        var form = await Html(alamat);
        var hasil = await Html(alamat, Post(("aksi", "tanya"), ("token", Ambil(Token(), form)), ("pertanyaan", pertanyaan)));
        var obrolan = Ambil(Pindah(), hasil);
        Assert.Matches("^kevin://belajar\\?tanya&obrolan=[0-9A-F]{16}#akhir$", obrolan);
        return obrolan;
    }

    [Fact]
    public async Task TombolTanyaDiBelajarDanCatatan()
    {
        Assert.Contains("href=\"kevin://belajar?tanya\"", await Html("kevin://belajar"));
        var catatan = await Html("kevin://belajar?m=fisika&c=2026-10-gaya-gesek");
        Assert.Contains("<a class=\"tombol\" href=\"kevin://belajar?tanya&amp;m=fisika&amp;c=2026-10-gaya-gesek\">Tanya tentang catatan ini</a>", catatan);
    }

    [Fact]
    public async Task TanpaKunciTombolnyaMati()
    {
        var html = await Html("kevin://belajar?tanya");
        Assert.Contains("Belum ada kunci API DeepSeek", html);
        Assert.Contains("type=\"submit\" disabled>Tanya</button>", html);
    }

    [Fact]
    public async Task TanyaDanJawab()
    {
        PasangKunci();
        Skenario(
            JaringanPalsu.Tool(null, ("cari_catatan", """{"kata":"gaya gesek"}""")),
            JaringanPalsu.Tool(null, ("baca_catatan", """{"mapel":"fisika","nama":"2026-10-gaya-gesek"}""")),
            JaringanPalsu.Tool("**Statis** bekerja sebelum benda bergerak. Lihat [[2026-10-gaya-gesek]] dan [[tidak-ada]]."),
            JaringanPalsu.Tool("Kinetis bekerja saat benda bergerak."));

        var mulai = await Html("kevin://belajar?tanya");
        Assert.Contains("autofocus", mulai);
        Assert.Contains("Model: deepseek-flash", mulai);
        var alamat = await Kirim("kevin://belajar?tanya", "Apa itu gaya gesek <statis>?");
        var html = await TungguSelesai(alamat);

        Assert.Contains("<p class=\"pertanyaan\">Apa itu gaya gesek &lt;statis&gt;?</p>", html);
        Assert.Contains("<strong>Statis</strong> bekerja", html);
        Assert.Contains("Lihat <a href=\"kevin://belajar?m=fisika&amp;c=2026-10-gaya-gesek\">Gaya gesek</a> dan", html);
        Assert.Contains("<span class=\"putus\">tidak-ada</span>", html);
        Assert.Contains("Catatan yang dibaca: <a href=\"kevin://belajar?m=fisika&amp;c=2026-10-gaya-gesek\">Gaya gesek</a>", html);
        Assert.Contains("deepseek-flash · 3.600 token masuk (2.700 dari cache), 150 keluar · biaya &lt;$0,001 · 0 detik", html);
        Assert.Contains("<section class=\"giliran\" id=\"akhir\">", html);
        Assert.Contains("Pertanyaan lanjutan", html);
        Assert.Contains("href=\"kevin://belajar?tanya\">Pertanyaan baru</a>", html);

        // Pertanyaan lanjutan dari formulir di bawah jawaban.
        var obrolan = alamat[..alamat.IndexOf('#')];
        Assert.Equal(alamat, await Kirim(obrolan, "Kalau kinetis?"));
        html = await TungguSelesai(alamat);
        Assert.Equal(2, Regex.Count(html, "<section class=\"giliran\""));
        Assert.Contains("Kinetis bekerja saat benda bergerak.", html);
        using var dok = JsonDocument.Parse(layanan.Jaringan.Permintaan[^1].Isi!);
        var pesan = dok.RootElement.GetProperty("messages");
        Assert.Equal("Apa itu gaya gesek <statis>?", pesan[1].GetProperty("content").GetString());
        Assert.Equal("Kalau kinetis?", pesan[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SelamaBekerjaHalamanMemperbaruiDiri()
    {
        PasangKunci();
        layanan.Jaringan.Tahan = new TaskCompletionSource();
        var alamat = await Kirim("kevin://belajar?tanya", "Apa itu gaya?");

        var html = await Html(alamat);
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"2\">", html);
        Assert.Contains("<li class=\"sedang\">Berpikir… · 0 detik</li>", html);
        Assert.DoesNotContain("name=\"pertanyaan\"", html);   // pertanyaan berikutnya menunggu jawaban ini

        var obrolan = alamat[..alamat.IndexOf('#')];
        await Html(obrolan, Post(("aksi", "batal")));
        html = await TungguSelesai(alamat);
        Assert.Contains("<p class=\"pesan\" role=\"status\">Dibatalkan.</p>", html);
        Assert.Contains("name=\"pertanyaan\"", html);
    }

    [Fact]
    public async Task TokenTerpakaiTidakBertanyaLagi()
    {
        PasangKunci();
        Skenario(JaringanPalsu.Tool("Jawaban."));
        var form = await Html("kevin://belajar?tanya");
        var isian = Post(("aksi", "tanya"), ("token", Ambil(Token(), form)), ("pertanyaan", "Apa itu gaya?"));
        await TungguSelesai(Ambil(Pindah(), await Html("kevin://belajar?tanya", isian)));

        var lagi = await Html("kevin://belajar?tanya", isian);

        Assert.Contains("sudah dipakai atau kedaluwarsa", lagi);
        Assert.Contains(">\nApa itu gaya?</textarea>", lagi);   // ketikannya tidak hilang
        Assert.Single(layanan.Jaringan.Permintaan);
    }

    [Fact]
    public async Task PertanyaanKosongAtauTerlaluPanjang()
    {
        PasangKunci();
        var form = await Html("kevin://belajar?tanya");
        Assert.Contains("Tulis dulu pertanyaannya.",
            await Html("kevin://belajar?tanya", Post(("aksi", "tanya"), ("token", Ambil(Token(), form)), ("pertanyaan", "  \n "))));
        Assert.Contains("Pertanyaannya terlalu panjang",
            await Html("kevin://belajar?tanya", Post(("aksi", "tanya"), ("token", Ambil(Token(), form)), ("pertanyaan", new string('x', 4001)))));
        Assert.Empty(layanan.Jaringan.Permintaan);
    }

    [Fact]
    public async Task ObrolanTidakAda()
    {
        var html = await Html("kevin://belajar?tanya&obrolan=0123456789ABCDEF");
        Assert.Contains("Obrolan ini tidak ditemukan.", html);
        Assert.Contains("href=\"kevin://belajar?tanya\"", html);
    }

    [Fact]
    public async Task TanyaTentangCatatan()
    {
        PasangKunci();
        Skenario(JaringanPalsu.Tool("Sebelum bergerak."));
        const string Alamat = "kevin://belajar?tanya&m=fisika&c=2026-10-gaya-gesek";
        Assert.Contains("Tentang catatan <a href=\"kevin://belajar?m=fisika&amp;c=2026-10-gaya-gesek\">Gaya gesek</a> (Fisika)", await Html(Alamat));

        var html = await TungguSelesai(await Kirim(Alamat, "Kapan statis bekerja?"));

        Assert.Contains("Tentang catatan <a href=\"kevin://belajar?m=fisika&amp;c=2026-10-gaya-gesek\">Gaya gesek</a>", html);
        Assert.Contains("Catatan yang dibaca: <a", html);
        using var dok = JsonDocument.Parse(Assert.Single(layanan.Jaringan.Permintaan).Isi!);
        var pertanyaan = dok.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.StartsWith("(Aku sedang membuka catatan [[2026-10-gaya-gesek]] (Fisika). Isinya:\n<<<CATATAN\n# Gaya gesek", pertanyaan);
        Assert.EndsWith("Kapan statis bekerja?", pertanyaan);
    }

    [Fact]
    public async Task GalatTampilDiObrolan()
    {
        PasangKunci();
        layanan.Jaringan.Jawab = _ => JaringanPalsu.Galat(401, "Authentication Fails");
        var html = await TungguSelesai(await Kirim("kevin://belajar?tanya", "Apa itu gaya?"));
        Assert.Contains("Kunci API ditolak DeepSeek.", html);
    }

    [Fact]
    public async Task ModelTanyaJawabDiPengaturan()
    {
        var form = await Html("kevin://belajar?ai");
        Assert.Contains("<select name=\"model-tanya\"><option value=\"deepseek-v4-pro\">", form);
        Assert.Contains("<option value=\"deepseek-flash\" selected>", form);

        var hasil = await Html("kevin://belajar?ai", Post(("aksi", "simpan"), ("token", Ambil(Token(), form)),
            ("model", "deepseek-flash"), ("model-tanya", "deepseek-v4-pro"), ("kunci", "")));

        Assert.Contains("Pengaturan Asisten AI disimpan.", hasil);
        Assert.Equal(("deepseek-flash", "deepseek-v4-pro"), (layanan.PengaturanAi.Model, layanan.PengaturanAi.ModelTanya));
        Assert.Contains("pertanyaan di halaman Tanya", hasil);   // privasi menyebut tanya-jawab
    }

    [Fact]
    public async Task ObrolanBahasaInggris()
    {
        layanan.Preferensi.Bahasa = Bahasa.Inggris;
        PasangKunci();
        Skenario(
            JaringanPalsu.Tool(null, ("cari_catatan", """{"kata":"friction"}""")),
            JaringanPalsu.Tool("Static friction acts before the object moves."));

        var html = await TungguSelesai(await Kirim("kevin://belajar?tanya&m=fisika&c=2026-10-gaya-gesek", "What is static friction?"));

        Assert.Contains("tokens in (1,800 cached), 100 out", html);
        Assert.Contains("Notes read:", html);
        Assert.Contains("About the note", html);
        foreach (var kata in UjiHalaman.KataIndonesia)
            Assert.False(html.Contains(kata, StringComparison.Ordinal), $"\"{kata}\"");
        using var dok = JsonDocument.Parse(layanan.Jaringan.Permintaan[0].Isi!);
        Assert.StartsWith("You are a study assistant", dok.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }
}
