using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiPapan
{
    // Jumlah posisi sesudah n langkah: angka baku untuk menguji aturan catur
    // (rokade, en passant, promosi, skak). Sumber: chessprogramming.org/Perft_Results.
    static long Perft(Papan papan, int kedalaman)
    {
        if (kedalaman == 0)
            return 1;
        var sah = papan.LangkahSah();
        if (kedalaman == 1)
            return sah.Count;
        long jumlah = 0;
        foreach (var l in sah)
            jumlah += Perft(papan.Jalankan(l), kedalaman - 1);
        return jumlah;
    }

    [Theory]
    [InlineData(Papan.FenAwal, 3, 8902)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039)]   // "Kiwipete"
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 3, 2812)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 2, 2079)]
    public void PerftSesuaiAngkaBaku(string fen, int kedalaman, long harapan) =>
        Assert.Equal(harapan, Perft(Papan.DariFen(fen)!, kedalaman));

    [Theory]
    [InlineData(Papan.FenAwal)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("8/8/8/4k3/8/8/8/4K3 b - - 12 57")]
    public void FenBolakBalik(string fen) => Assert.Equal(fen, Papan.DariFen(fen)!.Fen());

    [Theory]
    [InlineData("")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQ1BNR w KQkq - 0 1")]   // raja putih tidak ada
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    public void FenRusakDitolak(string fen) => Assert.Null(Papan.DariFen(fen));

    static string San(string fen, string uci)
    {
        var papan = Papan.DariFen(fen)!;
        return papan.San(papan.DariNotasi(uci)!.Value);
    }

    [Theory]
    [InlineData(Papan.FenAwal, "g1f3", "Nf3")]
    [InlineData(Papan.FenAwal, "e2e4", "e4")]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/8/PPP2PPP/RNBQKBNR w KQkq - 1 3", "d4e5", "dxe5")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", "O-O-O")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "a1a8", "Ra8+")]
    [InlineData("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8", "Ra8#")]
    [InlineData("4k3/8/8/8/8/8/K7/R6R w - - 0 1", "a1d1", "Rad1")]                    // pembeda kolom
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w - - 0 1", "a1d1", "Rd1")]                     // h1 terhalang raja
    [InlineData("4k3/R7/8/8/8/8/8/R3K3 w - - 0 1", "a1a4", "R1a4")]                   // pembeda baris
    [InlineData("4k3/8/8/8/8/Q1Q5/8/Q3K3 w - - 0 1", "a3b2", "Qa3b2")]                // keduanya
    [InlineData("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", "b7b8q", "b8=Q+")]
    [InlineData("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", "b7b8n", "b8=N")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "e5d6", "exd6")]                  // en passant
    public void NotasiSan(string fen, string uci, string harapan) => Assert.Equal(harapan, San(fen, uci));

    [Theory]
    [InlineData("Nf3", "g1f3")]
    [InlineData("Ng1f3", "g1f3")]       // pembeda berlebih
    [InlineData("N1f3", "g1f3")]
    [InlineData("e4", "e2e4")]
    [InlineData("e2-e4", "e2e4")]
    [InlineData("e2e4", "e2e4")]        // UCI
    [InlineData("Nf3!?", "g1f3")]
    public void NotasiPemaaf(string notasi, string uci) =>
        Assert.Equal(uci, Papan.Awal().DariNotasi(notasi)?.Uci);

    [Theory]
    [InlineData("Nf4")]
    [InlineData("e5")]
    [InlineData("Ke2")]
    [InlineData("O-O")]
    [InlineData("zz")]
    public void NotasiTidakSah(string notasi) => Assert.Null(Papan.Awal().DariNotasi(notasi));

    [Fact]
    public void NotasiBerduaDitolakKalauTidakJelas()
    {
        var papan = Papan.DariFen("4k3/8/8/8/8/8/K7/R6R w - - 0 1")!;
        Assert.Null(papan.DariNotasi("Rd1"));   // a1 atau h1?
        Assert.Equal("a1d1", papan.DariNotasi("Rad1")?.Uci);
        Assert.Equal("e1g1", Papan.DariFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")!.DariNotasi("0-0")?.Uci);
        Assert.Equal("b7b8q", Papan.DariFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1")!.DariNotasi("b8")?.Uci);   // promosi tanpa bidak: menteri
    }

    [Fact]
    public void RokadeHilangSesudahRajaAtauBentengPindah()
    {
        var papan = Papan.DariFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")!;
        Assert.Equal("r3k2r/8/8/8/8/8/8/R3K1R1 b Qkq - 1 1", papan.Jalankan(papan.DariNotasi("Rg1")!.Value).Fen());
        var sesudah = papan.Jalankan(papan.DariNotasi("Rxa8+")!.Value);
        Assert.Equal("R3k2r/8/8/8/8/8/8/4K2R b Kk - 0 1", sesudah.Fen());
    }
}
