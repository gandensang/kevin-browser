using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KevinBrowser;
using KevinBrowser.Asisten;

namespace Uji;

[Collection(Koleksi.Halaman)]
public sealed partial class UjiHalamanCatur : IDisposable
{
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

    static (int, IEnumerable<string>) Teks(string isi) => (200, isi.Split('\n'));

    void JaringanCatur() => layanan.Jaringan.Jawab = p =>
        p.Alamat.StartsWith("https://lichess.org/", StringComparison.Ordinal) ? Teks(UjiPgn.Lichess)
        : p.Alamat.EndsWith("/archives", StringComparison.Ordinal) ? Teks("""{"archives":["https://api.chess.com/pub/player/kevin_cc/games/2026/10"]}""")
        : Teks(UjiPgn.ChessCom);

    // Hubungkan akun lewat formulirnya, lalu ikuti pindahan ke ?ambil dan ?diambil.
    async Task<string> Hubungkan(string lichess, string chessCom)
    {
        var form = await Html("kevin://catur");
        var hasil = await Html("kevin://catur?akun", Post(("token", Ambil(Token(), form)), ("lichess", lichess), ("chesscom", chessCom)));
        Assert.Equal("kevin://catur?ambil", Ambil(Pindah(), hasil));
        var ambil = await Html("kevin://catur?ambil");
        Assert.Equal("kevin://catur?diambil", Ambil(Pindah(), ambil));
        return await Html("kevin://catur?diambil");
    }

    [Fact]
    public async Task MenuCaturMenggantikanSegeraHadir()
    {
        var html = await Html("kevin://beranda");
        Assert.Contains("<a href=\"kevin://catur\">Catur</a>", html);
        Assert.DoesNotContain("Segera hadir", html);
        Assert.Contains("<a href=\"kevin://catur\" aria-current=\"page\">Catur</a>", await Html("kevin://catur"));
    }

    [Fact]
    public async Task BerkasPapanDisajikan()
    {
        foreach (var skrip in new[] { "catur", "papan", "latihan" })
            Assert.Equal("text/javascript", (await HalamanBawaan.Ambil($"kevin://{skrip}.js", layanan)).Jenis);
        Assert.Equal("text/css", (await HalamanBawaan.Ambil("kevin://catur.css", layanan)).Jenis);
        foreach (var bidak in new[] { "wk", "wq", "wr", "wb", "wn", "wp", "bk", "bq", "br", "bb", "bn", "bp" })
        {
            var (isi, jenis) = await HalamanBawaan.Ambil($"kevin://bidak-{bidak}.svg", layanan);
            Assert.Equal("image/svg+xml", jenis);
            Assert.Contains("viewBox=\"0 0 45 45\"", Encoding.UTF8.GetString(isi));
        }
    }

    [Fact]
    public async Task TanpaAkunMintaDihubungkan()
    {
        var html = await Html("kevin://catur");
        Assert.Contains("Hubungkan akunmu", html);
        Assert.Contains("tidak perlu login atau kata sandi", html);
        Assert.Contains("Belum ada partai.", html);
        Assert.DoesNotContain("?ambil", html);   // belum ada akun untuk diambil
        Assert.DoesNotContain("<script", html);   // daftar partai tanpa JavaScript
        Assert.Empty(layanan.Jaringan.Permintaan);
    }

    [Fact]
    public async Task HubungkanAmbilDanDaftar()
    {
        JaringanCatur();

        var html = await Hubungkan("kevin_uji", "kevin_cc");

        Assert.Contains("Lichess: 1 partai baru. Chess.com: 1 partai baru.", html);
        Assert.Contains("Akun: Lichess <strong>kevin_uji</strong> · Chess.com <strong>kevin_cc</strong>", html);
        Assert.Contains("<span class=\"hasil kalah\">Kalah</span>", html);   // kevin_uji putih, 0-1
        Assert.Contains("<span class=\"hasil seri\">Seri</span>", html);
        Assert.Contains("vs lawan99 <small>(1602)</small>", html);
        Assert.Contains("<small class=\"pembukaan\">King&#39;s Pawn Game</small>", html);
        Assert.Contains("<small class=\"pembukaan\">Sicilian Defense Najdorf Variation</small>", html);   // dari ECOUrl
        Assert.Contains("Blitz 5+0 · Lichess", html);
        Assert.Contains("Rapid 10+0 · Chess.com", html);
        Assert.Contains("href=\"kevin://catur?partai=lichess-abcdEFGH\"", html);
        Assert.True(html.IndexOf("lichess-abcdEFGH", StringComparison.Ordinal) < html.IndexOf("chesscom-123456789", StringComparison.Ordinal));   // terbaru dulu
        // Muat ulang halaman hasil tidak mengambil lagi.
        var jumlah = layanan.Jaringan.Permintaan.Count;
        Assert.DoesNotContain("partai baru", await Html("kevin://catur?diambil"));
        Assert.Equal(jumlah, layanan.Jaringan.Permintaan.Count);
    }

