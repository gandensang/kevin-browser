using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KevinBrowser;
using KevinBrowser.Asisten;

namespace Uji;

// Tebak langkah (kevin://catur?latihan=…) bersama pelatih AI, dan pemasangan Stockfish.
public sealed partial class UjiHalamanCatur
{
    const string Latihan = "kevin://catur?latihan=lichess-abcdEFGH";

    // Posisi sesudah 1. e4 e5 2. Qh5 Nc6 3. Bc4 g6 (putih melangkah; di partai 4. Qf3).
    const string FenSoal = "r1bqkbnr/pppp1p1p/2n3p1/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 0 4";

    [GeneratedRegex("sesi=([0-9A-F]{16})")]
    private static partial Regex Sesi();

    // Akun, partai, dan (kalau diminta) mesin dan kunci DeepSeek. Permintaan
    // ke DeepSeek dijawab berurutan dari `deepseek`.
    async Task SiapkanLatihan(bool mesin = true, bool kunci = true, params (int, IEnumerable<string>)[] deepseek)
    {
        JaringanCatur();
        await Hubungkan("kevin_uji", "");
        if (mesin)
            layanan.PasangMesinTiruan();
        if (kunci)
            layanan.PengaturanAi.Simpan("deepseek-v4-pro", "sk-uji-1234567890");
        var i = 0;
        layanan.Jaringan.Jawab = p => p.Alamat.StartsWith("https://api.deepseek.com", StringComparison.Ordinal)
            ? deepseek[Math.Min(i++, deepseek.Length - 1)]
            : Teks(UjiPgn.Lichess);
    }

    async Task<JsonElement> Data(string uri, string post)
    {
        var (isi, jenis) = await HalamanBawaan.Ambil(uri, layanan, post);
        Assert.Equal("application/json", jenis);
        using var dok = JsonDocument.Parse(isi);
        return dok.RootElement.Clone();
    }

    JsonElement PesanDeepSeek(int ke) => BadanDeepSeek(ke).GetProperty("messages");

    JsonElement BadanDeepSeek(int ke)
    {
        var permintaan = layanan.Jaringan.Permintaan.Where(p => p.Alamat.StartsWith("https://api.deepseek.com", StringComparison.Ordinal)).ToList();
        using var dok = JsonDocument.Parse(permintaan[ke].Isi!);
        return dok.RootElement.Clone();
    }

    [Fact]
    public async Task LatihanTanpaMesinMintaPasang()
    {
        await SiapkanLatihan(mesin: false);
        var html = await Html(Latihan);
        Assert.Contains("Pasang Stockfish 19</button>", html);
        Assert.Contains("name=\"kembali\" value=\"kevin://catur?latihan=lichess-abcdEFGH&amp;sisi=putih\"", html);
        Assert.Contains("id=\"pasang-mesin\"", html);   // tombolnya mati dan kemajuan tampil selama mengunduh
        Assert.DoesNotContain("latihan.js", html);

        // Berkas yang isinya lain dari sidik yang dicatat tidak dipakai.
        layanan.Jaringan.JawabUnduh = p => (200, new byte[p.Alamat.EndsWith(".wasm", StringComparison.Ordinal) ? 1_785_285 : 21_415]);
        var hasil = await Html("kevin://catur?mesin", Post(("aksi", "pasang"), ("token", Ambil(Token(), html)), ("kembali", "kevin://catur?latihan=x")));
        Assert.Contains("tidak sama dengan yang diharapkan", hasil);
        Assert.False(layanan.MesinCatur.Terpasang);
        Assert.Equal("https://github.com/gandensang/kevin-browser/releases/download/stockfish-19-tanpa-simd/stockfish-19-lite-tanpa-simd.js",
            layanan.Jaringan.Permintaan[^1].Alamat);

        layanan.Jaringan.JawabUnduh = _ => throw new GalatAi(0, "tidak ada jaringan");
        hasil = await Html("kevin://catur?mesin", Post(("aksi", "pasang"), ("token", Ambil(Token(), hasil))));
        Assert.Contains("GitHub tidak bisa dihubungi", hasil);
    }

