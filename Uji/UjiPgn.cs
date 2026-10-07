using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiPgn
{
    // Seperti ekspor Lichess untuk partai yang sudah dianalisis: nilai mesin,
    // tanda, komentar penilaian, dan variasi.
    internal const string Lichess = """
        [Event "Rated blitz game"]
        [Site "https://lichess.org/abcdEFGH"]
        [Date "2026.10.05"]
        [White "kevin_uji"]
        [Black "lawan99"]
        [Result "0-1"]
        [UTCDate "2026.10.05"]
        [UTCTime "13:01:02"]
        [WhiteElo "1580"]
        [BlackElo "1602"]
        [TimeControl "300+0"]
        [ECO "C20"]
        [Opening "King's Pawn Game"]
        [Termination "Normal"]

        1. e4 { [%eval 0.2] } 1... e5 { [%eval 0.25] } 2. Qh5?! { [%eval -0.3] } { (0.25 → -0.30) Inaccuracy. Nf3 was best. } (2. Nf3 Nc6 { [%eval 0.3] })
        2... Nc6 { [%eval -0.25] } 3. Bc4 { [%eval -0.2] } 3... g6?? $4 { [%eval 5.6] } (3... Nf6 4. Qxf7#) 4. Qf3 { [%eval -0.1] } 4... Nf6
        5. g4 { [%eval #-2] } 0-1


        """;

    // Seperti arsip bulanan Chess.com: jam di komentar, tautan di Link.
    internal const string ChessCom = """
        [Event "Live Chess"]
        [Site "Chess.com"]
        [Date "2026.10.04"]
        [Round "-"]
        [White "lawan_cc"]
        [Black "kevin_cc"]
        [Result "1/2-1/2"]
        [ECO "B90"]
        [ECOUrl "https://www.chess.com/openings/Sicilian-Defense-Najdorf-Variation"]
        [UTCDate "2026.10.04"]
        [UTCTime "10:00:00"]
        [WhiteElo "1500"]
        [BlackElo "1490"]
        [TimeControl "600"]
        [Link "https://www.chess.com/game/live/123456789"]

        1. e4 {[%clk 0:09:58.1]} 1... c5 {[%clk 0:09:57.2]} 2. Nf3 {[%clk 0:09:50]} 2... d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 1/2-1/2

        """;

    [Fact]
    public void PisahDanKepala()
    {
        var semua = Pgn.Pisah(Lichess + ChessCom);

        Assert.Equal(2, semua.Count);
        var (l, c) = (semua[0], semua[1]);
        Assert.Equal(("kevin_uji", "lawan99", "0-1"), (l.Putih, l.Hitam, l.Hasil));
        Assert.Equal("lichess-abcdEFGH", l.Id);
        Assert.Equal("https://lichess.org/abcdEFGH", l.Tautan);
        Assert.Equal(new DateTime(2026, 10, 5, 13, 1, 2), l.Waktu);
        Assert.Equal("chesscom-123456789", c.Id);
        Assert.Equal("https://www.chess.com/game/live/123456789", c.Tautan);
        Assert.Null(c["Round"]);   // "-" dianggap kosong
        Assert.StartsWith("[Event \"Live Chess\"]\n", c.Pgn);
        Assert.EndsWith("5. Nc3 a6 1/2-1/2\n", c.Pgn);
    }

    [Fact]
    public void UraiDenganNilaiTandaDanKomentar()
    {
        var u = Pgn.Pisah(Lichess)[0].Urai();

        Assert.Null(u.Galat);
        Assert.Equal(["e4", "e5", "Qh5", "Nc6", "Bc4", "g6", "Qf3", "Nf6", "g4"], u.Langkah.Select(l => l.San));
        Assert.Equal(0.2, u.Langkah[0].Nilai);
        Assert.Equal(("?!", "(0.25 → -0.30) Inaccuracy. Nf3 was best."), (u.Langkah[2].Tanda, u.Langkah[2].Komentar));
        Assert.Equal("??", u.Langkah[5].Tanda);
        Assert.Equal(5.6, u.Langkah[5].Nilai);
        Assert.Equal((-2, (double?)null), (u.Langkah[8].Mat, u.Langkah[8].Nilai));
        Assert.Null(u.Langkah[7].Nilai);
        Assert.Equal("e2e4", u.Langkah[0].Langkah.Uci);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", u.Langkah[0].Fen);
    }

    [Fact]
    public void UraiChessComTanpaJam()
    {
        var u = Pgn.Pisah(ChessCom)[0].Urai();

        Assert.Null(u.Galat);
        Assert.Equal(10, u.Langkah.Count);
        Assert.All(u.Langkah, l => Assert.Null(l.Komentar));
        Assert.Equal("rnbqkb1r/1p2pppp/p2p1n2/8/3NP3/2N5/PPP2PPP/R1BQKB1R w KQkq - 0 6", u.Langkah[^1].Fen);
    }

    [Fact]
    public void LangkahTidakSahMenghentikanUraian()
    {
        var u = Pgn.Pisah("1. e4 e5 2. Ke3 Nc6")[0].Urai();
        Assert.Equal(2, u.Langkah.Count);
        Assert.Equal("2. Ke3", u.Galat);
    }

    [Fact]
    public void TanpaKepalaDanNomorMenempel()
    {
        var p = Assert.Single(Pgn.Pisah("1.d4 d5 2.c4 dxc4 *"));
        Assert.StartsWith("pgn-", p.Id);
        Assert.Equal(["d4", "d5", "c4", "dxc4"], p.Urai().Langkah.Select(l => l.San));
        Assert.Equal(p.Id, Pgn.Pisah("1. d4  d5\n2. c4 dxc4 *")[0].Id);   // spasi lain, partai sama
    }

    [Fact]
    public void PosisiAwalDariFen()
    {
        var p = Pgn.Pisah("""
            [SetUp "1"]
            [FEN "6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1"]

            1. Ra8# 1-0
            """)[0];
        var u = p.Urai();
        Assert.Equal("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", u.FenAwal);
        Assert.Equal("Ra8#", Assert.Single(u.Langkah).San);
    }

    [Fact]
    public void KomentarBerisiKurungSikuTidakDianggapKepala()
    {
        var semua = Pgn.Pisah("[White \"A\"]\n\n1. e4 {komentar\n[bukan kepala]} e5 *\n");
        var p = Assert.Single(semua);
        Assert.Equal(["e4", "e5"], p.Urai().Langkah.Select(l => l.San));
        Assert.Equal("komentar [bukan kepala]", p.Urai().Langkah[0].Komentar);
    }
}