    [Fact]
    public async Task Saringan()
    {
        JaringanCatur();
        await Hubungkan("kevin_uji", "kevin_cc");

        Assert.Contains("lichess-abcdEFGH", await Html("kevin://catur?hasil=kalah"));
        Assert.DoesNotContain("chesscom-123456789", await Html("kevin://catur?hasil=kalah"));
        Assert.Contains("Tidak ada partai yang cocok", await Html("kevin://catur?hasil=menang"));
        Assert.DoesNotContain("lichess-abcdEFGH", await Html("kevin://catur?warna=hitam"));
        Assert.Contains("chesscom-123456789", await Html("kevin://catur?warna=hitam"));
        Assert.DoesNotContain("lichess-abcdEFGH", await Html("kevin://catur?situs=chesscom"));
        Assert.Contains("lichess-abcdEFGH", await Html("kevin://catur?cari=lawan99"));
        Assert.DoesNotContain("chesscom-123456789", await Html("kevin://catur?cari=lawan99"));
        Assert.Contains("chesscom-123456789", await Html("kevin://catur?cari=najdorf"));
        var saring = await Html("kevin://catur?warna=hitam");
        Assert.Contains("<a class=\"pilihan aktif\" href=\"kevin://catur?warna=hitam\" aria-current=\"true\">Semua</a>", saring);
        Assert.Contains("<a class=\"pilihan aktif\" href=\"kevin://catur\" aria-current=\"true\">Hitam</a>", saring);   // klik lagi = lepas
    }

    [Fact]
    public async Task PemutarPartai()
    {
        JaringanCatur();
        await Hubungkan("kevin_uji", "");

        var html = await Html("kevin://catur?partai=lichess-abcdEFGH");

        Assert.Contains("<title>kevin_uji vs lawan99 · Kevin Browser</title>", html);
        Assert.Contains("<h1>kevin_uji vs lawan99</h1>", html);
        Assert.Contains("Rated blitz game · 5 Okt 2026 · Hitam menang (0-1)", html);
        Assert.Equal(9, Regex.Count(html, "data-ply=\""));
        Assert.Contains("<button type=\"button\" data-ply=\"6\" class=\"t-blunder\">g6<span class=\"tanda\">??</span></button>", html);
        Assert.Contains("<p class=\"pembukaan-partai\">C20 King&#39;s Pawn Game</p>", html);
        Assert.Contains("<div class=\"kolom-papan\">", html);   // putih di bawah: kevin_uji main putih
        Assert.Contains("<script type=\"application/json\" id=\"data-partai\">{\"fen0\":\"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1\",\"balik\":false,\"langkah\":[{\"fen\":", html);
        Assert.Contains("\"dari\":\"e2\",\"ke\":\"e4\",\"nilai\":0.2}", html);
        Assert.Contains("\"mat\":-2}", html);
        Assert.Contains("<script src=\"kevin://catur.js\"></script>", html);
        Assert.Contains("href=\"https://lichess.org/abcdEFGH\"", html);
        Assert.Contains("Buka di Lichess</a>", html);

        Assert.Contains("Partai tidak ditemukan", await Html("kevin://catur?partai=lichess-tidakada"));
    }

    [Fact]
    public async Task PapanTerbalikKalauMainHitam()
    {
        JaringanCatur();
        await Hubungkan("", "kevin_cc");
        var html = await Html("kevin://catur?partai=chesscom-123456789");
        Assert.Contains("<div class=\"kolom-papan terbalik\">", html);
        Assert.Contains("\"balik\":true", html);
        Assert.Contains("Buka di Chess.com</a>", html);
    }

    [Fact]
    public async Task DataPartaiAmanDiDalamSkrip()
    {
        var form = await Html("kevin://catur?tempel");
        var hasil = await Html("kevin://catur?tempel", Post(("token", Ambil(Token(), form)),
            ("pgn", "[White \"<b>A</b>\"]\n\n1. e4 { </script><script>alert(1)</script> } e5 *")));
        var html = await Html(Ambil(Pindah(), hasil));

        var data = html[html.IndexOf("id=\"data-partai\">", StringComparison.Ordinal)..];
        data = data[..data.IndexOf("</script>", StringComparison.Ordinal)];
        Assert.DoesNotContain("<", data);
        Assert.Contains("\\u003C/script\\u003E", data);
        Assert.Contains("<h1>&lt;b&gt;A&lt;/b&gt; vs ?</h1>", html);
    }

    [Fact]
    public async Task TempelPgnDanTautan()
    {
        var form = await Html("kevin://catur?tempel");
        var salah = await Html("kevin://catur?tempel", Post(("token", Ambil(Token(), form)), ("pgn", "1. e4 e5 2. Ke3")));
        Assert.Contains("Langkah 2. Ke3 tidak sah.", salah);
        Assert.Contains(">\n1. e4 e5 2. Ke3</textarea>", salah);   // ketikannya tidak hilang

        var hasil = await Html("kevin://catur?tempel", Post(("token", Ambil(Token(), salah)), ("pgn", "1. d4 d5 2. c4 *")));
        Assert.StartsWith("kevin://catur?partai=pgn-", Ambil(Pindah(), hasil));

        layanan.Jaringan.Jawab = _ => Teks(UjiPgn.Lichess);
        form = await Html("kevin://catur?tempel");
        hasil = await Html("kevin://catur?tempel", Post(("token", Ambil(Token(), form)), ("pgn", "https://lichess.org/abcdEFGH/black")));
        Assert.Equal("kevin://catur?partai=lichess-abcdEFGH", Ambil(Pindah(), hasil));
        Assert.Equal("https://lichess.org/game/export/abcdEFGH?evals=true&opening=true&clocks=false", Assert.Single(layanan.Jaringan.Permintaan).Alamat);
    }