    [Fact]
    public async Task MesinDipasangSekaliDenganKemajuan()
    {
        var tahan = new TaskCompletionSource();
        layanan.Jaringan.JawabUnduh = _ =>
        {
            tahan.Task.Wait();
            return (404, []);
        };
        var pertama = layanan.MesinCatur.Pasang(default);
        Assert.Same(pertama, layanan.MesinCatur.Pasang(default));   // klik kedua menunggu yang sama
        var kemajuan = await Data("kevin://catur?mesin&kemajuan", "");
        Assert.Equal(MesinCatur.Ukuran, kemajuan.GetProperty("total").GetInt64());
        tahan.SetResult();
        await Assert.ThrowsAsync<GalatAi>(() => pertama);
        Assert.Equal(0, (await Data("kevin://catur?mesin&kemajuan", "")).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task PerantaraMesin()
    {
        var (isi, jenis) = await HalamanBawaan.Ambil("kevin-mesin://mesin/perantara.html", layanan, null);
        var html = Encoding.UTF8.GetString(isi);
        Assert.Equal("text/html", jenis);
        // Mesin buatan sendiri tanpa WASM SIMD (prosesor tanpa AVX), dan galatnya berkode.
        Assert.Contains("new Worker('stockfish-19-lite-tanpa-simd.js#' + encodeURIComponent(new URL('stockfish-19-lite-tanpa-simd.wasm', location.href)))", html);
        Assert.Contains("lapor('WASM')", html);
        Assert.Contains("lapor('MUAT', e.message)", html);

        layanan.PasangMesinTiruan();
        (isi, jenis) = await HalamanBawaan.Ambil("kevin-mesin://mesin/stockfish-19-lite-tanpa-simd.wasm", layanan, null);
        Assert.Equal("application/wasm", jenis);
        Assert.Equal(1_785_285, isi.Length);
    }

    [Fact]
    public async Task HalamanMesin()
    {
        Assert.Contains("Pasang Stockfish 19", await Html("kevin://catur?mesin"));
        layanan.PasangMesinTiruan();
        var html = await Html("kevin://catur?mesin&kembali=" + Uri.EscapeDataString(Latihan));
        Assert.Contains("Stockfish 19 sudah terpasang.", html);
        Assert.Contains($"href=\"{Latihan}\">Kembali ke latihan", html);
        Assert.DoesNotContain("Kembali ke latihan", await Html("kevin://catur?mesin&kembali=https://contoh.com"));

        var hasil = await Html("kevin://catur?mesin", Post(("aksi", "lepas"), ("token", Ambil(Token(), html))));
        Assert.Contains("Stockfish 19 sudah dilepas.", hasil);
        Assert.False(layanan.MesinCatur.Terpasang);
    }

    [Fact]
    public async Task LatihanTanpaKunciMintaKunci()
    {
        await SiapkanLatihan(kunci: false);
        var html = await Html(Latihan);
        Assert.Contains("href=\"kevin://belajar?ai\"", html);
        Assert.Contains("Atur kunci DeepSeek", html);
        Assert.DoesNotContain("latihan.js", html);
    }

    [Fact]
    public async Task HalamanLatihan()
    {
        await SiapkanLatihan();
        var html = await Html(Latihan);

        // Partai sendiri sebagai putih; papan hanya gambar, obrolan di sampingnya.
        Assert.Contains("<a class=\"pilihan aktif\" href=\"kevin://catur?latihan=lichess-abcdEFGH&amp;sisi=putih\" aria-current=\"true\">Sebagai putih</a>", html);
        Assert.Contains("<script src=\"kevin://papan.js\"></script>", html);
        Assert.Contains("<script src=\"kevin://latihan.js\"></script>", html);
        Assert.Contains("\"perantara\":\"kevin-mesin://mesin/perantara.html\"", html);
        Assert.Contains("\"soal\":-1", html);
        Assert.Contains("\"no\":\"3...\"", html);
        Assert.Contains("id=\"pesan\" rows=\"1\" disabled", html);
        Assert.Contains("id=\"simpan\" disabled>", html);
        Assert.Contains("Posisi berikutnya", html);
        Assert.DoesNotContain("Petunjuk", html);
        Assert.Matches(Sesi(), html);
        // Model pelatih terkunci (keputusan pemakai), jadi tidak ada tautan "ubah".
        Assert.Contains("Pelatih: deepseek-v4-pro, mode berpikir · dinilai Stockfish 19 di laptop ini", html);
        Assert.DoesNotContain("belajar?ai", html);

        Assert.Contains("class=\"kolom-papan terbalik\"", await Html(Latihan + "&sisi=hitam"));
    }

    [Fact]
    public async Task ObrolanDenganPelatih()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qxe5+","variasi":["Qxe5+ Nxe5"]}""")),
            JaringanPalsu.Tool("Sayang, **4. Qxe5+** kehilangan menteri: ⚙️ 4... Nxe5.\n\nPelajaran: sebelum memakan, periksa siapa yang menjaga petak itu."),
            JaringanPalsu.Tool("Karena kuda c6 menjaga e5."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";

        // Soal: posisi sebelum langkah 4 putih (ply 6).
        Assert.True((await Data(alamat + "&soal", "ply=5")).TryGetProperty("galat", out _));   // giliran hitam
        var soal = (await Data(alamat + "&soal", "ply=6")).GetProperty("html").GetString()!;
        Assert.Contains("<strong>Soal 1</strong> · Langkah 4, putih melangkah.", soal);
        Assert.Contains("Langkah partai sebelumnya: 1. e4 e5 2. Qh5 Nc6 3. Bc4 g6", soal);

        // Jawaban siswa, dengan analisis awal dari halaman (kandidat dan langkah partai).
        const string awal = """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"},{"uci":"h5e2","cp":-40,"mat":null,"pv":"h5e2 g8f6"},{"uci":"h5d1","cp":-50,"mat":null,"pv":"h5d1 g8f6"}]""";
        var balas = await Data(alamat + "&kirim", Post(("pesan", "Aku main Qxe5+ <karena> skak"), ("awal", awal)));
        Assert.Equal("<p class=\"gelembung siswa\">Aku main Qxe5+ &lt;karena&gt; skak</p>", balas.GetProperty("siswa").GetString());
        var tugas = balas.GetProperty("tugas");
        Assert.Equal(3, tugas.GetArrayLength());
        Assert.Equal((FenSoal, 0, "h5e5"), (tugas[0].GetProperty("fen").GetString(), tugas[0].GetProperty("n").GetInt32(), tugas[0].GetProperty("cari")[0].GetString()));
        Assert.Equal((1, "c6e5"), (tugas[1].GetProperty("n").GetInt32(), tugas[1].GetProperty("cari")[0].GetString()));
        Assert.Equal(0, tugas[2].GetProperty("cari").GetArrayLength());

        var pertama = PesanDeepSeek(0);
        Assert.Contains("pelatih catur", pertama[0].GetProperty("content").GetString());
        // Selalu deepseek-v4-pro dengan mode berpikir, walau model tanya-jawab
        // di pengaturan deepseek-flash (lihat PelatihCatur.ModelPelatih).
        Assert.Equal("deepseek-flash", layanan.PengaturanAi.ModelTanya);
        Assert.Equal("deepseek-v4-pro", BadanDeepSeek(0).GetProperty("model").GetString());
        Assert.Equal("enabled", BadanDeepSeek(0).GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(PelatihCatur.MaksTokenBerpikir, BadanDeepSeek(0).GetProperty("max_tokens").GetInt32());
        var konteks = pertama[1].GetProperty("content").GetString()!;
        Assert.Contains("<<<POSISI", konteks);
        Assert.Contains($"FEN: {FenSoal}", konteks);
        Assert.Contains("1. 4. Qf3 (−0,2): 4. Qf3 4... Nf6", konteks);
        Assert.Contains("Langkah di partai: 4. Qf3 (⚙️ −0,2; langkah yang baik)", konteks);
        Assert.EndsWith("Jawaban siswa:\nAku main Qxe5+ <karena> skak", konteks);

        // Pesan lain selagi Stockfish bekerja ditolak.
        Assert.Contains("masih menjawab", (await Data(alamat + "&kirim", Post(("pesan", "halo")))).GetProperty("galat").GetString());

        // Hasil Stockfish, lalu penjelasan pelatih.
        const string hasil = """[[{"uci":"h5e5","cp":-900,"mat":null,"pv":"h5e5 c6e5"}],[{"uci":"c6e5","cp":900,"mat":null,"pv":"c6e5 f2f4"}],[{"uci":"f2f4","cp":-950,"mat":null,"pv":"f2f4 e5c4"}]]""";
        balas = await Data(alamat + "&lanjut", Post(("hasil", hasil)));
        Assert.Contains("kehilangan menteri", balas.GetProperty("ai").GetString());
        Assert.Equal(1, balas.GetProperty("dinilai").GetInt32());

        var laporan = PesanDeepSeek(1)[3];
        Assert.Equal("tool", laporan.GetProperty("role").GetString());
        var isiLaporan = laporan.GetProperty("content").GetString()!;
        Assert.Contains("⚙️ Terbaik: 4. Qf3 (−0,2): 4. Qf3 4... Nf6", isiLaporan);
        Assert.Contains("Langkah utama siswa: 4. Qxe5+ (⚙️ −9,0): blunder, peluang menang −45% dibanding terbaik. Dibanding langkah partai 4. Qf3 (⚙️ −0,2): lebih buruk.", isiLaporan);
        Assert.Contains("Variasi 1: 4. Qxe5+ 4... Nxe5", isiLaporan);
        Assert.Contains("- Semua langkah lain di variasi ini sesuai ⚙️", isiLaporan);   // 4... Nxe5 balasan terbaik hitam
        Assert.Contains("- Akhir variasi: ⚙️ −9,5; materi hitam unggul 8; lanjutan terbaik 5. f4 5... Nxc4.", isiLaporan);

        // Pesan berikutnya di soal yang sama: tanpa analisis awal, percakapannya berlanjut.
        balas = await Data(alamat + "&kirim", Post(("pesan", "kenapa?")));
        Assert.Contains("kuda c6 menjaga e5", balas.GetProperty("ai").GetString());
        Assert.Equal(6, PesanDeepSeek(2).GetArrayLength());   // sistem, soal, permintaan tool, hasilnya, jawaban, pesan ini

        // Tab dibangunkan lagi: obrolannya masih ada.
        var lagi = await Html(alamat);
        Assert.Contains("<strong>Soal 1</strong>", lagi);
        Assert.Contains("kuda c6 menjaga e5", lagi);
        Assert.Contains("\"soal\":6", lagi);
        Assert.Contains("\"perluAwal\":false", lagi);
        Assert.Contains("id=\"simpan\">", lagi);

        // Simpan ke catatan partai ini, dengan pelajaran dari pelatih.
        var simpan = await Html(alamat, Post(("aksi", "simpan"), ("token", Ambil(Token(), lagi))));
        var catatan = Assert.Single(layanan.Catatan.Semua());
        Assert.Equal(HalamanBelajar.Alamat(catatan), Ambil(Pindah(), simpan));
        var isi = layanan.Catatan.Baca("catur", catatan.Nama)!;
        Assert.Contains("## Pelajaran\n\n### Tebak langkah, 1 Okt 2026 09.00\n\n"
            + "Sebagai putih, dinilai Stockfish 19, dijelaskan deepseek-v4-pro: 1 posisi penting; sama dengan partai 0, setara 0, lebih baik 0, lebih buruk 1.\n\n"
            + "- [4. Qxe5+](kevin://catur?partai=lichess-abcdEFGH#6): lebih buruk dari partai (4. Qf3), blunder, peluang menang −45%. "
            + "⚙️ Terbaik: 4. Qf3 Nf6. FEN `" + FenSoal + "` **Pelajaran:** sebelum memakan, periksa siapa yang menjaga petak itu.\n", isi);
        Assert.DoesNotContain("(Tulis sendiri", isi);
    }

    // Skor hanya ditunjukkan sesudah partai habis, tetapi dihitung sejak soal
    // pertama. Soal yang ditinggalkan tanpa jawaban 0 poin; langkah partainya
    // tetap dinilai sebagai pembanding. Hasilnya tersimpan sendiri ke catatan,
    // satu bagian per latihan walau sebelumnya sudah disimpan manual.
    [Fact]
    public async Task LatihanSelesaiDenganSkorDanKesimpulan()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qf3"}""")),
            JaringanPalsu.Tool("Tepat, **4. Qf3** sama dengan partai.\n\nPelajaran: lindungi menteri sambil mengancam."),
            JaringanPalsu.Tool("Kamu menemukan langkah terbaik di soal pertama, tetapi melewatkan soal kedua."),
        ]);
        layanan.Partai.Skor.Tambah(LayananPalsu.Sekarang.AddDays(-1), "lichess-lain", true,
            new SkorLatihan(Soal: 3, Poin: 6, Dilewati: 0, PoinPartai: 3, SoalPartai: 3));   // latihan sebelumnya 67%
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";

        await Data(alamat + "&soal", "ply=6");
        var ai = (await Data(alamat + "&kirim", Post(("pesan", "Qf3"), ("awal", """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""")))).GetProperty("ai").GetString()!;
        Assert.DoesNotContain("Skor", ai);   // selama latihan skornya tidak ditunjukkan

        // Disimpan manual di tengah latihan: tanpa skor.
        var simpan = await Html(alamat, Post(("aksi", "simpan"), ("token", Ambil(Token(), await Html(alamat)))));
        var catatan = Assert.Single(layanan.Catatan.Semua());
        Assert.Equal(HalamanBelajar.Alamat(catatan), Ambil(Pindah(), simpan));
        Assert.DoesNotContain("Skor", layanan.Catatan.Baca("catur", catatan.Nama));

        // Soal kedua (sebelum 5. g4) ditinggalkan tanpa jawaban, lalu partainya habis.
        await Data(alamat + "&soal", "ply=8");
        const string awal = """[{"uci":"b1c3","cp":-150,"mat":null,"pv":"b1c3 f6g4"},{"uci":"g2g4","cp":-99998,"mat":-2,"pv":"g2g4 d8h4"}]""";
        var akhir = (await Data(alamat + "&selesai", Post(("awal", awal)))).GetProperty("html").GetString()!;
        Assert.Contains("<strong>Partai selesai.</strong> <strong>Skor: 3 dari 6</strong> (2 soal, 1 dilewati, 50%). "
            + "Langkah yang dimainkan di partai: 3 dari 6 (50%). Latihan sebelumnya: 67%.", akhir);
        Assert.Contains("melewatkan soal kedua", akhir);
        Assert.Contains($"Hasilnya tersimpan di <a href=\"{WebUtility.HtmlEncode(HalamanBelajar.Alamat(catatan))}\">catatan partai ini</a>.", akhir);

        // Kesimpulan: satu panggilan v4-pro berpikir, tanpa tool, dari data yang sudah dihitung.
        var badan = BadanDeepSeek(2);
        Assert.False(badan.TryGetProperty("tools", out _));
        Assert.Equal(("deepseek-v4-pro", "enabled"), (badan.GetProperty("model").GetString(), badan.GetProperty("thinking").GetProperty("type").GetString()));
        var data = badan.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("Partai ini dimainkan siswa sendiri (akun kevin_uji), jadi langkah di partai adalah langkahnya sendiri saat bermain.", data);
        Assert.Contains("Skor: 3 dari 6 (2 soal, 1 dilewati, 50%).", data);
        Assert.Contains("Pembanding latihan sebelumnya kasar", data);
        Assert.Contains("- Soal 1, langkah 4: jawaban 4. Qf3, langkah yang baik, peluang menang −0%, 3 poin; langkah partai 4. Qf3 (3 poin); "
            + "⚙️ terbaik 4. Qf3 Nf6; pelajaran: lindungi menteri sambil mengancam.", data);
        Assert.Contains("- Soal 2, langkah 5: dilewati, 0 poin; langkah partai 5. g4 (0 poin); ⚙️ terbaik 5. Nc3 Ng4", data);

        // Catatan: bagian yang sama diganti (satu bagian), sekarang dengan skor dan kesimpulan.
        var isi = layanan.Catatan.Baca("catur", catatan.Nama)!;
        Assert.Equal(1, Regex.Count(isi, "### Tebak langkah"));
        Assert.Contains("### Tebak langkah, 1 Okt 2026 09.00\n\n"
            + "Sebagai putih, dinilai Stockfish 19, dijelaskan deepseek-v4-pro: 2 posisi penting; sama dengan partai 1, setara 0, lebih baik 0, lebih buruk 0, dilewati 1.\n\n"
            + "**Skor: 3 dari 6** (2 soal, 1 dilewati, 50%). Langkah yang dimainkan di partai: 3 dari 6 (50%). Latihan sebelumnya: 67%.\n\n"
            + "**Kesimpulan pelatih:** Kamu menemukan langkah terbaik di soal pertama, tetapi melewatkan soal kedua.\n\n"
            + "- [4. Qf3](kevin://catur?partai=lichess-abcdEFGH#6): sama dengan partai, langkah yang baik. FEN `" + FenSoal + "` **Pelajaran:** lindungi menteri sambil mengancam.\n"
            + "- [5. ?](kevin://catur?partai=lichess-abcdEFGH#8): dilewati; di partai 5. g4. ⚙️ Terbaik: 5. Nc3 Ng4.", isi);
        Assert.Equal((2, 58), layanan.Partai.Skor.RataRata(5));   // 67% dan 50%

        // Sekali saja: tidak ada penilaian kedua, dan obrolannya tetap memuatnya.
        var lagi = await Data(alamat + "&selesai", Post(("awal", "")));
        Assert.True(lagi.GetProperty("selesai").GetBoolean());
        Assert.False(lagi.TryGetProperty("html", out _));
        Assert.Equal(3, layanan.Jaringan.Permintaan.Count(p => p.Alamat.StartsWith("https://api.deepseek.com", StringComparison.Ordinal)));
        Assert.Contains("Skor: 3 dari 6", await Html(alamat));
    }

    // Semua soal dilewati: skornya tetap ditunjukkan, tanpa kesimpulan AI,
    // dan tidak ada yang disimpan.
    [Fact]
    public async Task LatihanTanpaJawabanTidakDisimpan()
    {
        await SiapkanLatihan(deepseek: [JaringanPalsu.Tool("tidak dipakai")]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");
        var akhir = (await Data(alamat + "&selesai", Post(("awal", """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""")))).GetProperty("html").GetString()!;
        Assert.Contains("<strong>Skor: 0 dari 3</strong> (1 soal, 1 dilewati, 0%). Langkah yang dimainkan di partai: 3 dari 3 (100%).", akhir);
        Assert.Contains("Semua soal dilewati", akhir);
        Assert.Empty(layanan.Catatan.Semua());
        Assert.Null(layanan.Partai.Skor.RataRata(5));
        Assert.DoesNotContain(layanan.Jaringan.Permintaan, p => p.Alamat.StartsWith("https://api.deepseek.com", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 0)]
    [InlineData(45, 0)]
    public void PoinPerSoal(int hilang, int poin) => Assert.Equal(poin, PelatihCatur.Poin(hilang));

    // Satu latihan satu bagian: judul yang sama diganti sampai judul berikutnya,
    // bagian lain dan isi di bawahnya tetap. Latihan lain di menit yang sama
    // mendapat judul bernomor.
    [Fact]
    public void BagianLatihanDiganti()
    {
        var t = new Teks(Bahasa.Indonesia);
        const string isi = "# Partai\n\n## Pelajaran\n\n### Tebak langkah, 1 Okt 2026 09.00\n\nlama\n- baris lama\n\n### Tebak langkah, 2 Okt 2026 10.00\n\nlain\n";
        Assert.Equal("# Partai\n\n## Pelajaran\n\n### Tebak langkah, 1 Okt 2026 09.00\n\nbaru\n\n### Tebak langkah, 2 Okt 2026 10.00\n\nlain\n",
            HalamanCatur.GantiBagian(t, isi, "### Tebak langkah, 1 Okt 2026 09.00\n\nbaru\n"));
        Assert.EndsWith("lain\n\n### Tebak langkah, 3 Okt 2026 08.00\n\nketiga\n",
            HalamanCatur.GantiBagian(t, isi, "### Tebak langkah, 3 Okt 2026 08.00\n\nketiga\n"));
        Assert.Equal("### Tebak langkah, 1 Okt 2026 09.00 (2)", HalamanCatur.JudulBagian(t, isi, LayananPalsu.Sekarang));
        Assert.Equal("### Tebak langkah, 1 Okt 2026 09.00", HalamanCatur.JudulBagian(t, "", LayananPalsu.Sekarang));
    }

    // Variasi panjang: setiap langkah diperiksa, juga yang jauh dari posisi
    // soal. Langkah pihak siswa yang keliru dan balasan lawan yang lemah
    // sama-sama dilaporkan.
    [Fact]
    public async Task SetiapLangkahVariasiPanjangDiperiksa()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qf3","variasi":["4. Qf3 Nf6 5. g4 Nd4 6. Qd3 d5"]}""")),
            JaringanPalsu.Tool("Variasimu patah di 5. g4."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        const string awal = """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""";
        var tugas = (await Data(alamat + "&kirim", Post(("pesan", "Qf3 lalu g4 dan Qd3"), ("awal", awal)))).GetProperty("tugas");
        Assert.Equal(6, tugas.GetArrayLength());   // lima posisi di tengah variasi dan posisi akhirnya
        Assert.Equal(["g8f6", "g2g4", "c6d4", "f3d3", "d7d5"], tugas.EnumerateArray().Take(5).Select(x => x.GetProperty("cari")[0].GetString()));

        const string hasil = """
            [[{"uci":"g8f6","cp":20,"mat":null,"pv":"g8f6 g2g4"}],
             [{"uci":"g1e2","cp":50,"mat":null,"pv":"g1e2 d7d6"},{"uci":"g2g4","cp":-300,"mat":null,"pv":"g2g4 c6d4"}],
             [{"uci":"f6g4","cp":400,"mat":null,"pv":"f6g4 f3g3"},{"uci":"c6d4","cp":100,"mat":null,"pv":"c6d4 f3d3"}],
             [{"uci":"f3d3","cp":-100,"mat":null,"pv":"f3d3 d7d5"}],
             [{"uci":"d7d5","cp":100,"mat":null,"pv":"d7d5 c4b5"}],
             [{"uci":"c4b5","cp":-150,"mat":null,"pv":"c4b5 c8d7"}]]
            """;
        await Data(alamat + "&lanjut", Post(("hasil", hasil)));

        var laporan = PesanDeepSeek(1)[3].GetProperty("content").GetString()!;
        Assert.Contains("Variasi 1: 4. Qf3 4... Nf6 5. g4 5... Nd4 6. Qd3 6... d5\n", laporan);
        Assert.Contains("- 5. g4 (⚙️ −3,0): langkah putih (pihak siswa) ini kesalahan (peluang menang −30%); ⚙️ lebih baik 5. Ne2 5... d6 (+0,5).\n", laporan);
        Assert.Contains("- 5... Nd4 (⚙️ −1,0): bukan langkah terbaik hitam (peluang menang −22%), jadi variasi ini mengandaikan lawan bermain lebih lemah; ⚙️ yang lebih kuat 5... Nxg4 6. Qg3 (−4,0).\n", laporan);
        Assert.Contains("- Akhir variasi: ⚙️ −1,5; materi seimbang; lanjutan terbaik 7. Bb5+ 7... Bd7.", laporan);
        Assert.DoesNotContain("6. Qd3 (⚙️", laporan);   // langkah terbaik: tidak dicatat
    }

    // Di posisi yang sudah kalah, memberikan menteri hanya mengubah peluang
    // menang sedikit; selisih nilainya tetap harus dicatat. Juga "x" ke petak
    // kosong.
    [Fact]
    public async Task HadiahLawanDiPosisiKalahTetapDicatat()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qf3","variasi":["Qf3 Nf6 Qxf6"]}""")),
            JaringanPalsu.Tool("Oke."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");
        await Data(alamat + "&kirim", Post(("pesan", "Qf3"), ("awal", """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""")));

        // 5. Qxf6?? Qxf6 menukar menteri dengan kuda. Nilainya dibuat −8 lawan
        // −12 dari sudut putih: peluang menangnya sama-sama hampir 0.
        const string hasil = """
            [[{"uci":"g8f6","cp":20,"mat":null,"pv":"g8f6 g2g4"}],
             [{"uci":"f3e2","cp":-800,"mat":null,"pv":"f3e2 d7d6"},{"uci":"f3f6","cp":-1200,"mat":null,"pv":"f3f6 d8f6"}],
             [{"uci":"d8f6","cp":1200,"mat":null,"pv":"d8f6 g1f3"}]]
            """;
        await Data(alamat + "&lanjut", Post(("hasil", hasil)));

        var laporan = PesanDeepSeek(1)[3].GetProperty("content").GetString()!;
        Assert.Contains("- 5. Qxf6 (⚙️ −12,0): langkah putih (pihak siswa) ini bukan yang terbaik (selisih ⚙️ 4,0 pion); ⚙️ lebih baik 5. Qe2 5... d6 (−8,0).", laporan);
    }

    [Fact]
    public void AlasanLangkahTidakSahDiTengahVariasi()
    {
        var t = new Teks(Bahasa.Indonesia);
        var p = Papan.DariFen(FenPartaiPengguna)!;
        foreach (var san in "Nxe5 Nxe5 Qxe5 Bd2".Split(' '))
            p = p.Jalankan(p.DariNotasi(san)!.Value);
        Assert.EndsWith("; petak b2: pion putih; menteri e5 ke b2 terhalang pion hitam di d4", PelatihCatur.TidakSah(p, "Qxb2", t));

        // Dua benteng bisa ke petak yang sama.
        var dua = Papan.DariFen("6k1/8/8/8/8/8/5PPP/R4RK1 w - - 0 1")!;
        Assert.Contains("notasinya kurang jelas, lebih dari satu benteng bisa ke d1: Rad1, Rfd1", PelatihCatur.TidakSah(dua, "Rd1", t));

        // Bidak yang terpaku: jalannya bebas, tetapi raja sendiri akan terkena skak.
        var paku = Papan.DariFen("4r1k1/8/8/8/8/8/4B3/4K3 w - - 0 1")!;
        Assert.Contains("gajah e2 ke d3 membuat raja sendiri terkena skak", PelatihCatur.TidakSah(paku, "Bd3", t));
    }

    [Fact]
    public void FaktaStrukturDihitung()
    {
        var t = new Teks(Bahasa.Indonesia);
        var struktur = PelatihCatur.Struktur(Papan.DariFen(FenPartaiPengguna)!, t);
        Assert.Contains("Pion putih: terisolasi tidak ada; ganda tidak ada; lolos tidak ada.\n", struktur);
        Assert.Contains("Pion hitam: terisolasi tidak ada; ganda d4, d7; lolos d4.\n", struktur);
        Assert.Contains("Diserang lawan tanpa pelindung: tidak ada.\n", struktur);

        // Kuda c6 yang kehilangan pelindungnya: menteri c7 dan pion d7 pergi.
        var lepas = PelatihCatur.Struktur(Papan.DariFen("r4rk1/p3bppp/1pn1p3/4P3/B1Pp4/P2Q1N2/1P3PPP/R1B2RK1 b - - 1 14")!, t);
        Assert.Contains("Diserang lawan tanpa pelindung: kuda hitam c6.\n", lepas);
    }

    [Fact]
    public void FaktaSeranganDihitung()
    {
        var t = new Teks(Bahasa.Indonesia);
        var p = Papan.DariFen(FenPartaiPengguna)!;
        var serangan = PelatihCatur.Serangan(p, t);
        Assert.Contains("Putih menyerang: ", serangan);
        Assert.Contains("kuda hitam c6 (oleh gajah a4)", serangan);
        Assert.Contains("pion hitam d4 (oleh menteri d3, kuda f3)", serangan);

        var sesi = new SesiLatihan(Pgn.Pisah("1. Nf3 Nf6 2. c4 e6 3. a3 b6 4. Nc3 Bb7 5. e4 Be7 6. e5 Ne4 7. Nxe4 Bxe4 8. Qe2 Bb7 9. d4 O-O 10. Qd3 c5 11. Be2 Nc6 12. O-O Qc7 13. Bd1 cxd4 14. Ba4 Nxe5 *").Single(),
            false) { Soal = 27 };
        var sesudah = PelatihCatur.LihatPosisi(sesi, """{"langkah":"Nxe5"}""", t);
        Assert.Contains("Hitam menyerang: pion putih a3 (oleh gajah e7); menteri putih d3 (oleh kuda e5); kuda putih f3 (oleh kuda e5, gajah b7); pion putih c4 (oleh kuda e5, menteri c7).", sesudah);
        Assert.Contains("Diserang lawan tanpa pelindung: menteri putih d3, pion hitam d4.", sesudah);
    }

    [Theory]
    [InlineData("Sesudah 16. Bd2, lalu hitam 16... Be4 17. Qe2 d3 18. Qe3.", "16.Bd2 16...Be4 17.Qe2 17...d3 18.Qe3")]
    [InlineData("Nilainya 4.7 dan langkah 5 bagus; 12...Nxe5+ menang.", "12...Nxe5+")]
    [InlineData("Pada langkah 15. Hitam bermain hati-hati.", "")]
    public void LangkahBernomorDibaca(string teks, string harapan) =>
        Assert.Equal(harapan, string.Join(' ', PelatihCatur.LangkahBernomor(teks).Select(l => $"{l.Nomor}{(l.Putih ? "." : "...")}{l.San}")));

    [Fact]
    public async Task LangkahBernomorYangTidakSahDikembalikanKeAi()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qf3","variasi":["Qf3 Nf6"]}""")),
            JaringanPalsu.Tool("Bagus. Kalau **5. Qxf7+**, hitam kalah."),
            JaringanPalsu.Tool("Sudah kuperiksa: menteri f3 terhalang kuda f6."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");
        await Data(alamat + "&kirim", Post(("pesan", "Qf3"), ("awal", """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""")));
        var tugas = (await Data(alamat + "&kirim", Post(("pesan", "x")))).GetProperty("galat").GetString();   // masih menunggu Stockfish
        Assert.Contains("masih menjawab", tugas);
        var ai = (await Data(alamat + "&lanjut", Post(("hasil", """[[{"uci":"g8f6","cp":20,"mat":null,"pv":"g8f6 g2g4"}],[{"uci":"g2g4","cp":-50,"mat":null,"pv":"g2g4 d7d6"}]]""")))).GetProperty("ai").GetString();

        Assert.Contains("terhalang kuda f6", ai);   // jawaban dengan "5. Qxf7+" (menteri f3 terhalang) ditahan
        var ketiga = PesanDeepSeek(2);
        Assert.Contains("5. Qxf7+", ketiga[ketiga.GetArrayLength() - 1].GetProperty("content").GetString());
    }

    [Fact]
    public void MakanKePetakKosongDicatat()
    {
        var cek = PelatihCatur.BacaCek("""{"utama":"Nxe5","variasi":["Nxe5 Nxe5 Qxe5 f4 Qc5 b4 Qc7 Qxd4 Bf6 Qd3 Bxb2"]}""", Papan.DariFen(FenPartaiPengguna)!);
        var v = Assert.Single(cek.Variasi);
        Assert.Null(v.Galat);
        Assert.Equal((10, "Bxb2"), Assert.Single(v.BukanMakan!));
    }

    // Partai killtheclock79–langkahcerdas sesudah 14. Ba4 (hitam melangkah).
    const string FenPartaiPengguna = "r4rk1/pbqpbppp/1pn1p3/4P3/B1Pp4/P2Q1N2/1P3PPP/R1B2RK1 b - - 1 14";

    [Fact]
    public async Task LangkahSiswaYangTidakSahTidakKeStockfish()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qxf7+"}""")),
            JaringanPalsu.Tool("Qxf7+ tidak bisa: pion g6 menghalangi."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var balas = await Data(alamat + "&kirim", Post(("pesan", "Qxf7+ mat!"), ("awal", "[]")));

        Assert.False(balas.TryGetProperty("tugas", out _));
        Assert.Contains("pion g6", balas.GetProperty("ai").GetString());
        Assert.StartsWith("Langkah utama: \"Qxf7+\" tidak sah di sini (putih melangkah); langkah menteri putih yang sah: ", PesanDeepSeek(1)[3].GetProperty("content").GetString());
        Assert.EndsWith("; petak f7: pion hitam; menteri h5 ke f7 terhalang pion hitam di g6", PesanDeepSeek(1)[3].GetProperty("content").GetString());
        Assert.Equal(0, balas.GetProperty("dinilai").GetInt32());
    }

    [Fact]
    public async Task LanjutLewatObrolan()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool("Oke, kita lanjut.", ("posisi_berikutnya", "{}")),
            JaringanPalsu.Tool("Masih di posisi ini, ya."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var balas = await Data(alamat + "&kirim", Post(("pesan", "lanjut"), ("awal", "[]")));

        Assert.True(balas.GetProperty("berikutnya").GetBoolean());   // halaman menjalankan tombol Posisi berikutnya
        Assert.Contains("kita lanjut", balas.GetProperty("ai").GetString());
        Assert.False(balas.TryGetProperty("tugas", out _));
        Assert.Contains("posisi_berikutnya", PesanDeepSeek(0)[0].GetProperty("content").GetString());

        // Kalau partainya habis dan siswa masih mengobrol, percakapannya tetap sah.
        balas = await Data(alamat + "&kirim", Post(("pesan", "eh, tunggu")));
        Assert.False(balas.TryGetProperty("berikutnya", out _));
        var pesan = PesanDeepSeek(1);
        Assert.Equal(["system", "user", "assistant", "tool", "assistant", "user"], pesan.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Aplikasi pindah ke posisi berikutnya.", pesan[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SoalKaranganAiDibuang()
    {
        await SiapkanLatihan(deepseek: [JaringanPalsu.Tool("Soal 2. Langkah 12, putih melangkah. FEN: rn1q1rk1/pb3ppp/8/8/8/8/8/R3K2R w KQ - 0 12 POSISI>>>")]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var ai = (await Data(alamat + "&kirim", Post(("pesan", "lanjut"), ("awal", "[]")))).GetProperty("ai").GetString();

        Assert.Contains("Tekan <strong>Posisi berikutnya</strong>", ai);
        Assert.DoesNotContain("FEN", ai);
    }

    [Theory]
    [InlineData("Soal 2. Langkah 12. FEN: x POSISI>>>", "")]
    [InlineData("Oke.\n<<<POSISI\nSoal 2\nPOSISI>>>\nApa langkahmu?", "Oke.\n\nApa langkahmu?")]
    [InlineData("Mulai <<<POSISI tanpa akhir", "Mulai")]
    [InlineData("Biasa saja.", "Biasa saja.")]
    public void DataPosisiKaranganDibuang(string teks, string harapan) => Assert.Equal(harapan, PelatihCatur.BuangDataPalsu(teks));

    [Fact]
    public async Task AiMelihatPosisiDiTengahVariasi()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("lihat_posisi", """{"langkah":"4. Qxe5+ Nxe5"}""")),
            JaringanPalsu.Tool("Sesudah 4... Nxe5 menteri putih sudah hilang."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var balas = await Data(alamat + "&kirim", Post(("pesan", "apa yang terjadi sesudah Qxe5+?"), ("awal", "[]")));

        Assert.False(balas.TryGetProperty("tugas", out _));   // tanpa Stockfish, langsung dijawab
        var posisi = PesanDeepSeek(1)[3].GetProperty("content").GetString()!;
        Assert.StartsWith("Posisi sesudah 4. Qxe5+ 4... Nxe5.\nPutih melangkah.\n", posisi);
        Assert.Contains("Putih: Ke1, Ra1, Rh1, Bc1, Bc4, Nb1, Ng1; pion a2, b2, c2, d2, f2, g2, h2, e4\n", posisi);
        Assert.Contains("Hitam: Ke8, Qd8, Ra8, Rh8, Bc8, Bf8, Ne5, Ng8; pion g6, a7, b7, c7, d7, f7, h7\n", posisi);
        Assert.Contains("Langkah sah: ", posisi);
        Assert.Contains("menteri putih sudah hilang", balas.GetProperty("ai").GetString());
    }

    [Fact]
    public async Task LangkahYangBelumDiujiDikembalikanKeAi()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool("Main saja **Bh6**, gajahnya aktif."),
            JaringanPalsu.Tool("Setelah kuperiksa, gajah c1 belum bisa keluar: d2 dan b2 menghalanginya."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var ai = (await Data(alamat + "&kirim", Post(("pesan", "gajah mana yang aktif?"), ("awal", "[]")))).GetProperty("ai").GetString();

        Assert.Contains("Setelah kuperiksa", ai);
        Assert.DoesNotContain("Bh6", ai);
        var kedua = PesanDeepSeek(1);
        var koreksi = kedua[kedua.GetArrayLength() - 1].GetProperty("content").GetString()!;
        Assert.StartsWith("[Pesan otomatis dari aplikasi, bukan dari siswa]", koreksi);
        Assert.Contains(": Bh6.", koreksi);
        Assert.DoesNotContain("Bh6", await Html(alamat));   // draf yang dikembalikan tidak tampil
    }

    [Fact]
    public async Task CabangSiswaYangDilewatiDikembalikanKeAi()
    {
        await SiapkanLatihan(deepseek:
        [
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qf3"}""")),
            JaringanPalsu.Tool("Bagus, Qf3 tepat."),
            JaringanPalsu.Tool("Sudah kuperiksa semua cabangmu."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var ai = (await Data(alamat + "&kirim", Post(("pesan", "Qf3, kalau Nd4 aku Qd1"),
            ("awal", """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""")))).GetProperty("ai").GetString();

        Assert.Contains("Sudah kuperiksa semua cabangmu.", ai);
        var ketiga = PesanDeepSeek(2);
        var cakupan = ketiga[ketiga.GetArrayLength() - 1].GetProperty("content").GetString()!;
        Assert.StartsWith("[Pesan otomatis dari aplikasi, bukan dari siswa] Langkah yang ditulis siswa ini belum sampai diperiksa Stockfish: Nd4, Qd1.", cakupan);
    }

    // Menyebut langkah di argumen alat belum berarti memeriksanya: variasi
    // yang patah di langkah tidak sah tidak pernah sampai ke langkah di
    // belakangnya (terlihat 7 Okt 2026: "Nxe5 Nxe5 Qxe5 d6 Bxd6 Bd6" patah
    // di d6, lalu AI menyangkal ancaman mat Bd6 tanpa data). Laporan "tidak
    // sah" hanya menutup notasi itu sendiri: Bxd6 yang tidak sah bukan Bd6.
    [Fact]
    public async Task LangkahDiBelakangLangkahTidakSahBelumDiperiksa()
    {
        await SiapkanLatihan(deepseek:
        [
            // Bd6 sebagai langkah putih juga tidak sah, tetapi sah untuk hitam:
            // itu belum memeriksa Bd6 hitam yang dimaksud siswa.
            JaringanPalsu.Tool(null, ("cek_variasi", """{"utama":"Qf3","variasi":["Qf3 Nf6 Bxd6 Bd6","Qf3 Nf6 Bd6"]}""")),
            JaringanPalsu.Tool("Qf3 tepat."),
            JaringanPalsu.Tool("Sudah semua."),
        ]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var tugas = (await Data(alamat + "&kirim", Post(("pesan", "Qf3, kalau Nf6 aku Bxd6, lalu Bd6. Seperti 3. Bc4 tadi."),
            ("awal", """[{"uci":"h5f3","cp":-20,"mat":null,"pv":"h5f3 g8f6"}]""")))).GetProperty("tugas");
        var hasil = "[" + string.Join(',', Enumerable.Repeat("[]", tugas.GetArrayLength())) + "]";
        var ai = (await Data(alamat + "&lanjut", Post(("hasil", hasil)))).GetProperty("ai").GetString();

        Assert.Contains("Sudah semua.", ai);
        var laporan = PesanDeepSeek(1)[3].GetProperty("content").GetString()!;
        Assert.Contains("\"Bd6\" tidak sah di sini (putih melangkah)", laporan);
        Assert.Contains("sebagai langkah hitam, \"Bd6\" sah di posisi ini, jadi mungkin ada langkah putih yang terlewat sebelum langkah itu", laporan);
        var ketiga = PesanDeepSeek(2);
        var cakupan = ketiga[ketiga.GetArrayLength() - 1].GetProperty("content").GetString()!;
        // Bc4 langkah partai sebelum soal, bukan bagian jawaban.
        Assert.StartsWith("[Pesan otomatis dari aplikasi, bukan dari siswa] Langkah yang ditulis siswa ini belum sampai diperiksa Stockfish: Bd6.", cakupan);
    }

    // Jawaban yang berhenti di tengah kata, atau kosong karena batas token
    // habis untuk berpikir, diminta ulang sekali dan tidak ditampilkan.
    [Theory]
    [InlineData("Coba sebutkan dulu langkahmu, lalu terba", "stop", "terputus di tengah kalimat")]
    [InlineData("", "length", "batasnya habis untuk berpikir")]
    public async Task JawabanTerpotongDimintaUlang(string isi, string alasan, string pesan)
    {
        await SiapkanLatihan(deepseek: [JaringanPalsu.Tool(isi, [], alasan), JaringanPalsu.Tool("Coba sebutkan dulu langkahmu.")]);
        var html = await Html(Latihan);
        var alamat = $"{Latihan}&sisi=putih&sesi={Sesi().Match(html).Groups[1].Value}";
        await Data(alamat + "&soal", "ply=6");

        var ai = (await Data(alamat + "&kirim", Post(("pesan", "halo"), ("awal", "[]")))).GetProperty("ai").GetString()!;

        Assert.Contains("Coba sebutkan dulu langkahmu.", ai);
        Assert.DoesNotContain("terba", ai);
        var kedua = PesanDeepSeek(1);
        Assert.Contains(pesan, kedua[kedua.GetArrayLength() - 1].GetProperty("content").GetString());
    }

    // Ancaman mat dihitung, bukan ditebak: DeepSeek sungguhan pernah
    // menyangkal ancaman Qxh2# sesudah 16. Bxd7 Bd6 (7 Okt 2026).
    [Fact]
    public void AncamanMatDihitung()
    {
        var t = new Teks(Bahasa.Indonesia);
        var p = Papan.DariFen(FenPartaiPengguna)!;
        foreach (var l in "Nxe5 Nxe5 Qxe5 Bxd7".Split(' '))
            p = p.Jalankan(p.DariNotasi(l)!.Value);
        Assert.Empty(PelatihCatur.AncamanMat(p));
        Assert.Contains("Tidak ada ancaman skakmat satu langkah dari putih.", PelatihCatur.Gambaran(p, t));

        Assert.Contains("Materi (pion 1, kuda dan gajah 3, benteng 5, menteri 9): putih 31, hitam 32; materi hitam unggul 1.", PelatihCatur.Gambaran(p, t));

        p = p.Jalankan(p.DariNotasi("Bd6")!.Value);
        Assert.Equal(["Qxh2#"], PelatihCatur.AncamanMat(p));
        Assert.Contains("Hitam mengancam skakmat dengan Qxh2# (kalau putih tidak mencegahnya).", PelatihCatur.Gambaran(p, t));

        p = p.Jalankan(p.DariNotasi("b3")!.Value);
        Assert.Equal(["Qxh2#"], PelatihCatur.MatLangsung(p));
        Assert.Contains("Hitam bisa langsung skakmat dengan Qxh2#.", PelatihCatur.Gambaran(p, t));

        // Pihak yang sedang diskak tidak bisa "melewatkan" giliran.
        Assert.Empty(PelatihCatur.AncamanMat(Papan.DariFen("4k3/8/8/8/8/8/8/R3K3 w - - 0 1")!.Jalankan(new Langkah(0, 56))));   // Ra8+
    }

    [Fact]
    public void AlasanLangkahTidakSah()
    {
        var alasan = PelatihCatur.TidakSah(Papan.DariFen(FenSoal)!, "Bxd6", new Teks(Bahasa.Indonesia));
        const string awal = "\"Bxd6\" tidak sah di sini (putih melangkah); langkah gajah putih yang sah: ";
        Assert.StartsWith(awal, alasan);
        Assert.EndsWith("; petak d6: kosong", alasan);
        var sah = alasan[awal.Length..alasan.IndexOf(';', awal.Length)].Split(", ").Order();
        Assert.Equal(["Ba6", "Bb3", "Bb5", "Bd3", "Bd5", "Be2", "Be6", "Bf1", "Bxf7+"], sah);   // gajah c1 terhalang
    }

    // Daftar langkah sah tidak membuat langkah "dikenal": sah belum tentu baik.
    [Theory]
    [InlineData("Hitam melangkah.\nLangkah sah: Qd6, Bd6, Rfd8\nTidak ada ancaman.", "Hitam melangkah.\nLangkah sah: \nTidak ada ancaman.")]
    [InlineData("(berhenti di sini: \"Bd6\" tidak sah di sini (putih melangkah); langkah gajah putih yang sah: Bd2, Bxd7; petak d6: kosong)",
        "(berhenti di sini: \"Bd6\" tidak sah di sini (putih melangkah); langkah gajah putih yang sah: ; petak d6: kosong)")]
    [InlineData("\"Bd6\" isn't legal here (white to move); legal bishop moves for white: Bd2, Bxd7)", "\"Bd6\" isn't legal here (white to move); legal bishop moves for white: )")]
    public void DaftarLangkahSahDibuang(string teks, string harapan) => Assert.Equal(harapan, PelatihCatur.TanpaDaftarSah(teks));

    [Theory]
    [InlineData("Setelah 16. Bd2, lanjutan terba", true)]
    [InlineData("Lanjut ke posisi berikutnya,", true)]
    [InlineData("Mau lanjut ke posisi berikutnya?", false)]
    [InlineData("⚙️ 16. Bd2 Be4 (−4,6)", false)]
    [InlineData("Pelajaran: jaga raja.\n", false)]
    [InlineData("", false)]
    public void JawabanTerpotong(string teks, bool terpotong) => Assert.Equal(terpotong, PelatihCatur.Terpotong(teks));

    [Theory]
    [InlineData("Main **14...Nxe5+**, lalu exd5 dan O-O. Pion d5 dan petak e4.", "Nxe5 exd5 O-O")]
    [InlineData("Langkah Kxe9 bukan langkah; Rad8 dan R1e2 dan dxe8=Q langkah.", "Rad8 R1e2 dxe8=Q")]
    [InlineData("tanpa langkah bidak: d4 e5", "")]
    public void LangkahDiTeks(string teks, string harapan) => Assert.Equal(harapan, string.Join(' ', PelatihCatur.LangkahDiTeks(teks)));

    [Fact]
    public void JawabanSiswaDibaca()
    {
        var posisi = Papan.DariFen(FenSoal)!;
        var cek = PelatihCatur.BacaCek("""{"variasi":["4. Qf3!? Nf6 5.Ne2", "4. Qf3 Kxe9"]}""", posisi);
        Assert.Equal("h5f3", cek.Utama?.Uci);   // tanpa "utama": langkah pertama variasi pertama
        Assert.Equal(["Qf3", "Nf6", "Ne2"], cek.Variasi[0].Langkah.Select(l => l.San));
        Assert.Equal(("Kxe9", 1), (cek.Variasi[1].Galat, cek.Variasi[1].Langkah.Count));
        Assert.Equal("Qxf7+", PelatihCatur.BacaCek("""{"utama":"Qxf7+"}""", posisi).GalatUtama);
        Assert.Null(PelatihCatur.BacaCek("bukan json", posisi).Utama);
    }

    [Theory]
    [InlineData("Bagus.\n\nPelajaran: kuasai pusat dulu.", "kuasai pusat dulu.")]
    [InlineData("Bagus.\n\n**Pelajaran:** kuasai pusat dulu.", "kuasai pusat dulu.")]
    [InlineData("Good.\nLesson: control the centre.", "control the centre.")]
    [InlineData("Bagus.\n\nPelajaran: periksa skakmat dulu. Mau lanjut ke posisi berikutnya?", "periksa skakmat dulu.")]
    [InlineData("Pelajaran: kenapa kuda di tepi lemah?", "kenapa kuda di tepi lemah?")]
    [InlineData("Belum ada pelajaran di sini.", null)]
    public void PelajaranDariJawaban(string teks, string? harapan) => Assert.Equal(harapan, PelatihCatur.Pelajaran(teks));

    [Fact]
    public void PengisiPelajaranDibuang()
    {
        var t = new Teks(Bahasa.Indonesia);
        Assert.Equal("Sumber: x\n\n## Pelajaran\n\n### Tebak langkah\n", HalamanCatur.TambahPelajaran(t, "Sumber: x\n\n## Pelajaran\n\n(belum diisi)\n", "### Tebak langkah"));
        Assert.Equal("Sumber: x\n\n## Lessons\n\nsendiri\n\n### Tebak langkah\n", HalamanCatur.TambahPelajaran(t, "Sumber: x\n\n## Lessons\n\nsendiri", "### Tebak langkah\n"));
        Assert.Equal("Sumber: x\n\n## Pelajaran\n\n### Tebak langkah\n", HalamanCatur.TambahPelajaran(t, "Sumber: x\r\n", "### Tebak langkah"));
    }

    [Fact]
    public void VariasiMesinJadiNotasi()
    {
        Assert.Equal("1. e4 e5 2. Nf3", HalamanCatur.Variasi(Papan.Awal(), "e2e4 e7e5 g1f3 zz b8c6", 8));
        var hitam = Papan.Awal().Jalankan(Papan.Awal().DariNotasi("e4")!.Value);
        Assert.Equal("1... e5 2. Nf3 Nc6", HalamanCatur.Variasi(hitam, "e7e5 g1f3 b8c6", 8));
        Assert.Equal("1... e5", HalamanCatur.Variasi(hitam, "e7e5 g1f3 b8c6", 1));
        Assert.Equal("", HalamanCatur.Variasi(hitam, null, 8));
    }

    [Theory]
    [InlineData("1. e4 d5 2. exd5 Qxd5 *", "putih", null)]
    [InlineData("1. e4 d5 2. exd5 Qxd5 *", "hitam", HalamanCatur.LewatAmbil)]          // hanya menteri yang bisa
    [InlineData("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Bxc6 dxc6 *", "hitam", null)]         // dxc6 atau bxc6: pilihan
    [InlineData("1. e4 d5 2. exd5 Qxd5?! *", "hitam", null)]                            // ditandai: tetap ditanya
    [InlineData("1. e4 d5 2. exd5 Nf6 *", "hitam", null)]                               // tidak mengambil kembali
    [InlineData("[FEN \"7k/8/8/8/8/8/5PP1/r5K1 w - - 0 1\"]\n\n1. Kh2 *", "putih", HalamanCatur.LewatSatu)]
    [InlineData("[FEN \"7k/8/8/8/8/8/6P1/r5K1 w - - 0 1\"]\n\n1. Kf2 *", "putih", HalamanCatur.LewatSkak)]
    [InlineData("[FEN \"7k/8/8/8/8/8/8/r5K1 w - - 0 1\"]\n\n1. Kg2 *", "putih", null)]    // skak, tiga pilihan
    public void LangkahTanpaPilihanDilewati(string pgn, string sisi, string? harapan)
    {
        var u = Pgn.Pisah(pgn).Single().Urai();
        Assert.Null(u.Galat);
        Assert.Equal(harapan, HalamanCatur.SusunSoal(u, sisi == "putih", 1)[^1].Lewat);
    }
}
