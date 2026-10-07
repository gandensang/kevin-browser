using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiKoleksiPartai : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("kevin-catur-uji-").FullName;
    readonly JaringanPalsu jaringan = new();
    readonly KoleksiPartai koleksi;

    public UjiKoleksiPartai() => koleksi = new KoleksiPartai(folder, jaringan);

    public void Dispose() => Directory.Delete(folder, true);

    static (int, IEnumerable<string>) Teks(string isi) => (200, isi.Split('\n'));

    [Fact]
    public async Task AmbilLichessTanpaLogin()
    {
        koleksi.Akun.Simpan("kevin_uji", null);
        jaringan.Jawab = _ => Teks(UjiPgn.Lichess);

        Assert.Equal(1, await koleksi.AmbilLichess("kevin_uji", default));

        var minta = Assert.Single(jaringan.Permintaan);
        Assert.Equal("https://lichess.org/api/games/user/kevin_uji?max=30&perfType=ultraBullet,bullet,blitz,rapid,classical,correspondence&opening=true&evals=true&clocks=false", minta.Alamat);
        Assert.Contains(("Accept", "application/x-chess-pgn"), minta.Kepala);
        Assert.Contains(minta.Kepala, k => k.Nama == "User-Agent" && k.Nilai.StartsWith("KevinBrowser/", StringComparison.Ordinal));
        Assert.DoesNotContain(minta.Kepala, k => k.Nama == "Authorization");
        Assert.Equal("lichess-abcdEFGH", Assert.Single(koleksi.Semua()).Id);

        Assert.Equal(0, await koleksi.AmbilLichess("kevin_uji", default));   // sudah ada
        Assert.Single(koleksi.Semua());
        Assert.True(File.Exists(Path.Combine(folder, "lichess-kevin_uji.pgn")));
        Assert.Single(new KoleksiPartai(folder, jaringan).Semua());           // tersimpan di disk
    }

    [Fact]
    public async Task AmbilChessComDariArsipBulanan()
    {
        koleksi.Akun.Simpan(null, "Kevin_CC");
        var arsip = """
            {"archives":["https://api.chess.com/pub/player/kevin_cc/games/2026/08","https://api.chess.com/pub/player/kevin_cc/games/2026/09","https://contoh.com/pub/player/kevin_cc/games/2026/10","https://api.chess.com/pub/player/kevin_cc/games/2026/10"]}
            """;
        jaringan.Jawab = p => p.Alamat.EndsWith("/archives", StringComparison.Ordinal) ? Teks(arsip)
            : p.Alamat.EndsWith("/2026/10/pgn", StringComparison.Ordinal) ? Teks(UjiPgn.ChessCom)
            : Teks("");

        Assert.Equal(1, await koleksi.AmbilChessCom("Kevin_CC", default));

        Assert.Equal([
            "https://api.chess.com/pub/player/kevin_cc/games/archives",
            "https://api.chess.com/pub/player/kevin_cc/games/2026/09/pgn",
            "https://api.chess.com/pub/player/kevin_cc/games/2026/10/pgn",
        ], jaringan.Permintaan.Select(p => p.Alamat));
        Assert.Equal("chesscom-123456789", Assert.Single(koleksi.Semua()).Id);

        // Sesudah yang pertama, bulan terakhir saja.
        jaringan.Permintaan.Clear();
        await koleksi.AmbilChessCom("Kevin_CC", default);
        Assert.Equal(2, jaringan.Permintaan.Count);
    }

    [Fact]
    public async Task AkunTidakAdaJadiGalat()
    {
        jaringan.Jawab = _ => (404, ["""{"error":"Not found"}"""]);
        var galat = await Assert.ThrowsAsync<GalatAi>(() => koleksi.AmbilLichess("tidak_ada", default));
        Assert.Equal(404, galat.Kode);
        Assert.Empty(koleksi.Semua());
    }

    [Fact]
    public void TempelPgn()
    {
        var (partai, galat) = koleksi.Tempel("1. e4 e5 2. Nf3 *");
        Assert.Null(galat);
        Assert.StartsWith("pgn-", partai!.Id);
        Assert.Equal((null, "2. Ke3"), (koleksi.Tempel("1. e4 e5 2. Ke3 *").Partai, koleksi.Tempel("1. e4 e5 2. Ke3 *").Galat));
        Assert.Equal((null, "1. halo,"), koleksi.Tempel("halo, ini bukan partai"));   // kata pertama dianggap langkah
        Assert.Equal((null, ""), koleksi.Tempel("[Event \"Kosong\"]"));

        koleksi.Tempel("1. e4  e5 2. Nf3 *");   // partai yang sama
        Assert.Single(koleksi.Semua());
    }

    [Fact]
    public async Task PartaiBertanggalDuluTempelanTerakhir()
    {
        koleksi.Akun.Simpan("kevin_uji", "kevin_cc");
        koleksi.Tempel("1. d4 d5 *");
        jaringan.Jawab = p => p.Alamat.Contains("lichess", StringComparison.Ordinal) ? Teks(UjiPgn.Lichess)
            : p.Alamat.EndsWith("/archives", StringComparison.Ordinal) ? Teks("""{"archives":["https://api.chess.com/pub/player/kevin_cc/games/2026/10"]}""")
            : Teks(UjiPgn.ChessCom);
        await koleksi.AmbilLichess("kevin_uji", default);
        await koleksi.AmbilChessCom("kevin_cc", default);

        Assert.Equal(["lichess", "chesscom", "pgn"], koleksi.Semua().Select(p => p.Id.Split('-')[0]));

        // Akun yang dilepas tidak tampil lagi (berkasnya tetap).
        koleksi.Akun.Simpan(null, "kevin_cc");
        Assert.Equal(["chesscom", "pgn"], koleksi.Semua().Select(p => p.Id.Split('-')[0]));
    }

    [Fact]
    public async Task SatuPartaiLichessDariTautan()
    {
        jaringan.Jawab = _ => Teks(UjiPgn.Lichess);
        var p = await koleksi.AmbilSatuLichess("abcdEFGH", default);
        Assert.Equal("https://lichess.org/game/export/abcdEFGH?evals=true&opening=true&clocks=false", Assert.Single(jaringan.Permintaan).Alamat);
        Assert.Equal("lichess-abcdEFGH", p?.Id);
        Assert.Single(koleksi.Semua());   // tersimpan bersama tempelan
    }

    [Fact]
    public async Task VarianDilewati()
    {
        koleksi.Akun.Simpan(null, "kevin_cc");
        var chess960 = UjiPgn.ChessCom.Replace("[Event \"Live Chess\"]", "[Event \"Live Chess - Chess960\"]\n[Variant \"Chess960\"]")
            .Replace("123456789", "999");
        jaringan.Jawab = p => p.Alamat.EndsWith("/archives", StringComparison.Ordinal)
            ? Teks("""{"archives":["https://api.chess.com/pub/player/kevin_cc/games/2026/10"]}""")
            : Teks(UjiPgn.ChessCom + "\n" + chess960);

        Assert.Equal(1, await koleksi.AmbilChessCom("kevin_cc", default));
        Assert.Equal("chesscom-123456789", Assert.Single(koleksi.Semua()).Id);
    }

    [Theory]
    [InlineData("https://lichess.org/abcdEFGH", "abcdEFGH")]
    [InlineData("lichess.org/abcdEFGH/black", "abcdEFGH")]
    [InlineData("https://lichess.org/abcdEFGHijkl", "abcdEFGH")]
    [InlineData("https://lichess.org/abcdEFGH#32", "abcdEFGH")]
    [InlineData("https://lichess.org/analysis", null)]
    [InlineData("https://lichess.org/@/kevin", null)]
    [InlineData("https://lichess.org/abc", null)]
    [InlineData("1. e4 e5 https://lichess.org/abcdEFGH", null)]
    [InlineData("https://www.chess.com/game/live/123", null)]
    public void IdDariTautanLichess(string teks, string? harapan) => Assert.Equal(harapan, KoleksiPartai.IdLichess(teks));

    [Theory]
    [InlineData("kevin_uji", true)]
    [InlineData("Kevin-2010", true)]
    [InlineData("k", false)]
    [InlineData("nama dengan spasi", false)]
    [InlineData("../rahasia", false)]
    public void NamaAkun(string nama, bool sah) => Assert.Equal(sah, AkunCatur.NamaSah(nama));

    [Fact]
    public void AkunTersimpan()
    {
        koleksi.Akun.Simpan(" kevin_uji ", "");
        var lagi = new AkunCatur(Path.Combine(folder, "akun.tsv"));
        Assert.Equal(("kevin_uji", (string?)null, true), (lagi.Lichess, lagi.ChessCom, lagi.Ada));
    }
}