    [Fact]
    public async Task SimpanKeCatatan()
    {
        JaringanCatur();
        await Hubungkan("kevin_uji", "");
        var html = await Html("kevin://catur?partai=lichess-abcdEFGH");

        var hasil = await Html("kevin://catur?partai=lichess-abcdEFGH", Post(("aksi", "simpan"), ("token", Ambil(Token(), html))));

        var alamat = Ambil(Pindah(), hasil);
        Assert.StartsWith("kevin://belajar?m=catur&c=2026-10-", alamat);
        var catatan = Assert.Single(layanan.Catatan.Semua());
        Assert.Equal(("catur", "Partai vs lawan99, 5 Okt 2026"), (catatan.Mapel, catatan.Judul));
        Assert.Contains("-partai-vs-lawan99-", catatan.Nama);
        var isi = layanan.Catatan.Baca("catur", catatan.Nama)!;
        Assert.Contains("Sumber: https://lichess.org/abcdEFGH (Lichess, 5 Okt 2026, Blitz 5+0)", isi);
        Assert.Contains("- Hasil: Hitam menang (0-1) (Normal)\n- Pembukaan: C20 King's Pawn Game", isi);
        Assert.Contains("```pgn\n1. e4 e5 2. Qh5 Nc6 3. Bc4 g6 4. Qf3 Nf6 5. g4 0-1\n```", isi);
        Assert.Contains("[Buka di papan Catur](kevin://catur?partai=lichess-abcdEFGH)", isi);
        Assert.Contains("## Momen penting (analisis Lichess)\n\n- 2. Qh5?!: ketidaktepatan (0,25 → -0,30). Lebih baik: Nf3.\n\n## Pelajaran", isi);
        Assert.DoesNotContain("3... g6", isi);   // langkah lawan

        // Di Belajar, tautan kembali ke papan tetap jadi tautan.
        Assert.Contains("<a href=\"kevin://catur?partai=lichess-abcdEFGH\">Buka di papan Catur</a>", await Html(alamat));

        // Menyimpan lagi (token baru) membuka catatan yang sama, tidak membuat yang kedua.
        html = await Html("kevin://catur?partai=lichess-abcdEFGH");
        Assert.Equal(alamat, Ambil(Pindah(), await Html("kevin://catur?partai=lichess-abcdEFGH", Post(("aksi", "simpan"), ("token", Ambil(Token(), html))))));
        Assert.Single(layanan.Catatan.Semua());

        // Token yang sama tidak menyimpan dua kali.
        var lagi = await Html("kevin://catur?partai=lichess-abcdEFGH", Post(("aksi", "simpan"), ("token", Ambil(Token(), html))));
        Assert.Contains("sudah dipakai atau kedaluwarsa", lagi);
        Assert.Single(layanan.Catatan.Semua());
    }

    [Fact]
    public async Task AkunTidakSahDanGalatJaringan()
    {
        var form = await Html("kevin://catur?akun");
        var salah = await Html("kevin://catur?akun", Post(("token", Ambil(Token(), form)), ("lichess", "nama spasi"), ("chesscom", "")));
        Assert.Contains("Nama akun Lichess tidak sah", salah);
        Assert.Null(layanan.Partai.Akun.Lichess);

        layanan.Jaringan.Jawab = p => p.Alamat.Contains("lichess", StringComparison.Ordinal) ? (404, [""]) : throw new GalatAi(0, "tidak ada jaringan");
        var html = await Hubungkan("tidak_ada", "kevin_cc");
        Assert.Contains("Lichess: “tidak_ada” tidak ditemukan.", html);
        Assert.Contains("Chess.com tidak bisa dihubungi. Periksa sambungan internet.", html);
    }

    [Fact]
    public async Task BahasaInggris()
    {
        layanan.Preferensi.Bahasa = Bahasa.Inggris;
        JaringanCatur();
        var daftar = await Hubungkan("kevin_uji", "kevin_cc");
        var pemutar = await Html("kevin://catur?partai=lichess-abcdEFGH");

        Assert.Contains("<h1>Chess</h1>", daftar);
        Assert.Contains("Lichess: 1 new game.", daftar);
        Assert.Contains("<span class=\"hasil kalah\">Lost</span>", daftar);
        Assert.Contains("Black won (0-1)", pemutar);
        foreach (var html in new[] { daftar, pemutar, await Html("kevin://catur?tempel"), await Html("kevin://catur?akun") })
            foreach (var kata in UjiHalaman.KataIndonesia)
                Assert.False(html.Contains(kata, StringComparison.Ordinal), $"\"{kata}\"");
    }
}
