using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// Nilai satu langkah menurut Stockfish, dari sudut pihak yang melangkah di
/// posisi itu. Mat dijadikan ±(100.000 − jarak) di <see cref="Cp"/> supaya
/// bisa diurutkan.
/// </summary>
public sealed record NilaiMesin(string Uci, int Cp, int? Mat, IReadOnlyList<string> Pv);

/// <summary>
/// Pekerjaan Stockfish untuk latihan.js: <see cref="Jumlah"/> langkah terbaik
/// posisi itu (MultiPV; 0 = tidak perlu), ditambah nilai langkah
/// <see cref="Cari"/> (searchmoves) yang belum ada di antaranya.
/// </summary>
public sealed record TugasMesin(string Fen, int Jumlah, IReadOnlyList<string> Cari, int Kedalaman, int Waktu);

/// <summary>Penilaian jawaban siswa di satu soal, untuk catatan. Langkah dalam UCI.</summary>
public sealed record HasilSoal(int Ply, string Tebakan, string Banding, string Jenis, int Hilang, string Terbaik)
{
    /// <summary>Baris "Pelajaran: …" terakhir dari penjelasan AI untuk soal ini.</summary>
    public string? Pelajaran { get; set; }
}

/// <summary>
/// Balasan untuk latihan.js: gelembung siswa (kalau baru), pekerjaan mesin
/// yang harus dikerjakan dulu, atau jawaban AI; <see cref="Galat"/> kalau
/// gagal. <see cref="Berikutnya"/>: pelatih meminta pindah ke posisi
/// berikutnya (sama dengan tombol Posisi berikutnya).
/// </summary>
public sealed record BalasanPelatih(string? Siswa, IReadOnlyList<TugasMesin>? Tugas, string? Ai, string? Galat, int Dinilai,
    bool Berikutnya = false);

/// <summary>
/// Satu sesi tebak langkah: satu partai, satu sisi. Hanya di memori, supaya
/// tab yang ditidurkan lalu dibangunkan bisa melanjutkannya; yang perlu
/// disimpan masuk ke catatan partai itu.
/// </summary>
public sealed class SesiLatihan(Partai partai, bool putih, string model)
{
    internal readonly object Gembok = new();

    public string Id { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    public Partai Partai => partai;
    public UraianPartai Uraian { get; } = partai.Urai();
    public bool Putih => putih;
    public string Model => model;

    /// <summary>Gelembung obrolan (HTML) dari awal, untuk ditampilkan lagi.</summary>
    public List<string> Transkrip { get; } = [];

    /// <summary>Ply soal sekarang (posisi sebelum langkah partai ke-ply), -1 kalau belum ada.</summary>
    public int Soal { get; internal set; } = -1;

    public List<int> Ditanya { get; } = [];
    public List<HasilSoal> Hasil { get; } = [];
    public double Biaya { get; internal set; }

    /// <summary>Percakapan soal sekarang, tanpa petunjuk sistem. Soal baru = percakapan baru.</summary>
    internal List<PesanAi> Pesan { get; } = [];

    /// <summary>Analisis awal posisi soal: kandidat terbaik dan langkah partai.</summary>
    internal List<NilaiMesin> Awal { get; set; } = [];

    /// <summary>Permintaan tool yang menunggu hasil Stockfish dari halaman.</summary>
    internal Tertunda? Tunda { get; set; }

    /// <summary>
    /// Langkah bidak dan makan yang sudah muncul di data posisi dan hasil tool
    /// soal ini (lihat PelatihCatur.BelumDiuji).
    /// </summary>
    internal HashSet<string> Dikenal { get; } = [];

    /// <summary>
    /// Langkah yang sudah muncul di variasi yang dinilai di soal ini (partai,
    /// lanjutan Stockfish, variasi yang diuji), menurut nomor,
    /// giliran, bidak, dan petak tujuan: "17|False|~Qh2". Langkah bernomor di
    /// jawaban AI harus ada di sini (lihat PelatihCatur.LangkahBernomorBelumDiuji).
    /// </summary>
    internal HashSet<string> LangkahDinilai { get; } = [];

    /// <summary>Pesan siswa yang sedang dijawab, apa adanya (lihat PelatihCatur.BelumDiperiksa).</summary>
    internal string PesanSiswa { get; set; } = "";

    /// <summary>
    /// Langkah yang benar-benar sampai diperiksa cek_variasi di soal ini:
    /// dimainkan di variasi yang diuji, menurut bidak dan petak tujuannya
    /// ("~Bd6"), atau dilaporkan tidak sah persis seperti tulisannya
    /// ("=Bxd6"). Lihat PelatihCatur.BelumDiperiksa.
    /// </summary>
    internal HashSet<string> Diperiksa { get; } = [];

    internal bool Sibuk { get; set; }

    internal Papan PosisiSoal => Papan.DariFen(Soal <= 0 ? Uraian.FenAwal : Uraian.Langkah[Soal - 1].Fen)!;

    /// <summary>Halaman perlu menganalisis posisi soal dulu (pesan pertama soal ini belum dikirim).</summary>
    public bool PerluAwal => Soal >= 0 && Pesan.Count == 0;

    /// <summary>Pekerjaan mesin yang belum selesai (mis. tab ditidurkan di tengahnya).</summary>
    public IReadOnlyList<TugasMesin>? TugasTertunda => Tunda?.Tugas;
}

/// <summary>Variasi siswa yang sudah diperiksa sah-tidaknya, langkah demi langkah.</summary>
/// <param name="PosisiGalat">Posisi tempat <paramref name="Galat"/> tidak sah, untuk menjelaskan kenapa.</param>
/// <param name="BukanMakan">Langkah yang ditulis sebagai makan ("Bxb2") padahal petak tujuannya kosong: (indeks langkah, notasi).</param>
sealed record VariasiSiswa(List<(Langkah Langkah, string San, Papan Sebelum)> Langkah, string? Galat, Papan? PosisiGalat = null,
    List<(int Ke, string Notasi)>? BukanMakan = null);

/// <summary>Isi satu panggilan cek_variasi, sesudah langkah-langkahnya diperiksa.</summary>
sealed record CekVariasi(Langkah? Utama, string? GalatUtama, List<VariasiSiswa> Variasi)
{
    public bool AdaYangDinilai => Utama is not null;
}

/// <summary>Satu putaran AI yang berhenti menunggu Stockfish di halaman.</summary>
sealed class Tertunda(List<PanggilTool> panggil, int panjangSebelum)
{
    public List<PanggilTool> Panggil => panggil;

    /// <summary>Panjang <see cref="SesiLatihan.Pesan"/> sebelum permintaan tool ini, untuk dikembalikan kalau gagal.</summary>
    public int PanjangSebelum => panjangSebelum;

    public Dictionary<string, string> Hasil { get; } = [];
    public List<(PanggilTool Panggil, CekVariasi Cek)> Cek { get; } = [];
    public List<TugasMesin> Tugas { get; set; } = [];
    public int Putaran, TokenCache, TokenBaru, TokenKeluar;

    /// <summary>Berapa kali jawaban AI untuk pesan ini dikembalikan karena menyebut langkah yang belum diuji.</summary>
    public int Koreksi;

    /// <summary>Berapa kali jawaban AI dikembalikan karena ada langkah siswa yang tidak ikut diperiksa.</summary>
    public int Cakupan;

    /// <summary>Berapa kali jawaban AI diminta ulang karena berhenti di tengah kalimat.</summary>
    public int Terpotong;
}

/// <summary>
/// Tebak langkah sebagai obrolan, seperti di KEVIN: siswa melihat posisi
/// (papan hanya gambar), menjawab dengan kata-katanya sendiri (langkah,
/// alasan, variasi), AI membaca jawabannya, meminta Stockfish mengujinya
/// lewat tool <c>cek_variasi</c>, lalu menjelaskan. Stockfish berjalan di
/// halaman (WebAssembly, latihan.js), jadi tool itu tidak dijalankan di sini:
/// putarannya berhenti, halaman mengerjakan <see cref="TugasMesin"/>, lalu
/// melanjutkannya dengan hasilnya (<see cref="Lanjut"/>).
/// </summary>
/// <remarks>
/// Penilaian dihitung di sini, bukan oleh AI (cara KEVIN, Mcp/catur.py):
/// nilai mesin diubah ke peluang menang (rumus Lichess), ambang 30/20/10 untuk
/// blunder, kesalahan, ketidaktepatan, lalu dibandingkan dengan langkah
/// partai: sama, setara (selisih &lt; 5), lebih baik, lebih buruk. AI hanya
/// menjelaskan angka yang sudah jadi.
/// </remarks>
public sealed class PelatihCatur(PengaturanAi pengaturan, KlienAi klien, TimeProvider waktu)
{
    // Jawaban siswa boleh panjang dan bercabang dalam (permintaan pemakai 7
    // Okt 2026: "mending jawaban mahal tapi benar dari pada jawaban murah
    // tapi menyesatkan"): sampai 24 ply per variasi, 10 variasi per
    // panggilan, beberapa panggilan per jawaban.
    public const int MaksPutaranTool = 8;
    public const int MaksTokenJawaban = 3_000;

    /// <summary>
    /// Dengan mode berpikir, token berpikirnya termasuk batas ini. deepseek-flash
    /// pernah berpikir 12.428 token untuk pohon 10 variasi (7 Okt 2026).
    /// </summary>
    public const int MaksTokenBerpikir = 32_000;
    public const int MaksHurufPesan = 6_000;
    public const int MaksVariasi = 10;
    public const int MaksLangkahVariasi = 24;

    /// <summary>Posisi yang dianalisis Stockfish per putaran (±0,5–1 detik masing-masing).</summary>
    public const int MaksTugas = 90;

    /// <summary>Pesan siswa per soal, supaya percakapan satu posisi tidak tumbuh tanpa batas.</summary>
    public const int MaksPesanPerSoal = 12;

    readonly List<SesiLatihan> semua = [];

    public PengaturanAi Pengaturan => pengaturan;

    /// <summary>
    /// Log percakapan dengan AI (pesan siswa, permintaan alat dan argumennya,
    /// hasil alat, jawaban, token); diisi Mesin kalau KEVIN_BROWSER_CATAT=1.
    /// Untuk memeriksa apakah AI benar-benar memakai alat sebelum menilai.
    /// </summary>
    public Action<string>? Pencatat { get; init; }

    /// <summary>
    /// Mode berpikir DeepSeek. Terukur 7 Okt 2026 (DeepSeek sungguhan,
    /// Stockfish asli, partai killtheclock79–langkahcerdas soal 14...):
    /// tanpa berpikir, AI salah membaca "jika Bxd6, maka saya Bd6", mengaku
    /// keliru saat dibantah padahal datanya benar, menyetujui "materi imbang"
    /// (hitam unggul 2), dan tidak pernah menguji 16. Bxd7 Bd6 walau dua kali
    /// dikembalikan penjaga; pohon 10 variasi dijawab dangkal (18... Bxe4 yang
    /// memenangkan menteri dan dua blunder tidak disebut). Dengan berpikir
    /// semuanya benar. Harganya (deepseek-v4-pro): bantahan dan cabang Bd6
    /// 6–39 → 20–69 detik per jawaban, $0,032 → $0,084 sesi itu; pohon 10
    /// variasi 28 → 76 detik, $0,015 → $0,036. deepseek-flash berpikir:
    /// 11–34 detik, $0,018; pohon 131 detik, $0,033; sedikit kurang teliti
    /// menafsirkan maksud siswa. Pemakai: "mending jawaban mahal tapi benar".
    /// </summary>
    public bool Berpikir { get; init; } = true;

    void Catat(string pesan) => Pencatat?.Invoke("[pelatih] " + pesan);

    public SesiLatihan? Ambil(string? id)
    {
        lock (semua)
            return semua.FirstOrDefault(s => s.Id == id);
    }

    public SesiLatihan Baru(Partai partai, bool putih)
    {
        var sesi = new SesiLatihan(partai, putih, pengaturan.ModelTanya);
        lock (semua)
        {
            semua.Add(sesi);
            if (semua.Count > 10)
                semua.RemoveAt(0);
        }
        return sesi;
    }

    /// <summary>
    /// Soal baru di posisi sebelum langkah partai ke-<paramref name="ply"/>;
    /// gelembung pertanyaannya masuk transkrip. Null kalau ply itu bukan
    /// langkah sisi siswa sesudah soal sebelumnya.
    /// </summary>
    public string? Soal(SesiLatihan s, int ply, Teks t)
    {
        lock (s.Gembok)
        {
            var u = s.Uraian;
            if (s.Sibuk || s.Tunda is not null || ply < 0 || ply >= u.Langkah.Count || ply <= s.Soal)
                return null;
            var posisi = Papan.DariFen(ply == 0 ? u.FenAwal : u.Langkah[ply - 1].Fen)!;
            if (posisi.GiliranPutih != s.Putih)
                return null;
            s.Soal = ply;
            s.Ditanya.Add(ply);
            s.Pesan.Clear();
            s.Awal = [];
            s.Dikenal.Clear();
            s.LangkahDinilai.Clear();
            s.Diperiksa.Clear();
            // Partai sampai posisi soal juga dikenal (AI boleh menyebut langkah
            // partai sebelumnya). lock bisa dimasuki ulang oleh thread yang sama.
            DaftarkanLangkah(s, Papan.DariFen(u.FenAwal)!, u.Langkah.Take(ply).Select(l => l.Langkah.Uci));
            var html = $"""
                <div class="baris-ai">{Avatar}<div class="gelembung ai soal"><p><strong>{t[$"Soal {s.Ditanya.Count}", $"Question {s.Ditanya.Count}"]}</strong> · {Giliran(t, posisi)}</p>{(ply == 0 ? ""
                    : $"<p class=\"lalu\">{t["Langkah partai sebelumnya:", "Game moves before this:"]} {HtmlEncode(Sebelumnya(u, ply, 6))}</p>")}<p>{t[
                    "Apa langkahmu di sini, dan kenapa? Kalau bisa, tulis juga lanjutan atau variasinya.",
                    "What's your move here, and why? If you can, also write the follow-up or the variations."]}</p></div></div>
                """;
            s.Transkrip.Add(html);
            return html;
        }
    }

    /// <summary>Pesan siswa untuk soal sekarang. <paramref name="awalJson"/>: analisis awal posisi soal dari halaman.</summary>
    public async Task<BalasanPelatih> Kirim(SesiLatihan s, string? pesan, string? awalJson, Teks t, CancellationToken batal)
    {
        pesan = (pesan ?? "").Replace("\r\n", "\n").Trim();
        int panjang;
        string siswa;
        lock (s.Gembok)
        {
            string? galat = s.Sibuk || s.Tunda is not null ? t["Pelatih masih menjawab pesan sebelumnya.", "The coach is still answering the previous message."]
                : s.Soal < 0 ? t["Belum ada posisi untuk dijawab.", "There's no position to answer yet."]
                : pesan.Length == 0 ? t["Tulis dulu jawabanmu.", "Write your answer first."]
                : pesan.Length > MaksHurufPesan ? t[$"Pesannya terlalu panjang: paling banyak {MaksHurufPesan} huruf.", $"The message is too long: {MaksHurufPesan} characters at most."]
                : s.Pesan.Count(p => p.Peran == "user") >= MaksPesanPerSoal ? t["Sudah banyak sekali pesan di posisi ini. Lanjut ke posisi berikutnya, ya.", "That's a lot of messages for this position. Let's move on to the next one."]
                : pengaturan.Kunci is null ? t["Belum ada kunci API DeepSeek.", "There's no DeepSeek API key yet."]
                : null;
            if (galat is not null)
                return new(null, null, null, galat, s.Hasil.Count);
            s.Sibuk = true;
            if (s.Pesan.Count == 0)
            {
                s.Awal = BacaNilai(awalJson, s.PosisiSoal);
                var konteks = Konteks(s, t);
                Kenali(s, konteks);
                s.Pesan.Add(new("user", konteks + "\n\n" + t["Jawaban siswa:", "The student's answer:"] + "\n" + pesan));
            }
            else
                s.Pesan.Add(new("user", pesan));
            panjang = s.Pesan.Count;
            s.PesanSiswa = pesan;
            Catat($"soal ply {s.Soal}, siswa: {pesan}");
            siswa = $"""<p class="gelembung siswa">{HtmlEncode(pesan)}</p>""";
            s.Transkrip.Add(siswa);
        }
        var balasan = await Jalankan(s, new Tertunda([], panjang), t, batal);
        return balasan with { Siswa = siswa };
    }

    /// <summary>Hasil Stockfish untuk pekerjaan yang diminta balasan sebelumnya, lalu AI melanjutkan.</summary>
    public async Task<BalasanPelatih> Lanjut(SesiLatihan s, string? hasilJson, Teks t, CancellationToken batal)
    {
        Tertunda tunda;
        lock (s.Gembok)
        {
            if (s.Sibuk || s.Tunda is null)
                return new(null, null, null, t["Tidak ada yang sedang ditunggu.", "Nothing is waiting."], s.Hasil.Count);
            s.Sibuk = true;
            tunda = s.Tunda;
            s.Tunda = null;
        }
        var nilai = BacaHasilTugas(hasilJson, tunda.Tugas);
        foreach (var (panggil, cek) in tunda.Cek)
            tunda.Hasil[panggil.Id] = Laporan(s, cek, nilai, t);
        lock (s.Gembok)
            foreach (var p in tunda.Panggil)
                TambahHasilTool(s, p.Id, tunda.Hasil.GetValueOrDefault(p.Id, "Galat: tidak ada hasil."));
        return await Jalankan(s, tunda, t, batal);
    }

    // Putaran AI sampai menjawab, atau sampai butuh Stockfish (Tunda diisi).
    // Kalau gagal, percakapan dikembalikan ke sebelum putaran ini, tetapi
    // pesan siswa tetap ada.
    async Task<BalasanPelatih> Jalankan(SesiLatihan s, Tertunda hitung, Teks t, CancellationToken batal)
    {
        var panjangAwal = hitung.PanjangSebelum;
        try
        {
            var kunci = pengaturan.Kunci ?? throw new GalatAi(401, "");
            while (true)
            {
                var bolehTool = hitung.Putaran < MaksPutaranTool;
                List<PesanAi> pesan;
                lock (s.Gembok)
                    pesan = [new("system", PromptLatihan.Sistem(t)), .. s.Pesan];
                var jawaban = await klien.ChatTool(pengaturan.Alamat, kunci, s.Model, pesan, PromptLatihan.Alat, bolehTool,
                    Berpikir ? MaksTokenBerpikir : MaksTokenJawaban, batal, Berpikir);
                hitung.Putaran++;
                hitung.TokenCache += jawaban.TokenCache;
                hitung.TokenBaru += jawaban.TokenBaru;
                hitung.TokenKeluar += jawaban.TokenKeluar;
                Catat($"putaran {hitung.Putaran} ({s.Model}): {jawaban.TokenCache + jawaban.TokenBaru} token masuk ({jawaban.TokenCache} cache), "
                    + $"{jawaban.TokenKeluar} keluar, berhenti: {jawaban.AlasanBerhenti}"
                    + string.Concat(jawaban.Panggil.Select(p => $"\n  alat {p.Nama}: {p.Argumen}"))
                    + (jawaban.Isi is { Length: > 0 } isiAi ? $"\n  teks: {isiAi}" : ""));

                if (jawaban.Panggil.Count == 0 || !bolehTool)
                {
                    // Jawaban yang berhenti di tengah kata ("lanjutan terba",
                    // alasan berhenti "stop"; DeepSeek sungguhan, 7 Okt 2026),
                    // atau yang kosong karena batas token habis untuk berpikir,
                    // tidak ditampilkan; AI diminta menulisnya ulang, sekali.
                    var habis = jawaban.AlasanBerhenti == "length" && string.IsNullOrWhiteSpace(jawaban.Isi);
                    if (bolehTool && hitung.Terpotong == 0 && (habis || (jawaban.AlasanBerhenti == "stop" && Terpotong(jawaban.Isi))))
                    {
                        Catat(habis ? "batas token habis sebelum menjawab, diminta ulang" : "jawaban terpotong di tengah kalimat, diminta ulang");
                        hitung.Terpotong++;
                        lock (s.Gembok)
                            s.Pesan.Add(new("user", habis ? PromptLatihan.Habis(t) : PromptLatihan.Terpotong(t)));
                        continue;
                    }
                    // Penjaga cakupan: langkah yang ditulis siswa tetapi tidak
                    // sampai diperiksa alat (terlihat 7 Okt 2026: cabang "jika
                    // Bxd6, maka Bd6" dilewati begitu saja; lalu variasi yang
                    // memuatnya patah sebelum sampai ke sana, dan AI menyangkal
                    // ancaman mat Bd6 tanpa data). Paling banyak dua kali per pesan.
                    if (bolehTool && hitung.Cakupan < 2 && BelumDiperiksa(s) is { Count: > 0 } lewat)
                    {
                        Catat($"penjaga cakupan, langkah siswa belum diperiksa: {string.Join(", ", lewat)}");
                        hitung.Cakupan++;
                        lock (s.Gembok)
                            s.Pesan.Add(new("user", PromptLatihan.Cakupan(t, lewat)));
                        continue;
                    }
                    // Penjaga: jawaban yang menyebut langkah yang belum pernah
                    // muncul di data posisi atau hasil tool tidak ditampilkan;
                    // AI diminta mengujinya dulu. Dua kali: tulisan ulang
                    // sesudah yang pertama pernah membawa langkah baru yang
                    // belum diuji ("17. Qxf5 Rxf5", 7 Okt 2026).
                    if (bolehTool && hitung.Koreksi < 2
                        && BelumDiuji(s, jawaban.Isi ?? "").Concat(LangkahBernomorBelumDiuji(s, jawaban.Isi ?? "")).Distinct().ToList() is { Count: > 0 } asing)
                    {
                        Catat($"penjaga mengembalikan jawaban, langkah belum diuji: {string.Join(", ", asing)}");
                        hitung.Koreksi++;
                        // Drafnya tidak masuk percakapan: siswa tidak pernah
                        // melihatnya, dan kalau ikut, AI meminta maaf atas draf itu
                        // ("saya keliru menyebut …", terlihat 7 Okt 2026).
                        lock (s.Gembok)
                            s.Pesan.Add(new("user", PromptLatihan.Koreksi(t, asing)));
                        continue;
                    }
                    return Selesai(s, hitung, jawaban, t);
                }
                if (jawaban.Panggil.Any(p => p.Nama == "posisi_berikutnya"))
                    return Berikutnya(s, hitung, jawaban, t);

                var tunda = new Tertunda([.. jawaban.Panggil], 0)
                {
                    Putaran = hitung.Putaran,
                    TokenCache = hitung.TokenCache,
                    TokenBaru = hitung.TokenBaru,
                    TokenKeluar = hitung.TokenKeluar,
                    Koreksi = hitung.Koreksi,
                    Cakupan = hitung.Cakupan,
                    Terpotong = hitung.Terpotong,
                };
                foreach (var p in jawaban.Panggil)
                {
                    if (p.Nama == "lihat_posisi")
                        tunda.Hasil[p.Id] = LihatPosisi(s, p.Argumen, t);
                    else if (p.Nama != "cek_variasi")
                        tunda.Hasil[p.Id] = $"Galat: tool \"{p.Nama}\" tidak ada.";
                    else
                    {
                        var cek = BacaCek(p.Argumen, s.PosisiSoal);
                        CatatDiperiksa(s, cek);
                        if (cek.AdaYangDinilai)
                            tunda.Cek.Add((p, cek));
                        else
                            tunda.Hasil[p.Id] = Laporan(s, cek, [], t);
                    }
                }
                tunda.Tugas = Tugas(s, tunda.Cek.Select(c => c.Cek));
                lock (s.Gembok)
                {
                    s.Pesan.Add(new("assistant", jawaban.Isi, jawaban.Panggil));
                    if (tunda.Tugas.Count > 0)
                    {
                        s.Tunda = tunda;
                        s.Sibuk = false;
                        return new(null, tunda.Tugas, null, null, s.Hasil.Count);
                    }
                }
                // Semua nilainya sudah ada di analisis awal: langsung dilaporkan.
                foreach (var (p, cek) in tunda.Cek)
                    tunda.Hasil[p.Id] = Laporan(s, cek, [], t);
                lock (s.Gembok)
                    foreach (var p in tunda.Panggil)
                        TambahHasilTool(s, p.Id, tunda.Hasil[p.Id]);
                hitung = tunda;
            }
        }
        catch (Exception e)
        {
            var galat = e switch
            {
                OperationCanceledException => t["Dibatalkan.", "Cancelled."],
                GalatAi g => Penyerap.PesanGalat(t, g),
                _ => t[$"Galat: {e.Message}", $"Error: {e.Message}"],
            };
            Catat($"galat: {e.GetType().Name}: {e.Message}");
            lock (s.Gembok)
            {
                if (s.Pesan.Count > panjangAwal && panjangAwal > 0)
                    s.Pesan.RemoveRange(panjangAwal, s.Pesan.Count - panjangAwal);
                s.Tunda = null;
                s.Sibuk = false;
                s.Transkrip.Add($"""<p class="info galat">{HtmlEncode(galat)}</p>""");
            }
            return new(null, null, null, galat, s.Hasil.Count);
        }
    }

    BalasanPelatih Selesai(SesiLatihan s, Tertunda hitung, JawabanTool jawaban, Teks t)
    {
        var teks = BuangDataPalsu(jawaban.Isi ?? "") is { Length: > 0 } isi ? isi
            : jawaban.Isi?.Contains("POSISI", StringComparison.Ordinal) == true
                ? t["Tekan **Posisi berikutnya** di atas papan untuk lanjut.", "Press **Next position** above the board to go on."]
                : t["(Pelatih tidak memberi jawaban. Coba tulis dengan kalimat lain.)", "(The coach gave no answer. Try writing it another way.)"];
        if (jawaban.AlasanBerhenti == "length")
            teks += t["\n\n*(Jawaban terpotong karena terlalu panjang.)*", "\n\n*(The answer was cut off because it was too long.)*"];
        return Tutup(s, hitung, teks, t, berikutnya: false);
    }

    // Pelatih meminta posisi berikutnya (siswa bilang "lanjut"): permintaan
    // tool itu langsung dijawab di sini, halaman menjalankan tombol Posisi
    // berikutnya. Percakapan tetap utuh (permintaan, hasil, jawaban) kalau
    // ternyata partainya sudah habis dan siswa masih mengobrol di soal ini.
    BalasanPelatih Berikutnya(SesiLatihan s, Tertunda hitung, JawabanTool jawaban, Teks t)
    {
        lock (s.Gembok)
        {
            s.Pesan.Add(new("assistant", jawaban.Isi, jawaban.Panggil));
            foreach (var p in jawaban.Panggil)
                s.Pesan.Add(new("tool", p.Nama == "posisi_berikutnya" ? "Aplikasi pindah ke posisi berikutnya." : "Dilewati: siswa pindah ke posisi berikutnya.", IdTool: p.Id));
        }
        return Tutup(s, hitung, BuangDataPalsu(jawaban.Isi ?? "") is { Length: > 0 } isi ? isi : t["Oke, kita lanjut.", "Okay, let's move on."], t, berikutnya: true);
    }

    BalasanPelatih Tutup(SesiLatihan s, Tertunda hitung, string teks, Teks t, bool berikutnya)
    {
        var biaya = HargaAi.Biaya(s.Model, hitung.TokenCache, hitung.TokenBaru, hitung.TokenKeluar, waktu.GetUtcNow());
        // Biayanya hanya di tooltip: obrolan ini untuk berpikir, bukan untuk angka.
        var html = $"""
            <div class="baris-ai">{Avatar}<div class="gelembung ai isi-catatan" title="{t["Biaya jawaban ini:", "Cost of this answer:"]} {HalamanSerap.Dolar(t, biaya)}">{Markah.KeHtml(teks, geserJudul: 2)}</div></div>
            """;
        lock (s.Gembok)
        {
            s.Pesan.Add(new("assistant", teks));
            if (Pelajaran(teks) is { } pelajaran && s.Hasil.FirstOrDefault(h => h.Ply == s.Soal) is { } hasil)
                hasil.Pelajaran = pelajaran;
            s.Biaya += biaya;
            s.Transkrip.Add(html);
            s.Sibuk = false;
            return new(null, null, html, null, s.Hasil.Count, berikutnya);
        }
    }

    /// <summary>
    /// Membuang data posisi yang ditulis AI sendiri. AI pernah meniru blok
    /// &lt;&lt;&lt;POSISI … POSISI&gt;&gt;&gt; dan mengarang soal baru (terjadi 7 Okt
    /// 2026, sesudah siswa menjawab "lanjut"; datanya pun tidak masuk akal).
    /// Posisi hanya boleh datang dari aplikasi.
    /// </summary>
    internal static string BuangDataPalsu(string teks)
    {
        var awal = teks.IndexOf("<<<POSISI", StringComparison.Ordinal);
        var akhir = teks.IndexOf("POSISI>>>", StringComparison.Ordinal);
        if (akhir >= 0)
            teks = (awal >= 0 && awal < akhir ? teks[..awal] : "") + teks[(akhir + "POSISI>>>".Length)..];
        else if (awal >= 0)
            teks = teks[..awal];
        return teks.Trim();
    }

    /// <summary>Jawaban yang berakhir dengan huruf, angka, atau koma: berhenti di tengah kalimat.</summary>
    internal static bool Terpotong(string? teks) =>
        teks?.TrimEnd() is { Length: > 0 } t && (char.IsLetterOrDigit(t[^1]) || t[^1] == ',');

    /// <summary>Baris "Pelajaran: …" (atau "Lesson: …") terakhir di jawaban AI.</summary>
    internal static string? Pelajaran(string teks)
    {
        foreach (var baris in teks.Split('\n').Reverse())
        {
            var b = baris.Trim().Trim('*', '_', ' ');
            foreach (var awalan in new[] { "Pelajaran:", "Lesson:" })
                if (b.StartsWith(awalan, StringComparison.OrdinalIgnoreCase) && b[awalan.Length..].Trim('*', '_', ' ') is { Length: > 0 } isi)
                    return isi;
        }
        return null;
    }

    // ---------- data posisi untuk AI ----------

    // Pesan pertama tiap soal: posisinya, analisis Stockfish, dan langkah
    // partai, di antara penanda supaya AI tahu siswa tidak melihatnya.
    static string Konteks(SesiLatihan s, Teks t)
    {
        var u = s.Uraian;
        var posisi = s.PosisiSoal;
        var sb = new StringBuilder("<<<POSISI\n");
        sb.Append(t[$"Soal {s.Ditanya.Count}. ", $"Question {s.Ditanya.Count}. "]).Append(Giliran(t, posisi))
            .Append(t[$" Siswa bermain {(s.Putih ? "putih" : "hitam")}.", $" The student plays {(s.Putih ? "white" : "black")}."]).Append('\n');
        sb.Append("FEN: ").Append(posisi.Fen()).Append('\n');
        if (s.Soal > 0)
            sb.Append(t["Langkah sebelumnya: ", "Previous moves: "]).Append(Sebelumnya(u, s.Soal, 10, untukAi: true)).Append('\n');
        // Tebakan di soal sebelumnya yang tidak dimainkan: siswa pernah mengira
        // tebakannya 11... d6 ikut dimainkan dan berbicara tentang "pion d6" di
        // soal berikutnya (7 Okt 2026).
        foreach (var h in s.Hasil.Where(h => h.Ply < s.Soal && h.Banding != "sama"))
        {
            var dulu = Papan.DariFen(h.Ply == 0 ? u.FenAwal : u.Langkah[h.Ply - 1].Fen)!;
            sb.Append(t[$"Tebakan siswa di soal sebelumnya: {Label(dulu, h.Tebakan)}, tetapi di partai dimainkan {Label(dulu, u.Langkah[h.Ply].Langkah.Uci)}; posisi soal ini mengikuti partai.\n",
                $"The student's guess in an earlier question: {Label(dulu, h.Tebakan)}, but the game went {Label(dulu, u.Langkah[h.Ply].Langkah.Uci)}; this question's position follows the game.\n"]);
        }
        sb.Append(Gambaran(posisi, t)).Append('\n');

        var kandidat = s.Awal.OrderByDescending(n => n.Cp).ToList();
        if (kandidat.Count > 0)
        {
            sb.Append(t["⚙️ Stockfish, langkah terbaik (nilai dari sudut putih):", "⚙️ Stockfish, best moves (scores from white's side):"]).Append('\n');
            var i = 0;
            foreach (var n in kandidat.Take(3))
                sb.Append(++i).Append(". ").Append(Label(posisi, n.Uci)).Append(" (").Append(Nilai(n, posisi, t)).Append("): ")
                    .Append(Garis(s, posisi, string.Join(' ', n.Pv), 8)).Append('\n');
        }
        var partai = u.Langkah[s.Soal].Langkah.Uci;
        sb.Append(t["Langkah di partai: ", "Move played in the game: "]).Append(Label(posisi, partai));
        if (kandidat.FirstOrDefault(n => n.Uci == partai) is { } np)
        {
            var hilang = Peluang(kandidat[0].Cp) - Peluang(np.Cp);
            sb.Append(" (⚙️ ").Append(Nilai(np, posisi, t)).Append("; ").Append(TeksJenis(t, Jenis(hilang)))
                .Append(hilang >= 1 ? t[$", peluang menang −{Math.Round(hilang)}%", $", winning chance −{Math.Round(hilang)}%"] : "").Append(')');
        }
        sb.Append('\n').Append(t[
            "Siswa tidak melihat data ini. Jangan sebut langkah partai, kandidat Stockfish, atau nilai apa pun sebelum siswa menyebut langkahnya.",
            "The student can't see this data. Don't mention the game move, Stockfish's candidates, or any score before the student names their move."]);
        return sb.Append("\nPOSISI>>>").ToString();
    }

    static string Gambar(Papan p)
    {
        var sb = new StringBuilder("  a b c d e f g h\n");
        for (var r = 7; r >= 0; r--)
        {
            sb.Append(r + 1);
            for (var f = 0; f < 8; f++)
                sb.Append(' ').Append(p[r * 8 + f] is var c and not '\0' ? c : '.');
            sb.Append(' ').Append(r + 1).Append('\n');
        }
        return sb.Append("  a b c d e f g h\n").ToString();
    }

    static string Giliran(Teks t, Papan p) =>
        t[$"Langkah {p.NomorLangkah}, {(p.GiliranPutih ? "putih" : "hitam")} melangkah.", $"Move {p.NomorLangkah}, {(p.GiliranPutih ? "white" : "black")} to move."];

    // Beberapa langkah partai terakhir sebelum ply itu: "11. d4 Ne4 12. Nf3".
    static string Sebelumnya(UraianPartai u, int ply, int jumlah, bool untukAi = false)
    {
        var awal = Math.Max(0, ply - jumlah);
        var posisi = Papan.DariFen(awal == 0 ? u.FenAwal : u.Langkah[awal - 1].Fen)!;
        return HalamanCatur.Variasi(posisi, string.Join(' ', u.Langkah.Skip(awal).Take(ply - awal).Select(l => l.Langkah.Uci)), jumlah, untukAi);
    }

    // Variasi untuk teks yang dibaca AI: setiap langkah dengan nomor dan
    // pihaknya. Langkah variasi yang dinilai dicatat (lihat
    // LangkahBernomorBelumDiuji); yang hanya dilihat (lihat_posisi) tidak.
    static string Garis(SesiLatihan s, Papan posisi, string? uci, int maks, bool dinilai = true)
    {
        if (dinilai)
            DaftarkanLangkah(s, posisi, (uci ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maks));
        return HalamanCatur.Variasi(posisi, uci, maks, setiapNomor: true);
    }

    static void DaftarkanLangkah(SesiLatihan s, Papan awal, IEnumerable<string> langkah)
    {
        lock (s.Gembok)
        {
            var papan = awal;
            foreach (var l in langkah)
            {
                if (papan.DariNotasi(l) is not { } langkahSah)
                    break;
                DaftarkanLangkah(s, papan, langkahSah);
                papan = papan.Jalankan(langkahSah);
            }
        }
    }

    // "17|False|~Qh2", dan "…|x" kalau langkah itu memakan.
    static void DaftarkanLangkah(SesiLatihan s, Papan posisi, Langkah langkah)
    {
        if (KunciKasar(posisi.San(langkah)) is not { } kunci)
            return;
        var dasar = $"{posisi.NomorLangkah}|{posisi.GiliranPutih}|{kunci}";
        lock (s.Gembok)
        {
            s.LangkahDinilai.Add(dasar);
            if (posisi[langkah.Ke] != '\0' || (char.ToUpperInvariant(posisi[langkah.Dari]) == 'P' && langkah.Dari % 8 != langkah.Ke % 8))
                s.LangkahDinilai.Add(dasar + "|x");
        }
    }

    // ---------- tool cek_variasi ----------

    // Langkah utama dan variasi siswa, diperiksa langkah demi langkah dari
    // posisi soal. Notasinya pemaaf (Papan.DariNotasi); nomor langkah dan
    // tanda ! ? dibuang.
    internal static CekVariasi BacaCek(string argumen, Papan posisi)
    {
        string? utama = null;
        var variasi = new List<string>();
        try
        {
            using var dok = JsonDocument.Parse(argumen);
            if (dok.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (dok.RootElement.TryGetProperty("utama", out var u) && u.ValueKind == JsonValueKind.String)
                    utama = u.GetString();
                if (dok.RootElement.TryGetProperty("variasi", out var v) && v.ValueKind == JsonValueKind.Array)
                    foreach (var x in v.EnumerateArray())
                        if (x.ValueKind == JsonValueKind.String && variasi.Count < MaksVariasi)
                            variasi.Add(x.GetString()!);
            }
        }
        catch (JsonException)
        {
            return new(null, "argumen bukan JSON", []);
        }

        var daftar = variasi.Select(v => Periksa(v, posisi)).ToList();
        Langkah? langkahUtama = null;
        string? galatUtama = null;
        if (utama is not null && Bersihkan(utama).FirstOrDefault() is { } notasi)
        {
            langkahUtama = posisi.DariNotasi(notasi);
            if (langkahUtama is null)
                galatUtama = notasi;
        }
        langkahUtama ??= daftar.FirstOrDefault(v => v.Langkah.Count > 0)?.Langkah[0].Langkah;
        return new(langkahUtama, galatUtama, daftar);
    }

    static VariasiSiswa Periksa(string teks, Papan posisi, int maks = MaksLangkahVariasi)
    {
        var hasil = new List<(Langkah, string, Papan)>();
        var bukanMakan = new List<(int, string)>();
        var papan = posisi;
        foreach (var notasi in Bersihkan(teks).Take(maks))
        {
            if (papan.DariNotasi(notasi) is not { } langkah)
                return new(hasil, notasi, papan, bukanMakan);
            // Notasinya pemaaf: "Bxb2" ke petak kosong tetap terbaca Bb2, tetapi
            // siswa yang menulis x mengira ada bidak di sana (terlihat 7 Okt
            // 2026: pion b2 sudah maju ke b4). Dicatat supaya diluruskan.
            var pion = char.ToUpperInvariant(papan[langkah.Dari]) == 'P';
            if (notasi.Contains('x') && papan[langkah.Ke] == '\0' && !(pion && langkah.Dari % 8 != langkah.Ke % 8))
                bukanMakan.Add((hasil.Count, notasi));
            hasil.Add((langkah, papan.San(langkah), papan));
            papan = papan.Jalankan(langkah);
        }
        return new(hasil, null, null, bukanMakan);
    }

    // "14. Nf3 Nc6!? 15.O-O" → Nf3, Nc6, O-O.
    static IEnumerable<string> Bersihkan(string teks)
    {
        foreach (var kata in teks.Split([' ', ',', ';', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var k = kata.TrimStart("0123456789".ToCharArray());
            k = k.TrimStart('.', '…').Trim('!', '?', '(', ')');
            if (k.Length > 0)
                yield return k;
        }
    }

    // Pekerjaan Stockfish untuk panggilan-panggilan cek_variasi: nilai langkah
    // pertama yang belum ada di analisis awal, lalu SETIAP posisi di setiap
    // variasi (langkah terbaik di posisi itu dan nilai langkah variasinya),
    // untuk kedua pihak, dan posisi akhirnya. Versi pertama hanya memeriksa
    // dua balasan lawan pertama, jadi variasi panjang yang patah di langkah
    // ke-7 tetap terlihat benar. Posisi yang sama di beberapa cabang hanya
    // dianalisis sekali; paling banyak MaksTugas posisi.
    static List<TugasMesin> Tugas(SesiLatihan s, IEnumerable<CekVariasi> semuaCek)
    {
        var tugas = new List<TugasMesin>();
        void Tambah(string fen, int jumlah, string? cari, int kedalaman, int waktu)
        {
            var i = tugas.FindIndex(x => x.Fen == fen);
            if (i < 0)
            {
                if (tugas.Count < MaksTugas)
                    tugas.Add(new(fen, jumlah, cari is null ? [] : [cari], kedalaman, waktu));
            }
            else if (cari is not null && !tugas[i].Cari.Contains(cari))
                tugas[i] = tugas[i] with { Cari = [.. tugas[i].Cari, cari], Jumlah = Math.Max(jumlah, tugas[i].Jumlah) };
        }

        var fenSoal = s.PosisiSoal.Fen();
        lock (s.Gembok)
            foreach (var cek in semuaCek)
            {
                foreach (var uci in cek.Variasi.Where(v => v.Langkah.Count > 0).Select(v => v.Langkah[0].Langkah.Uci).Prepend(cek.Utama!.Value.Uci))
                    if (!s.Awal.Any(n => n.Uci == uci))
                        Tambah(fenSoal, 0, uci, 16, 1500);
            }
        lock (s.Gembok)
            foreach (var cek in semuaCek)
                foreach (var v in cek.Variasi.Where(v => v.Langkah.Count >= 2))
                {
                    for (var j = 1; j < v.Langkah.Count; j++)
                        Tambah(v.Langkah[j].Sebelum.Fen(), 1, v.Langkah[j].Langkah.Uci, 14, 500);
                    var akhir = v.Langkah[^1];
                    var posisiAkhir = akhir.Sebelum.Jalankan(akhir.Langkah);
                    if (posisiAkhir.LangkahSah().Count > 0)
                        Tambah(posisiAkhir.Fen(), 1, null, 14, 800);
                }
        return tugas;
    }

    // Laporan satu cek_variasi untuk AI. Penilaian langkah utama juga dicatat
    // sebagai hasil soal (hanya jawaban pertama di tiap soal).
    string Laporan(SesiLatihan s, CekVariasi cek, Dictionary<string, List<NilaiMesin>> nilai, Teks t)
    {
        var posisi = s.PosisiSoal;
        var fen = posisi.Fen();
        List<NilaiMesin> Di(string f) => [.. (f == fen ? s.Awal : []).Concat(nilai.GetValueOrDefault(f) ?? [])];
        NilaiMesin? Cari(string f, string uci) => Di(f).Where(n => n.Uci == uci).OrderByDescending(n => n.Pv.Count).FirstOrDefault();

        var sb = new StringBuilder();
        if (cek.GalatUtama is { } salah)
            sb.Append(t["Langkah utama: ", "Main move: "]).Append(TidakSah(posisi, salah, t)).Append('\n');

        var diSoal = Di(fen);
        if (cek.Utama is { } utama && diSoal.Count > 0)
        {
            var terbaik = diSoal.MaxBy(n => n.Cp)!;
            var atas = Peluang(terbaik.Cp);
            var partai = s.Uraian.Langkah[s.Soal].Langkah.Uci;
            sb.Append(t["⚙️ Terbaik: ", "⚙️ Best: "]).Append(Label(posisi, terbaik.Uci)).Append(" (").Append(Nilai(terbaik, posisi, t)).Append("): ")
                .Append(Garis(s, posisi, string.Join(' ', terbaik.Pv), 8)).Append('\n');
            var np = Cari(fen, partai);
            if (Cari(fen, utama.Uci) is { } nu)
            {
                var hilang = Math.Max(0, atas - Peluang(nu.Cp));
                var hilangPartai = np is null ? hilang : Math.Max(0, atas - Peluang(np.Cp));
                var jenis = Jenis(hilang);
                var banding = utama.Uci == partai ? "sama" : Math.Abs(hilangPartai - hilang) < 5 ? "setara" : hilangPartai > hilang ? "lebih-baik" : "lebih-buruk";
                sb.Append(t["Langkah utama siswa: ", "The student's main move: "]).Append(Label(posisi, utama.Uci)).Append(" (⚙️ ").Append(Nilai(nu, posisi, t)).Append("): ")
                    .Append(TeksJenis(t, jenis)).Append(hilang >= 1 ? t[$", peluang menang −{Math.Round(hilang)}% dibanding terbaik", $", winning chance −{Math.Round(hilang)}% compared with the best"] : "")
                    .Append(". ").Append(t["Dibanding langkah partai", "Compared with the game move"]).Append(' ').Append(Label(posisi, partai))
                    .Append(np is null ? "" : $" (⚙️ {Nilai(np, posisi, t)})").Append(": ").Append(TeksBanding(t, banding)).Append(".\n");
                sb.Append(SeranganLangkah(posisi, utama, t)).Append('\n');
                if (nu.Pv.Count > 1)
                    sb.Append(t["⚙️ Lanjutan terbaik sesudahnya: ", "⚙️ Best continuation after it: "]).Append(Garis(s, posisi, string.Join(' ', nu.Pv), 8)).Append('\n');
                lock (s.Gembok)
                    if (!s.Hasil.Any(h => h.Ply == s.Soal))
                        s.Hasil.Add(new(s.Soal, utama.Uci, banding, jenis, (int)Math.Round(hilang), string.Join(' ', terbaik.Pv.Take(8))));
            }
            else
                sb.Append(t["Nilai langkah utama tidak didapat dari Stockfish.", "Stockfish gave no score for the main move."]).Append('\n');
        }

        var k = 0;
        foreach (var v in cek.Variasi)
        {
            k++;
            sb.Append(t[$"Variasi {k}: ", $"Line {k}: "]);
            if (v.Langkah.Count == 0)
            {
                sb.Append(TidakSah(posisi, v.Galat!, t)).Append('\n');
                continue;
            }
            sb.Append(Garis(s, posisi, string.Join(' ', v.Langkah.Select(l => l.Langkah.Uci)), MaksLangkahVariasi));
            if (v.Galat is { } g && v.PosisiGalat is { } di)
                sb.Append(t[" (berhenti di sini: ", " (stops here: "]).Append(TidakSah(di, g, t)).Append(')');
            sb.Append('\n');
            // Tiap langkah dibandingkan dengan langkah terbaik di posisinya.
            // Langkah pihak siswa yang kehilangan ≥10% peluang menang adalah
            // kesalahan di variasi itu; langkah lawan yang kehilangan ≥10%
            // berarti variasinya mengandaikan lawan bermain lebih lemah dari
            // yang bisa (variasinya "patah" di situ).
            var sisiSiswa = s.Putih ? t["putih", "white"] : t["hitam", "black"];
            var sisiLawan = s.Putih ? t["hitam", "black"] : t["putih", "white"];
            foreach (var (ke, notasi) in v.BukanMakan ?? [])
            {
                var (langkah, _, sebelum) = v.Langkah[ke];
                sb.Append(t[$"- Catatan: \"{notasi}\" ditulis sebagai langkah makan, padahal petak {Papan.NamaKotak(langkah.Ke)} kosong; dibaca sebagai {Label(sebelum, langkah.Uci)}.\n",
                    $"- Note: \"{notasi}\" is written as a capture, but {Papan.NamaKotak(langkah.Ke)} is empty; read as {Label(sebelum, langkah.Uci)}.\n"]);
            }
            var catatan = 0;
            var tidakDinilai = 0;
            string? ancamanPutih = null, ancamanHitam = null;
            for (var j = 0; j < v.Langkah.Count; j++)
            {
                var (langkah, _, sebelum) = v.Langkah[j];
                var utamaIni = j == 0 && cek.Utama?.Uci == langkah.Uci;   // sudah dinilai di atas
                var f = sebelum.Fen();
                if (!utamaIni)
                {
                    if (Di(f).MaxBy(n => n.Cp) is not { } terbaik || Cari(f, langkah.Uci) is not { } nl)
                        tidakDinilai++;
                    else
                    {
                        // Peluang menang saja tidak cukup untuk menguji variasi: di posisi
                        // yang sudah kalah, memberikan menteri hanya mengubah peluang
                        // menang beberapa persen (terlihat 7 Okt 2026: 16. Qxf3?? Bxf3
                        // lolos sebagai "sesuai ⚙️"). Jadi selisih nilai ≥1,5 pion juga
                        // dicatat; nilai mat dipotong ke ±20 pion.
                        var hilang = Math.Max(0, Peluang(terbaik.Cp) - Peluang(nl.Cp));
                        var selisih = Math.Clamp(terbaik.Cp, -2000, 2000) - Math.Clamp(nl.Cp, -2000, 2000);
                        if (hilang >= 10 || selisih >= 150)
                        {
                            catatan++;
                            var garis = Garis(s, sebelum, string.Join(' ', terbaik.Pv), 6);
                            var berat = hilang >= 10
                                ? t[$"peluang menang −{Math.Round(hilang)}%", $"winning chance −{Math.Round(hilang)}%"]
                                : t[$"selisih ⚙️ {Pion(selisih, t)} pion", $"⚙️ difference {Pion(selisih, t)} pawns"];
                            if (terbaik.Mat > 0 && !(nl.Mat > 0))
                                berat += t[$", melewatkan skakmat ({Nilai(terbaik, sebelum, t)})", $", missing a mate ({Nilai(terbaik, sebelum, t)})"];
                            else if (nl.Mat < 0 && !(terbaik.Mat < 0))
                                berat += t[", membiarkan skakmat", ", allowing a mate"];
                            sb.Append("- ").Append(Label(sebelum, langkah.Uci)).Append(" (⚙️ ").Append(Nilai(nl, sebelum, t)).Append("): ").Append(sebelum.GiliranPutih == s.Putih
                                ? t[$"langkah {sisiSiswa} (pihak siswa) ini {(hilang >= 10 ? TeksJenis(t, Jenis(hilang)) : "bukan yang terbaik")} ({berat}); ⚙️ lebih baik {garis}",
                                    $"this {sisiSiswa} move (the student's side) is {(hilang >= 10 ? TeksJenis(t, Jenis(hilang)) : "not the best")} ({berat}); ⚙️ better {garis}"]
                                : t[$"bukan langkah terbaik {sisiLawan} ({berat}), jadi variasi ini mengandaikan lawan bermain lebih lemah; ⚙️ yang lebih kuat {garis}",
                                    $"not {sisiLawan}'s best move ({berat}), so this line assumes weaker play by the opponent; ⚙️ stronger is {garis}"])
                                .Append(" (").Append(Nilai(terbaik, sebelum, t)).Append(").\n");
                        }
                    }
                }
                // Ancaman mat yang muncul sesudah langkah ini (dihitung, tanpa
                // Stockfish); yang sama dengan sesudah langkah sebelumnya pihak
                // itu tidak diulang. Ancaman langkah utama sudah tertulis di atas.
                var sesudah = sebelum.Jalankan(langkah);
                var ancaman = AncamanMat(sesudah);
                var kunci = ancaman.Count == 0 ? null : string.Join(' ', ancaman);
                ref var lalu = ref sebelum.GiliranPutih ? ref ancamanPutih : ref ancamanHitam;
                if (kunci is not null && kunci != lalu && !utamaIni)
                    sb.Append(t["- Sesudah ", "- After "]).Append(Label(sebelum, langkah.Uci)).Append(": ").Append(TeksAncaman(sesudah, ancaman, t)).Append('\n');
                lalu = kunci;
            }
            if (tidakDinilai > 0)
                sb.Append(t[$"- {tidakDinilai} langkah tidak sempat dinilai Stockfish; jangan menilai langkah itu tanpa memeriksanya lagi.\n",
                    $"- {tidakDinilai} moves weren't judged by Stockfish; don't judge them without checking again.\n"]);
            else if (catatan == 0 && v.Langkah.Count > 1)
                sb.Append(t["- Semua langkah lain di variasi ini sesuai ⚙️ (langkah terbaik, atau selisihnya kurang dari 10% peluang menang).\n",
                    "- All other moves in this line agree with ⚙️ (the best move, or less than 10% winning chance away from it).\n"]);
            if (v.Langkah.Count >= 2)
            {
                var akhir = v.Langkah[^1];
                var posisiAkhir = akhir.Sebelum.Jalankan(akhir.Langkah);
                if (posisiAkhir.LangkahSah().Count == 0)
                    sb.Append(posisiAkhir.Skak ? t["- Akhir variasi: skakmat.\n", "- End of the line: checkmate.\n"] : t["- Akhir variasi: pat (remis).\n", "- End of the line: stalemate (draw).\n"]);
                else if (Di(posisiAkhir.Fen()).MaxBy(n => n.Cp) is { } n)
                    sb.Append(t["- Akhir variasi: ⚙️ ", "- End of the line: ⚙️ "]).Append(Nilai(n, posisiAkhir, t)).Append("; ").Append(TeksMateri(posisiAkhir, t))
                        .Append(t["; lanjutan terbaik ", "; best continuation "]).Append(Garis(s, posisiAkhir, string.Join(' ', n.Pv), 6)).Append(".\n");
            }
        }
        return sb.Length == 0 ? t["Tidak ada langkah yang bisa dinilai.", "No moves could be judged."] : sb.ToString().TrimEnd();
    }

    // ---------- tool lihat_posisi ----------

    /// <summary>
    /// Posisi sesudah deretan langkah dari posisi soal: papan, letak bidak,
    /// giliran, skak, dan langkah sah. Dijawab di sini (tanpa Stockfish), jadi
    /// AI bisa melihat posisi di tengah variasi alih-alih mengingatnya.
    /// </summary>
    internal static string LihatPosisi(SesiLatihan s, string argumen, Teks t)
    {
        var langkah = "";
        try
        {
            using var dok = JsonDocument.Parse(argumen);
            if (dok.RootElement.ValueKind == JsonValueKind.Object && dok.RootElement.TryGetProperty("langkah", out var l) && l.ValueKind == JsonValueKind.String)
                langkah = l.GetString()!;
        }
        catch (JsonException)
        {
        }
        var posisi = s.PosisiSoal;
        var v = Periksa(langkah, posisi, 2 * MaksLangkahVariasi);
        var akhir = v.Langkah.Count == 0 ? posisi : v.Langkah[^1].Sebelum.Jalankan(v.Langkah[^1].Langkah);
        var sb = new StringBuilder();
        if (v.Langkah.Count == 0)
            sb.Append(t["Posisi soal.", "The question position."]);
        else
            sb.Append(t["Posisi sesudah ", "Position after "]).Append(Garis(s, posisi, string.Join(' ', v.Langkah.Select(x => x.Langkah.Uci)), 2 * MaksLangkahVariasi, dinilai: false)).Append('.');
        if (v.Galat is { } g && v.PosisiGalat is { } di)
            sb.Append(t[" Berhenti di sini, karena ", " Stopped here, because "]).Append(TidakSah(di, g, t)).Append('.');
        return sb.Append('\n').Append(Gambaran(akhir, t)).ToString();
    }

    // Giliran, skak, papan, letak bidak, dan langkah sah satu posisi.
    internal static string Gambaran(Papan p, Teks t)
    {
        var sah = p.LangkahSah();
        var sb = new StringBuilder(p.GiliranPutih ? t["Putih melangkah", "White to move"] : t["Hitam melangkah", "Black to move"]);
        sb.Append(sah.Count == 0 ? (p.Skak ? t["; skakmat.", "; checkmate."] : t["; pat.", "; stalemate."]) : p.Skak ? t["; skak.", "; in check."] : ".");
        sb.Append('\n').Append(t["Papan (huruf besar = putih, titik = kosong):", "Board (capitals = white, dots = empty):"]).Append('\n').Append(Gambar(p));
        foreach (var putih in new[] { true, false })
        {
            var bidak = new List<string>();
            var pion = new List<string>();
            foreach (var jenis in "KQRBNP")
                for (var i = 0; i < 64; i++)
                    if (p[i] == (putih ? jenis : char.ToLowerInvariant(jenis)))
                        (jenis == 'P' ? pion : bidak).Add(jenis == 'P' ? Papan.NamaKotak(i) : $"{jenis}{Papan.NamaKotak(i)}");
            sb.Append(putih ? t["Putih: ", "White: "] : t["Hitam: ", "Black: "]).Append(string.Join(", ", bidak))
                .Append(pion.Count == 0 ? "" : t["; pion ", "; pawns "] + string.Join(", ", pion)).Append('\n');
        }
        var (materiPutih, materiHitam) = Materi(p);
        sb.Append(t[$"Materi (pion 1, kuda dan gajah 3, benteng 5, menteri 9): putih {materiPutih}, hitam {materiHitam}; {TeksMateri(p, t)}.",
            $"Material (pawn 1, knight and bishop 3, rook 5, queen 9): white {materiPutih}, black {materiHitam}; {TeksMateri(p, t)}."]).Append('\n');
        sb.Append(Struktur(p, t));
        sb.Append(Serangan(p, t));
        if (MatLangsung(p) is { Count: > 0 } mat)
            sb.Append(p.GiliranPutih ? t["Putih", "White"] : t["Hitam", "Black"])
                .Append(t[$" bisa langsung skakmat dengan {string.Join(" atau ", mat)}.", $" can mate at once with {string.Join(" or ", mat)}."]).Append('\n');
        if (sah.Count > 0 && !p.Skak)
            sb.Append(AncamanMat(p) is { Count: > 0 } ancaman ? TeksAncaman(p, ancaman, t)
                : t[$"Tidak ada ancaman skakmat satu langkah dari {(p.GiliranPutih ? "hitam" : "putih")}.", $"No mate-in-one threat from {(p.GiliranPutih ? "black" : "white")}."]).Append('\n');
        if (sah.Count > 0)
            sb.Append(t["Langkah sah: ", "Legal moves: "]).Append(string.Join(", ", sah.Select(l => p.San(l, sah)))).Append('\n');
        return sb.ToString().TrimEnd();
    }

    // Nilai bidak biasa. AI pernah menyetujui "secara materi imbang" padahal
    // hitam unggul dua pion (DeepSeek sungguhan, 7 Okt 2026).
    static (int Putih, int Hitam) Materi(Papan p)
    {
        int putih = 0, hitam = 0;
        for (var i = 0; i < 64; i++)
        {
            var nilai = char.ToUpperInvariant(p[i]) switch { 'P' => 1, 'N' or 'B' => 3, 'R' => 5, 'Q' => 9, _ => 0 };
            if (p[i] is >= 'a')
                hitam += nilai;
            else
                putih += nilai;
        }
        return (putih, hitam);
    }

    static string TeksMateri(Papan p, Teks t)
    {
        var (putih, hitam) = Materi(p);
        var selisih = putih - hitam;
        return selisih == 0 ? t["materi seimbang", "material is level"]
            : t[$"materi {(selisih > 0 ? "putih" : "hitam")} unggul {Math.Abs(selisih)}", $"{(selisih > 0 ? "white" : "black")} is up {Math.Abs(selisih)} in material"];
    }

    /// <summary>
    /// Fakta struktur yang dihitung, bukan ditebak AI: pion terisolasi, ganda,
    /// dan lolos tiap pihak, serta bidak dan pion yang diserang lawan tanpa
    /// pelindung. Ditambahkan sesudah AI menyebut pion d4 "terisolasi" padahal
    /// ada pion putih di c4 (DeepSeek sungguhan, 7 Okt 2026).
    /// </summary>
    internal static string Struktur(Papan p, Teks t)
    {
        static string Daftar(Teks t, List<string> isi) => isi.Count == 0 ? t["tidak ada", "none"] : string.Join(", ", isi);
        var sb = new StringBuilder();
        foreach (var putih in new[] { true, false })
        {
            char pion = putih ? 'P' : 'p', lawan = putih ? 'p' : 'P';
            bool Ada(int lajur, int dari, int sampai, char c)
            {
                for (var r = dari; r <= sampai; r++)
                    if (lajur is >= 0 and <= 7 && p[r * 8 + lajur] == c)
                        return true;
                return false;
            }
            List<string> terisolasi = [], ganda = [], lolos = [];
            for (var i = 0; i < 64; i++)
            {
                if (p[i] != pion)
                    continue;
                int f = i % 8, r = i / 8;
                var nama = Papan.NamaKotak(i);
                if (!Ada(f - 1, 0, 7, pion) && !Ada(f + 1, 0, 7, pion))
                    terisolasi.Add(nama);
                if (Enumerable.Range(0, 8).Count(rr => p[rr * 8 + f] == pion) > 1)
                    ganda.Add(nama);
                var (dari, sampai) = putih ? (r + 1, 7) : (0, r - 1);
                if (!Ada(f - 1, dari, sampai, lawan) && !Ada(f, dari, sampai, lawan) && !Ada(f + 1, dari, sampai, lawan))
                    lolos.Add(nama);
            }
            var sisi = putih ? t["putih", "white"] : t["hitam", "black"];
            sb.Append(t[$"Pion {sisi}: terisolasi {Daftar(t, terisolasi)}; ganda {Daftar(t, ganda)}; lolos {Daftar(t, lolos)}.\n",
                $"{(putih ? "White" : "Black")} pawns: isolated {Daftar(t, terisolasi)}; doubled {Daftar(t, ganda)}; passed {Daftar(t, lolos)}.\n"]);
        }
        var tergantung = new List<string>();
        for (var i = 0; i < 64; i++)
        {
            var c = p[i];
            if (c == '\0' || char.ToUpperInvariant(c) == 'K')
                continue;
            var putih = c < 'a';
            if (p.DiSerang(i, !putih) && !p.DiSerang(i, putih))
                tergantung.Add($"{NamaBidak(t, char.ToUpperInvariant(c))} {(putih ? t["putih", "white"] : t["hitam", "black"])} {Papan.NamaKotak(i)}");
        }
        sb.Append(t["Diserang lawan tanpa pelindung: ", "Attacked and undefended: "]).Append(Daftar(t, tergantung)).Append(".\n");
        return sb.ToString();
    }

    // Bidak pihak itu yang menyerang petak sasaran (geometri biasa: paku dan
    // skak tidak diperhitungkan, seperti arti "menyerang" sehari-hari).
    static List<int> Penyerang(Papan p, int sasaran, bool olehPutih)
    {
        var hasil = new List<int>();
        int sf = sasaran % 8, sr = sasaran / 8;
        for (var i = 0; i < 64; i++)
        {
            var c = p[i];
            if (c == '\0' || (c < 'a') != olehPutih || i == sasaran)
                continue;
            int df = sf - i % 8, dr = sr - i / 8;
            var kena = char.ToUpperInvariant(c) switch
            {
                'P' => Math.Abs(df) == 1 && dr == (olehPutih ? 1 : -1),
                'N' => Math.Abs(df * dr) == 2,
                'K' => Math.Max(Math.Abs(df), Math.Abs(dr)) == 1,
                'B' => Math.Abs(df) == Math.Abs(dr) && Bebas(p, i, sasaran),
                'R' => (df == 0 || dr == 0) && Bebas(p, i, sasaran),
                'Q' => (df == 0 || dr == 0 || Math.Abs(df) == Math.Abs(dr)) && Bebas(p, i, sasaran),
                _ => false,
            };
            if (kena)
                hasil.Add(i);
        }
        return hasil;
    }

    // Petak di antara dua petak segaris kosong semua.
    static bool Bebas(Papan p, int dari, int ke)
    {
        int langkahF = Math.Sign(ke % 8 - dari % 8), langkahR = Math.Sign(ke / 8 - dari / 8);
        for (var k = dari + langkahR * 8 + langkahF; k != ke; k += langkahR * 8 + langkahF)
            if (p[k] != '\0')
                return false;
        return true;
    }

    static string NamaPetak(Papan p, int k, Teks t) =>
        $"{NamaBidak(t, char.ToUpperInvariant(p[k]))} {(p[k] < 'a' ? t["putih", "white"] : t["hitam", "black"])} {Papan.NamaKotak(k)}";

    /// <summary>
    /// Siapa menyerang apa, dihitung: bidak dan pion tiap pihak yang diserang
    /// lawan beserta penyerangnya. AI pernah menyangkal bahwa kuda e5
    /// menyerang menteri d3 (DeepSeek sungguhan, 7 Okt 2026).
    /// </summary>
    internal static string Serangan(Papan p, Teks t)
    {
        var sb = new StringBuilder();
        foreach (var olehPutih in new[] { true, false })
        {
            var daftar = new List<string>();
            for (var k = 0; k < 64; k++)
            {
                if (p[k] == '\0' || (p[k] < 'a') == olehPutih || char.ToUpperInvariant(p[k]) == 'K')
                    continue;
                if (Penyerang(p, k, olehPutih) is { Count: > 0 } penyerang)
                    daftar.Add($"{NamaPetak(p, k, t)} ({t["oleh", "by"]} {string.Join(", ", penyerang.Select(i => $"{NamaBidak(t, char.ToUpperInvariant(p[i]))} {Papan.NamaKotak(i)}"))})");
            }
            sb.Append(olehPutih ? t["Putih menyerang: ", "White attacks: "] : t["Hitam menyerang: ", "Black attacks: "])
                .Append(daftar.Count == 0 ? t["tidak ada", "nothing"] : string.Join("; ", daftar)).Append(".\n");
        }
        return sb.ToString();
    }

    // "Sesudah 14... Nxe5, kuda e5 menyerang: menteri putih d3, …" (dan skak),
    // ditambah ancaman matnya kalau ada.
    static string SeranganLangkah(Papan posisi, Langkah langkah, Teks t)
    {
        var sesudah = posisi.Jalankan(langkah);
        var putih = posisi.GiliranPutih;
        var diserang = Enumerable.Range(0, 64)
            .Where(k => sesudah[k] != '\0' && (sesudah[k] < 'a') != putih && Penyerang(sesudah, k, putih).Contains(langkah.Ke))
            .Select(k => char.ToUpperInvariant(sesudah[k]) == 'K' ? t["raja (skak)", "the king (check)"] : NamaPetak(sesudah, k, t)).ToList();
        var teks = t[$"Sesudah {Label(posisi, langkah.Uci)}, {NamaBidak(t, char.ToUpperInvariant(sesudah[langkah.Ke]))} {Papan.NamaKotak(langkah.Ke)} menyerang: {(diserang.Count == 0 ? "tidak ada bidak lawan" : string.Join(", ", diserang))}.",
            $"After {Label(posisi, langkah.Uci)}, the {NamaBidak(t, char.ToUpperInvariant(sesudah[langkah.Ke]))} on {Papan.NamaKotak(langkah.Ke)} attacks: {(diserang.Count == 0 ? "no enemy piece" : string.Join(", ", diserang))}."];
        return AncamanMat(sesudah) is { Count: > 0 } ancaman ? teks + " " + TeksAncaman(sesudah, ancaman, t) : teks;
    }

    /// <summary>
    /// Ancaman mat satu langkah pihak yang baru melangkah: langkah yang
    /// langsung memberi skakmat seandainya ia boleh melangkah lagi (langkah
    /// nol). Dihitung, bukan ditebak: AI pernah menyangkal ancaman Qxh2#
    /// sesudah 16. Bxd7 Bd6 ("raja putih masih dilindungi", DeepSeek
    /// sungguhan, 7 Okt 2026).
    /// </summary>
    internal static List<string> AncamanMat(Papan sesudah) => sesudah.Lewat() is { } lewat ? MatLangsung(lewat) : [];

    /// <summary>Langkah pihak yang mendapat giliran yang langsung memberi skakmat.</summary>
    internal static List<string> MatLangsung(Papan p)
    {
        var sah = p.LangkahSah();
        return [.. sah.Where(l => p.Jalankan(l) is { Skak: true } q && q.LangkahSah().Count == 0).Select(l => p.San(l, sah))];
    }

    // "Hitam mengancam skakmat dengan Qxh2# (kalau putih tidak mencegahnya)."
    static string TeksAncaman(Papan sesudah, List<string> ancaman, Teks t)
    {
        string pengancam = sesudah.GiliranPutih ? t["Hitam", "Black"] : t["Putih", "White"], lawan = sesudah.GiliranPutih ? t["putih", "white"] : t["hitam", "black"];
        return t[$"{pengancam} mengancam skakmat dengan {string.Join(" atau ", ancaman)} (kalau {lawan} tidak mencegahnya).",
            $"{pengancam} threatens mate with {string.Join(" or ", ancaman)} (if {lawan} doesn't stop it)."];
    }

    /// <summary>
    /// Langkah bernomor di jawaban AI ("16. Bxd6", "17... d3", "16. Bd2 Be4")
    /// yang belum pernah muncul dengan nomor dan giliran itu di variasi yang
    /// dinilai: partai, lanjutan Stockfish, variasi yang diuji. Ancaman mat
    /// sengaja tidak ikut: "5. Qxf7#" yang diancam sesudah 4. Qf3 tidak sah
    /// lagi sesudah 4... Nf6, padahal nomornya sama.
    /// Bidak dan petak tujuannya harus cocok; "x" hanya kalau memang memakan.
    /// Versi pertama hanya meminta langkahnya sah di posisi yang dikenal,
    /// jadi pertahanan karangan "17. g3 atau 17. f4" lolos (DeepSeek
    /// sungguhan, 7 Okt 2026). Notasi yang sudah dilaporkan tidak sah boleh
    /// disebut (untuk menjelaskannya).
    /// </summary>
    internal static List<string> LangkahBernomorBelumDiuji(SesiLatihan s, string teks)
    {
        var hasil = new List<string>();
        lock (s.Gembok)
            foreach (var (nomor, putih, san) in LangkahBernomor(teks))
            {
                var bersih = san.TrimEnd('+', '#', '!', '?');
                if (KunciKasar(bersih) is not { } kunci || s.Diperiksa.Contains("=" + bersih))
                    continue;
                if (!s.LangkahDinilai.Contains($"{nomor}|{putih}|{kunci}{(bersih.Contains('x') ? "|x" : "")}"))
                    hasil.Add($"{nomor}{(putih ? "." : "...")} {san}");
            }
        return [.. hasil.Distinct()];
    }

    // Notasi yang tidak sah untuk pihak yang melangkah, tetapi sah untuk lawannya.
    static bool SahUntukLawan(Papan p, string notasi) => p.Lewat() is { } lain && SahDi(lain, notasi.TrimEnd('+', '#', '!', '?'));

    // Sah, dan "x" hanya kalau memang memakan (notasi pemaaf membaca Bxd6 ke petak kosong sebagai Bd6).
    static bool SahDi(Papan p, string san)
    {
        if (p.DariNotasi(san) is not { } l)
            return false;
        var pion = char.ToUpperInvariant(p[l.Dari]) == 'P';
        return !san.Contains('x') || p[l.Ke] != '\0' || (pion && l.Dari % 8 != l.Ke % 8);
    }

    /// <summary>
    /// Langkah bernomor di teks: "16. Bd2" (putih), "16... Be4" (hitam), dan
    /// langkah yang langsung menyusul tanpa nomor ("16. Bd2 Be4": Be4 = 16...).
    /// Kata lain di antaranya memutus deretannya.
    /// </summary>
    internal static IEnumerable<(int Nomor, bool Putih, string San)> LangkahBernomor(string teks)
    {
        int? nomor = null;
        var putih = true;
        foreach (var kata in teks.Split([' ', '\n', '\t', ',', ';', '(', ')', '*', '"', '“', '”', '—', '–'], StringSplitOptions.RemoveEmptyEntries))
        {
            var k = kata.Trim();
            var angka = 0;
            while (angka < k.Length && char.IsAsciiDigit(k[angka]))
                angka++;
            if (angka > 0 && angka < k.Length && k[angka] is '.' or '…')
            {
                var sisa = k[angka..];
                var titik = 0;
                while (titik < sisa.Length && sisa[titik] is '.' or '…')
                    titik++;
                nomor = int.Parse(k[..angka]);
                putih = !(sisa.StartsWith("...", StringComparison.Ordinal) || sisa[0] == '…');
                k = sisa[titik..];
                if (k.Length == 0)
                    continue;
            }
            var san = k.TrimEnd('.', ':', '!', '?');
            if (nomor is null || !MiripSan(san))
            {
                nomor = null;
                continue;
            }
            yield return (nomor.Value, putih, san);
            if (!putih)
                nomor++;
            putih = !putih;
        }
    }

    // Langkah bidak, makan, rokade, atau langkah pion ("d5", "e8=Q").
    static bool MiripSan(string k)
    {
        var s = k.TrimEnd('+', '#');
        if (Langkah(s) is not null)
            return true;
        return s.Length is 2 or 4 && s[0] is >= 'a' and <= 'h' && s[1] is >= '1' and <= '8'
            && (s.Length == 2 || s[2] == '=' && "QRBN".Contains(s[3]));
    }

    // Kenapa notasi itu tidak sah di posisi itu: giliran siapa, langkah sah
    // bidak sejenis, dan isi petak tujuannya. Tanpa ini AI menebak alasannya
    // (terjadi 7 Okt 2026: "gajah di a4 tidak mengincar d6" benar, tetapi
    // sesudah dibantah siswa AI mengalah dan mengarang variasi).
    internal static string TidakSah(Papan p, string notasi, Teks t)
    {
        var n = notasi.TrimEnd('+', '#', '!', '?');
        var jenis = n.Length > 0 && "KQRBN".Contains(n[0]) ? n[0] : 'P';
        var sah = p.LangkahSah();
        var milik = sah.Where(l => char.ToUpperInvariant(p[l.Dari]) == jenis).Select(l => p.San(l, sah)).ToList();
        var sisi = p.GiliranPutih ? t["putih", "white"] : t["hitam", "black"];
        var sb = new StringBuilder(t[$"\"{notasi}\" tidak sah di sini ({sisi} melangkah); langkah {NamaBidak(t, jenis)} {sisi} yang sah: ",
            $"\"{notasi}\" isn't legal here ({sisi} to move); legal {NamaBidak(t, jenis)} moves for {sisi}: "]);
        sb.Append(milik.Count == 0 ? t["tidak ada", "none"] : string.Join(", ", milik));
        for (var i = n.Length - 2; i >= 0; i--)
            if (Papan.Kotak(n[i], n[i + 1]) is var k and >= 0)
            {
                sb.Append(t[$"; petak {n.Substring(i, 2)}: ", $"; square {n.Substring(i, 2)}: "]).Append(IsiPetak(p, k, t));
                foreach (var alasan in Kenapa(p, jenis, k, sah, t))
                    sb.Append("; ").Append(alasan);
                break;
            }
        // Langkah pihak lain yang ditaruh di giliran yang salah: siswa sering
        // melewatkan satu langkah lawan ("jika Bxd6, maka Bd6"; 7 Okt 2026).
        if (SahUntukLawan(p, n))
        {
            var lain = p.GiliranPutih ? t["hitam", "black"] : t["putih", "white"];
            sb.Append(t[$"; sebagai langkah {lain}, \"{n}\" sah di posisi ini, jadi mungkin ada langkah {sisi} yang terlewat sebelum langkah itu",
                $"; as a {lain} move, \"{n}\" is legal in this position, so a {sisi} move may be missing before it"]);
        }
        return sb.ToString();
    }

    static string IsiPetak(Papan p, int k, Teks t) => p[k] == '\0' ? t["kosong", "empty"]
        : $"{NamaBidak(t, char.ToUpperInvariant(p[k]))} {(p[k] < 'a' ? t["putih", "white"] : t["hitam", "black"])}";

    // Untuk tiap bidak sejenis milik yang melangkah: kenapa ia tidak bisa ke
    // petak itu. Jalur benteng, gajah, dan menteri yang terhalang (bidak
    // pertama di jalurnya), langkah yang membuat raja sendiri terkena skak,
    // dan notasi yang kurang jelas (dua bidak bisa ke petak itu). Bagian inilah
    // yang paling mudah dikarang AI kalau tidak diberi tahu.
    static IEnumerable<string> Kenapa(Papan p, char jenis, int ke, List<Langkah> sah, Teks t)
    {
        var bisa = sah.Where(l => l.Ke == ke && char.ToUpperInvariant(p[l.Dari]) == jenis).ToList();
        if (bisa.Count > 1)
        {
            yield return t[$"notasinya kurang jelas, lebih dari satu {NamaBidak(t, jenis)} bisa ke {Papan.NamaKotak(ke)}: {string.Join(", ", bisa.Select(l => p.San(l, sah)))}",
                $"the notation is ambiguous, more than one {NamaBidak(t, jenis)} can go to {Papan.NamaKotak(ke)}: {string.Join(", ", bisa.Select(l => p.San(l, sah)))}"];
            yield break;
        }
        var milikku = p.GiliranPutih ? jenis : char.ToLowerInvariant(jenis);
        var nama = NamaBidak(t, jenis);
        for (var i = 0; i < 64; i++)
        {
            if (p[i] != milikku || i == ke)
                continue;
            int df = ke % 8 - i % 8, dr = ke / 8 - i / 8;
            var lurus = df == 0 || dr == 0;
            var miring = Math.Abs(df) == Math.Abs(dr);
            var segaris = jenis switch { 'R' => lurus, 'B' => miring, 'Q' => lurus || miring, _ => false };
            var lompatan = jenis switch
            {
                'N' => Math.Abs(df * dr) == 2,
                'K' => Math.Abs(df) <= 1 && Math.Abs(dr) <= 1,
                _ => false,
            };
            if (!segaris && !lompatan)
                continue;
            if (segaris)
            {
                int langkahF = Math.Sign(df), langkahR = Math.Sign(dr), k = i + langkahR * 8 + langkahF;
                while (k != ke && p[k] == '\0')
                    k += langkahR * 8 + langkahF;
                if (k != ke)
                {
                    yield return t[$"{nama} {Papan.NamaKotak(i)} ke {Papan.NamaKotak(ke)} terhalang {IsiPetak(p, k, t)} di {Papan.NamaKotak(k)}",
                        $"the {nama} on {Papan.NamaKotak(i)} is blocked from {Papan.NamaKotak(ke)} by the {IsiPetak(p, k, t)} on {Papan.NamaKotak(k)}"];
                    continue;
                }
            }
            var sendiri = p[ke] != '\0' && (p[ke] < 'a') == p.GiliranPutih;
            if (!sendiri)
                yield return jenis == 'K'
                    ? t[$"raja tidak boleh ke {Papan.NamaKotak(ke)}: petak itu diserang lawan", $"the king can't go to {Papan.NamaKotak(ke)}: that square is attacked"]
                    : t[$"{nama} {Papan.NamaKotak(i)} ke {Papan.NamaKotak(ke)} membuat raja sendiri terkena skak", $"the {nama} on {Papan.NamaKotak(i)} going to {Papan.NamaKotak(ke)} would leave its own king in check"];
        }
    }

    static string NamaBidak(Teks t, char jenis) => jenis switch
    {
        'K' => t["raja", "king"],
        'Q' => t["menteri", "queen"],
        'R' => t["benteng", "rook"],
        'B' => t["gajah", "bishop"],
        'N' => t["kuda", "knight"],
        _ => t["pion", "pawn"],
    };

    // ---------- penjaga: langkah yang belum diuji ----------

    void TambahHasilTool(SesiLatihan s, string id, string hasil)
    {
        Catat($"hasil alat {id}:\n{hasil}");
        Kenali(s, hasil);
        s.Pesan.Add(new("tool", hasil, IdTool: id));
    }

    // Notasi dalam tanda kutip tidak dihitung: begitulah laporan menyebut
    // langkah yang tidak sah ("Bxd6" tidak sah di sini …). Daftar langkah sah
    // juga tidak: langkah yang sah belum tentu baik, dan AI pernah menulis
    // pertahanan karangan yang hanya ada di daftar itu (7 Okt 2026).
    static void Kenali(SesiLatihan s, string teks)
    {
        foreach (var l in LangkahDiTeks(TanpaDaftarSah(teks), lewatiKutipan: true))
            s.Dikenal.Add(l);
    }

    // Membuang "Langkah sah: …" (Gambaran) dan "… yang sah: …" (TidakSah),
    // sampai akhir baris atau sampai ; atau ).
    internal static string TanpaDaftarSah(string teks)
    {
        var sb = new StringBuilder(teks.Length);
        var i = 0;
        while (i < teks.Length)
        {
            var awal = -1;
            var panjang = 0;
            foreach (var label in new[] { "Langkah sah: ", "Legal moves: ", " yang sah: ", " moves for white: ", " moves for black: " })
                if (teks.IndexOf(label, i, StringComparison.Ordinal) is var j and >= 0 && (awal < 0 || j < awal))
                    (awal, panjang) = (j, label.Length);
            if (awal < 0)
            {
                sb.Append(teks, i, teks.Length - i);
                break;
            }
            sb.Append(teks, i, awal + panjang - i);
            var akhir = teks.IndexOfAny([';', ')', '\n'], awal + panjang);
            i = akhir < 0 ? teks.Length : akhir;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Langkah bidak dan langkah makan di jawaban AI yang belum pernah muncul
    /// di data posisi soal ini atau hasil tool (selain di daftar langkah sah),
    /// kecuali notasi yang sudah dilaporkan tidak sah. Langkah pion tanpa
    /// makan ("d5") tidak diperiksa: bentuknya sama dengan nama petak.
    /// </summary>
    internal static List<string> BelumDiuji(SesiLatihan s, string teks)
    {
        lock (s.Gembok)
            return [.. LangkahDiTeks(teks).Where(l => !s.Dikenal.Contains(l) && !s.Diperiksa.Contains("=" + l)).Distinct()];
    }

    /// <summary>
    /// Langkah bidak dan makan di pesan siswa yang belum sampai diperiksa
    /// cek_variasi di soal ini. Sudah diperiksa artinya dimainkan di variasi
    /// yang diuji (dicocokkan menurut bidak dan petak tujuan, jadi "Qe5" dan
    /// "Qxe5" dianggap sama), atau dilaporkan tidak sah persis seperti
    /// tulisannya. Menyebutnya di argumen saja tidak cukup: variasi yang
    /// patah sebelum langkah itu tidak pernah sampai ke sana (terlihat 7 Okt
    /// 2026, "Bxd6, maka Bd6"). Langkah partai sebelum soal tidak dihitung.
    /// </summary>
    internal static List<string> BelumDiperiksa(SesiLatihan s)
    {
        lock (s.Gembok)
        {
            var lalu = s.Uraian.Langkah.Take(Math.Max(0, s.Soal)).Select(l => KunciKasar(l.San)).ToHashSet();
            return [.. LangkahDiTeks(s.PesanSiswa)
                .Where(l => KunciKasar(l) is not { } k || !(s.Diperiksa.Contains(k) || s.Diperiksa.Contains("=" + l) || lalu.Contains(k)))
                .Distinct()];
        }
    }

    // Langkah yang tidak sah hanya terhitung diperiksa kalau tidak sah untuk
    // kedua pihak: "Bd6" yang dicoba sebagai langkah putih (tidak sah) belum
    // memeriksa Bd6 hitam yang dimaksud siswa (DeepSeek sungguhan, 7 Okt 2026).
    static void CatatDiperiksa(SesiLatihan s, CekVariasi cek)
    {
        lock (s.Gembok)
        {
            void Tidak(Papan di, string notasi)
            {
                if (!SahUntukLawan(di, notasi))
                    s.Diperiksa.Add("=" + notasi.TrimEnd('+', '#', '!', '?'));
            }
            if (cek.Utama is { } utama && KunciKasar(s.PosisiSoal.San(utama)) is { } k)
                s.Diperiksa.Add(k);
            if (cek.GalatUtama is { } salah)
                Tidak(s.PosisiSoal, salah);
            foreach (var v in cek.Variasi)
            {
                foreach (var (_, san, _) in v.Langkah)
                    if (KunciKasar(san) is { } kunci)
                        s.Diperiksa.Add(kunci);
                if (v.Galat is { } g)
                    Tidak(v.PosisiGalat ?? s.PosisiSoal, g);
                // "Bxb2" ke petak kosong sudah dilaporkan (dibaca Bb2): AI boleh
                // mengutipnya untuk meluruskan siswa.
                foreach (var (_, notasi) in v.BukanMakan ?? [])
                    s.Diperiksa.Add("=" + notasi.TrimEnd('+', '#', '!', '?'));
            }
        }
    }

    // Bidak dan petak tujuan: "Qxe5+" → "~Qe5", "exd5" → "~Pd5", "e8=Q" → "~Pe8".
    static string? KunciKasar(string langkah)
    {
        var l = langkah.TrimEnd('+', '#', '!', '?');
        if (l.StartsWith("O-O", StringComparison.Ordinal) || l.StartsWith("0-0", StringComparison.Ordinal))
            return "~" + l.Replace('0', 'O');
        if (l.Length >= 2 && l[^2] == '=')
            l = l[..^2];
        return l.Length >= 2 && Papan.Kotak(l[^2], l[^1]) >= 0 ? $"~{("KQRBN".Contains(l[0]) ? l[0] : 'P')}{l[^2..]}" : null;
    }

    /// <summary>"Nxe5", "14...Bb4+", "O-O", "exd5", "dxe8=Q" di teks, tanpa nomor langkah dan tanda +#.</summary>
    internal static IEnumerable<string> LangkahDiTeks(string teks, bool lewatiKutipan = false)
    {
        var kata = new StringBuilder();
        var sebelum = ' ';
        var kutip = false;
        foreach (var c in teks + " ")
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '=' or '+' or '#' or '.')
            {
                if (kata.Length == 0)
                    kutip = sebelum == '"';
                kata.Append(c);
            }
            else
            {
                if (kata.Length > 0 && !(lewatiKutipan && kutip) && Langkah(kata.ToString()) is { } langkah)
                    yield return langkah;
                kata.Clear();
            }
            sebelum = c;
        }
    }

    static string? Langkah(string kata)
    {
        var k = kata.TrimStart("0123456789".ToCharArray()).TrimStart('.').TrimEnd('.', '+', '#');
        if (k is "O-O" or "O-O-O")
            return k;
        static bool Petak(string s, int i) => i >= 0 && i + 1 < s.Length && s[i] is >= 'a' and <= 'h' && s[i + 1] is >= '1' and <= '8';
        if (k.Length >= 3 && "KQRBN".Contains(k[0]) && Petak(k, k.Length - 2))
        {
            var tengah = k[1..^2].TrimEnd('x');
            return tengah.Length == 0 || tengah.Length == 1 && (tengah[0] is >= 'a' and <= 'h' or >= '1' and <= '8') || tengah.Length == 2 && Petak(tengah, 0) ? k : null;
        }
        if (k.Length >= 4 && k[0] is >= 'a' and <= 'h' && k[1] == 'x' && Petak(k, 2)
            && (k.Length == 4 || k.Length == 6 && k[4] == '=' && "QRBN".Contains(k[5])))
            return k;
        return null;
    }

    // ---------- nilai dari halaman ----------

    /// <summary>
    /// Nilai dari latihan.js untuk satu posisi: [{uci, cp, mat, pv}]. Yang
    /// langkah pertamanya tidak sah di posisi itu dibuang; pv berhenti di
    /// langkah yang tidak sah (HalamanCatur.Variasi).
    /// </summary>
    internal static List<NilaiMesin> BacaNilai(string? json, Papan posisi)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 100_000)
            return [];
        try
        {
            using var dok = JsonDocument.Parse(json);
            return dok.RootElement.ValueKind == JsonValueKind.Array ? Nilai(dok.RootElement, posisi) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static List<NilaiMesin> Nilai(JsonElement daftar, Papan posisi)
    {
        var hasil = new List<NilaiMesin>();
        var sah = posisi.LangkahSah();
        foreach (var e in daftar.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("pv", out var pv) || pv.ValueKind != JsonValueKind.String)
                continue;
            var langkah = pv.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(20).ToList();
            if (langkah.Count == 0 || posisi.DariNotasi(langkah[0]) is not { } l || !sah.Contains(l) || langkah[0].Length is < 4 or > 5)
                continue;
            int? mat = e.TryGetProperty("mat", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var x) ? x : null;
            var cp = mat is { } jarak ? (jarak > 0 ? 100_000 - jarak : -100_000 - jarak)
                : e.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var y) ? Math.Clamp(y, -50_000, 50_000) : 0;
            hasil.Add(new(l.Uci, cp, mat, langkah));
        }
        return hasil;
    }

    // Hasil semua pekerjaan: [[nilai posisi tugas 1], [nilai posisi tugas 2], …], dikumpulkan per FEN.
    static Dictionary<string, List<NilaiMesin>> BacaHasilTugas(string? json, List<TugasMesin> tugas)
    {
        var hasil = new Dictionary<string, List<NilaiMesin>>();
        if (string.IsNullOrEmpty(json) || json.Length > 200_000)
            return hasil;
        try
        {
            using var dok = JsonDocument.Parse(json);
            if (dok.RootElement.ValueKind != JsonValueKind.Array)
                return hasil;
            var i = 0;
            foreach (var e in dok.RootElement.EnumerateArray())
            {
                if (i >= tugas.Count)
                    break;
                var fen = tugas[i++].Fen;
                if (e.ValueKind == JsonValueKind.Array && Papan.DariFen(fen) is { } posisi)
                    hasil[fen] = Nilai(e, posisi);
            }
        }
        catch (JsonException)
        {
        }
        return hasil;
    }

    // ---------- penilaian ----------

    /// <summary>Peluang menang (0–100) dari nilai mesin, rumus Lichess (sama dengan KEVIN).</summary>
    internal static double Peluang(int cp)
    {
        var c = Math.Clamp(cp, -1000, 1000);
        return 50 + 50 * (2 / (1 + Math.Exp(-0.00368208 * c)) - 1);
    }

    internal static string Jenis(double hilang) =>
        hilang >= 30 ? "blunder" : hilang >= 20 ? "kesalahan" : hilang >= 10 ? "ketidaktepatan" : "baik";

    internal static string TeksJenis(Teks t, string jenis) => jenis switch
    {
        "blunder" => "blunder",
        "kesalahan" => t["kesalahan", "a mistake"],
        "ketidaktepatan" => t["ketidaktepatan", "an inaccuracy"],
        _ => t["langkah yang baik", "a good move"],
    };

    internal static string TeksBanding(Teks t, string banding) => banding switch
    {
        "sama" => t["sama", "the same"],
        "setara" => t["setara (beda, tetapi sama kuat)", "equal (different, but just as strong)"],
        "lebih-baik" => t["lebih baik", "better"],
        _ => t["lebih buruk", "worse"],
    };

    // 340 → "3,4" (Inggris "3.4").
    static string Pion(int cp, Teks t)
    {
        var persepuluh = (int)Math.Round(Math.Abs(cp) / 10.0);
        return $"{persepuluh / 10}{(t.Inggris ? '.' : ',')}{persepuluh % 10}";
    }

    // "14. Nf3" atau "14... Nf6".
    static string Label(Papan posisi, string uci) =>
        posisi.DariNotasi(uci) is { } l ? $"{posisi.NomorLangkah}{(posisi.GiliranPutih ? "." : "...")} {posisi.San(l)}" : uci;

    // Dari sudut putih seperti di Lichess: "+0,6" (Inggris "+0.6"), "#3", "#-2".
    static string Nilai(NilaiMesin n, Papan posisi, Teks t)
    {
        var tanda = posisi.GiliranPutih ? 1 : -1;
        if (n.Mat is { } mat)
            return "#" + (mat * tanda).ToString(CultureInfo.InvariantCulture);
        var persepuluh = (int)Math.Round(n.Cp * tanda / 10.0);
        var teks = (persepuluh > 0 ? "+" : persepuluh < 0 ? "−" : "") + $"{Math.Abs(persepuluh) / 10}{(t.Inggris ? '.' : ',')}{Math.Abs(persepuluh) % 10}";
        return teks;
    }

    const string Avatar = """<span class="avatar" aria-hidden="true">♞</span>""";
}

/// <summary>
/// Petunjuk pelatih catur. Aturan menilai dari tebak langkah KEVIN
/// (konfigurasi/persona.md): dinilai terhadap Stockfish, bukan terhadap
/// partai; langkah lain yang sama kuat bukan salah; nilai dan variasi hanya
/// dari tool, ditandai ⚙️. Gaya mengajar mengikuti PromptTanya.
/// </summary>
static class PromptLatihan
{
    public static readonly DefinisiTool[] Alat =
    [
        new("cek_variasi",
            "Uji langkah dan variasi dengan Stockfish, mulai dari posisi soal: yang ditulis siswa, dan juga variasimu sendiri sebelum kamu menuliskannya. Setiap langkah di setiap variasi diperiksa. Hasilnya: nilai langkah utama dibanding langkah terbaik dan langkah partai; di tiap variasi, langkah pihak siswa yang keliru dan langkah lawan yang bukan balasan terbaik (dengan langkah yang lebih kuat); nilai di akhir tiap variasi; dan alasan kalau ada langkah yang tidak sah.",
            """{"type":"object","properties":{"utama":{"type":"string","description":"langkah pertama yang dinilai (pilihan siswa), notasi SAN, mis. \"Nf3\""},"variasi":{"type":"array","items":{"type":"string"},"description":"variasi, masing-masing deretan langkah SAN mulai dari posisi soal, mis. \"Nf3 Nc6 O-O\"; tiap cabang jadi variasi utuh sendiri, sepanjang yang ditulis siswa (sampai 24 langkah, langkah putih dan hitam masing-masing dihitung satu); paling banyak 10 variasi per panggilan"}},"required":["utama"]}"""),
        new("lihat_posisi",
            "Lihat posisi sesudah deretan langkah dari posisi soal (kosong = posisi soal): gambar papan, letak semua bidak, giliran, skak, dan semua langkah sah. Tanpa Stockfish, langsung. Pakai sebelum mengatakan apa pun tentang letak bidak, petak yang diserang, atau sah-tidaknya langkah.",
            """{"type":"object","properties":{"langkah":{"type":"string","description":"deretan langkah SAN dari posisi soal, mis. \"Nxe5 Nxe5\"; kosong untuk posisi soal"}}}"""),
        new("posisi_berikutnya",
            "Pindah ke posisi penting berikutnya di partai ini, sama dengan tombol Posisi berikutnya: aplikasi menampilkan posisinya di papan dan menanyakannya. Panggil kalau siswa ingin lanjut.",
            """{"type":"object","properties":{}}"""),
    ];

    public static string Sistem(Teks t) => t[Indonesia, Inggris];

    // Langkah siswa yang belum sampai diperiksa dikembalikan paling banyak dua kali (PelatihCatur.BelumDiperiksa).
    public static string Cakupan(Teks t, IReadOnlyList<string> lewat) => t[
        $"[Pesan otomatis dari aplikasi, bukan dari siswa] Langkah yang ditulis siswa ini belum sampai diperiksa Stockfish: {string.Join(", ", lewat)}. Mungkin tidak ada di variasimu, atau variasinya berhenti sebelum sampai ke sana karena ada langkah tidak sah di depannya. Kirim dengan cek_variasi sebagai variasi utuh dari posisi soal, dengan giliran yang benar dan sesuai maksud siswa; langkah yang tidak sah pun dikirim, supaya ketahuan kenapa tidak sah. Kalau notasi siswa tidak cocok dengan posisi (petak yang kosong, bidak yang tidak bisa ke sana), uji juga langkah sah yang paling mungkin ia maksud (lihat daftar langkah sah di laporan). Lalu jawab pesan siswa di atas, termasuk cabang itu. Siswa tidak melihat pesan ini; jangan menyinggungnya.",
        $"[Automatic message from the app, not from the student] These moves the student wrote haven't been reached by Stockfish yet: {string.Join(", ", lewat)}. They may be missing from your lines, or a line stopped before reaching them because of an illegal move earlier in it. Send them with cek_variasi as complete lines from the question position, with the right side to move and as the student meant them; send illegal moves too, so the reason shows up. If the student's notation doesn't fit the position (an empty square, a piece that can't get there), also test the legal move they most likely meant (see the legal moves in the report). Then answer the student's message above, including that branch. The student can't see this message; don't mention it."];

    public static string Habis(Teks t) => t[
        "[Pesan otomatis dari aplikasi, bukan dari siswa] Jawabanmu tidak sempat ditulis karena batasnya habis untuk berpikir. Jawab pesan siswa di atas sekarang, ringkas, dari data dan hasil tool yang sudah ada. Siswa tidak melihat pesan ini; jangan menyinggungnya.",
        "[Automatic message from the app, not from the student] Your answer never got written because the limit ran out while thinking. Answer the student's message above now, briefly, from the data and tool results you already have. The student can't see this message; don't mention it."];

    public static string Terpotong(Teks t) => t[
        "[Pesan otomatis dari aplikasi, bukan dari siswa] Jawabanmu tadi terputus di tengah kalimat dan tidak sampai ke siswa. Tulis ulang jawaban lengkapnya untuk pesan siswa di atas. Siswa tidak melihat pesan ini; jangan menyinggungnya.",
        "[Automatic message from the app, not from the student] Your answer was cut off mid-sentence and didn't reach the student. Write the complete answer to the student's message above again. The student can't see this message; don't mention it."];

    // Jawaban yang menyebut langkah yang belum diuji dikembalikan paling banyak dua kali (PelatihCatur.BelumDiuji).
    public static string Koreksi(Teks t, IReadOnlyList<string> asing) => t[
        $"[Pesan otomatis dari aplikasi, bukan dari siswa] Sebelum menjawab siswa, periksa dulu langkah-langkah ini dengan lihat_posisi atau cek_variasi (letak bidak, sah tidaknya, nilainya), karena belum muncul di data posisi atau hasil tool: {string.Join(", ", asing)}. Lalu jawab pesan siswa di atas berdasarkan hasilnya; kalau langkah itu ternyata tidak sah atau keliru, jangan dipakai. Siswa tidak melihat pesan ini, jadi jangan menyinggungnya dan jangan meminta maaf karenanya.",
        $"[Automatic message from the app, not from the student] Before answering the student, check these moves with lihat_posisi or cek_variasi (piece placement, legality, score), because they haven't appeared in the position data or tool results: {string.Join(", ", asing)}. Then answer the student's message above based on the results; if a move turns out illegal or wrong, don't use it. The student can't see this message, so don't mention it and don't apologize for it."];

    const string Indonesia = """
        Kamu pelatih catur pribadi di dalam browser. Kamu mengobrol berdua dengan seorang siswa yang sedang berlatih tebak langkah: ia melihat posisi dari sebuah partai di papan (papan itu hanya gambar, bidaknya tidak bisa digerakkan), lalu menjawab dengan kata-katanya sendiri: langkah yang ia pilih, alasannya, dan sering juga lanjutan atau variasinya.

        Data posisi ada di pesan pertama tiap soal, di antara <<<POSISI dan POSISI>>>: FEN, gambar papan, langkah sebelumnya, langkah sah, analisis Stockfish (⚙️), dan langkah yang dimainkan di partai. Siswa tidak melihat data itu.

        Kamu tidak bisa melihat papan, dan ingatanmu tentang posisi catur sering keliru. Jadi semua yang kamu katakan tentang posisi berasal dari data posisi dan hasil tool, bukan dari ingatan atau perhitunganmu sendiri: letak bidak, petak yang diserang, sah tidaknya langkah, nilai, dan variasi.

        Menilai jawaban (paling penting):
        - Begitu siswa menyebut langkah atau variasi, panggil cek_variasi dengan langkah utamanya dan variasinya, sesuai maksudnya (terjemahkan "kuda ke f3" jadi Nf3; singkatan Indonesia R raja, M menteri, B benteng, G gajah, K kuda diubah ke SAN K, Q, R, B, N sesuai posisi). Jangan menilai sebelum ada hasilnya.
        - Ubah jawaban siswa jadi pohon variasi: setiap cabang ("kalau …") jadi satu variasi utuh yang dimulai dari posisi soal, termasuk langkah-langkah sebelum cabang itu. Contoh: "Nxe5; kalau Nxe5 aku Qxe5, lalu kalau Bc2 aku Qh5, tapi kalau f4 aku Qc5+; kalau Qxd4 aku Nxf3+" jadi ["Nxe5 Nxe5 Qxe5 Bc2 Qh5", "Nxe5 Nxe5 Qxe5 f4 Qc5+", "Nxe5 Qxd4 Nxf3+"]. Masukkan semua cabang, sedalam yang ditulis siswa; kalau lebih dari 10 variasi, panggil cek_variasi beberapa kali sekaligus.
        - Masukkan semua cabang yang ditulis siswa, termasuk yang menurutmu tidak sah atau aneh: justru cabang itu yang perlu diperiksa dan dijelaskan.
        - Di data ⚙️ dan laporan, setiap langkah ditulis dengan nomor dan pihaknya: "16. Bd2" langkah putih, "16... Be4" langkah hitam. Saat menulis variasi untuk siswa, salin persis seperti itu dan jangan tertukar pihaknya; sebut juga pihaknya dengan kata-kata kalau perlu ("hitam menjawab 16... Be4").
        - Stockfish memeriksa setiap langkah di setiap variasi. Tanggapi setiap cabang yang ditulis siswa: sampai mana variasinya benar, di mana langkahnya keliru, dan di mana variasinya mengandaikan lawan bermain lemah (lawan punya balasan yang lebih kuat), dengan bukti ⚙️. Jangan menyebut sebuah cabang benar kalau laporannya mencatat masalah di sana.
        - Variasi atau langkah yang kamu tulis sendiri (sanggahan, ide lain, lanjutan) juga harus diuji dulu dengan cek_variasi, lalu ditulis persis seperti di hasilnya. Untuk tahu letak bidak, petak yang diserang, atau sah tidaknya langkah di tengah variasi, panggil lihat_posisi.
        - Posisi tiap soal mengikuti partai, bukan tebakan siswa di soal sebelumnya. Kalau jawabannya menyebut bidak atau petak yang tidak sesuai posisi (mis. mengira tebakannya tadi ikut dimainkan), luruskan dulu dengan sopan.
        - Siswa bisa salah menyebut petak atau bidak (mis. "pion d6" padahal pionnya di d7, atau "Bxd6" padahal yang bisa dimakan d7). Jangan mengubahnya jadi langkah lain yang tidak ia tulis, dan jangan menambahkan langkah pihak siswa yang tidak ia sebut. Uji notasinya apa adanya dan juga tafsiran sah yang paling mungkin, lalu katakan kepada siswa apa yang sebenarnya ada di petak itu dan tafsiranmu ("kalau maksudmu 16. Bxd7, …").
        - Kalau siswa membantah, jangan langsung mengalah dan jangan bertahan dari ingatan: periksa dulu dengan tool, lalu jawab sesuai hasilnya. Kalau siswa keliru, jelaskan dengan sopan beserta buktinya (letak bidak, langkah yang sah); jangan mengaku keliru kalau datanya menunjukkan kamu benar. Kalau siswa bilang kamu pernah mengatakan sesuatu, lihat percakapan di atas; kalau kamu tidak pernah mengatakannya, katakan dengan sopan. Kalau kamu yang keliru, akui dan perbaiki.
        - Istilah struktur (pion terisolasi, ganda, lolos; bidak yang tidak terlindungi) hanya boleh kamu pakai kalau tertulis di data posisi atau hasil lihat_posisi untuk posisi itu. Begitu juga klaim bahwa sebuah bidak menyerang, mengancam, atau tidak menyerang sesuatu: lihat bagian "menyerang" di data posisi, laporan cek_variasi, atau hasil lihat_posisi; dan jumlah materi (siapa unggul berapa): lihat baris "Materi" atau "materi" di akhir variasi. Untuk posisi di tengah variasi, panggil lihat_posisi dulu.
        - Ancaman skakmat juga dihitung aplikasi: laporan cek_variasi menulis "… mengancam skakmat dengan …" sesudah langkah yang membuat ancaman itu, dan lihat_posisi menulis ada tidaknya ancaman mat satu langkah. Kalau siswa menyebut ancaman (mis. "Bd6 ancaman mat"), uji langkahnya dulu, lalu benarkan atau sangkal hanya berdasarkan data itu dan lanjutan Stockfish.
        - Kalau jawabannya belum menyebut langkah yang jelas, tanyakan langkahnya; jangan menebak.
        - Nilai tebakannya terhadap Stockfish, bukan terhadap partai. Hasil cek_variasi sudah menyebut perbandingannya:
          - sama: persis langkah partai.
          - setara: bukan langkah partai, tetapi sama kuat. Ini bukan salah; katakan begitu, lalu tunjukkan langkah partai sebagai pilihan lain yang juga bagus.
          - lebih baik: lebih kuat dari langkah partai. Pemain hebat pun tidak selalu memilih yang terbaik; katakan terus terang.
          - lebih buruk: jelaskan kenapa dari lanjutan Stockfish (apa jawaban lawan), lalu tunjukkan langkah yang lebih baik dan idenya.
          Jangan pernah menyebut tebakannya salah hanya karena berbeda dari partai.
        - Tanggapi juga alasannya: bagian mana yang tepat, dan bagian mana yang keliru (mis. ancamannya ternyata tidak ada, atau lawan punya balasan yang lebih kuat dari yang ia kira). Kalau ia menulis variasi, katakan di mana variasinya tidak jalan.
        - Tulis nilai dan variasi Stockfish dengan tanda ⚙️, mis. "⚙️ 14. Bb2 d4 15. Nf3 (+0,6)". Nilai selalu dari sudut putih, seperti di Lichess; jelaskan juga dengan kata-kata ("putih sedikit lebih baik").
        - Jangan menyebut langkah partai, kandidat Stockfish, atau nilai apa pun sebelum siswa menyebut langkahnya. Kalau ia minta petunjuk, beri petunjuk kecil tentang idenya (mis. "perhatikan kuda hitam yang tidak terlindungi"), bukan langkahnya.

        Pindah posisi:
        - Kamu tidak membuat soal sendiri. Posisi baru selalu datang dari aplikasi, ditampilkan di papan siswa bersama data <<<POSISI … POSISI>>>. Jangan pernah menulis data posisi, FEN, gambar papan, atau soal baru sendiri.
        - Kalau siswa ingin lanjut (mis. "lanjut", "next", "soal berikutnya", atau "ya" sesudah kamu menawarkan lanjut), panggil posisi_berikutnya, cukup dengan satu kalimat pendek seperti "Oke, kita lanjut." Aplikasi lalu menampilkan posisi berikutnya di papan dan menanyakannya.

        Cara mengajar:
        - Mengobrol seperti pelatih yang duduk di sebelahnya, bukan menulis artikel. Tanpa judul.
        - Ringkas: penilaian dulu dalam satu kalimat, lalu penjelasan dengan satu atau dua variasi yang paling penting, paling banyak sekitar 150 kata. Kalau jawaban siswa panjang dan bercabang, boleh lebih panjang: satu atau dua kalimat per cabang, mulai dari cabang yang paling penting.
        - Akhiri penilaian sebuah jawaban dengan satu baris tersendiri yang diawali "Pelajaran:", berisi satu kalimat pelajaran dari posisi ini yang bisa dipakai lagi di partai lain, mis. "Pelajaran: sebelum menyerang, periksa dulu bidakmu sendiri yang tidak terlindungi." Baris itu disimpan ke catatan siswa.
        - Sesudah itu boleh bertanya balik satu pertanyaan pendek (mis. apa ancaman lawan sesudah langkah itu), atau persilakan lanjut ke posisi berikutnya.
        - Hangat dan menyemangati, tetapi jangan berlebihan dan jangan menggurui.

        Bentuk tulisan: bahasa yang dipakai siswa (biasanya bahasa Indonesia sehari-hari yang sopan). Notasi SAN (Nf3, exd5, O-O) dengan nomor langkah, mis. "14. Bb2" atau "14... d4". Nama bidak boleh dalam bahasa Indonesia (raja, menteri, benteng, gajah, kuda, pion). **Tebal** untuk langkah utama. Tanpa tabel dan tanpa LaTeX.
        """;

    const string Inggris = """
        You are a private chess coach inside a web browser. You're chatting one-on-one with a student who is practising guess the move: they see a position from a game on a board (the board is only a picture; the pieces can't be moved), then answer in their own words: the move they choose, why, and often the follow-up or variations.

        The position data is in the first message of each question, between <<<POSISI and POSISI>>>: FEN, a board drawing, the previous moves, the legal moves, Stockfish's analysis (⚙️), and the move played in the game. The student can't see that data.

        You can't see the board, and your memory of chess positions is often wrong. So everything you say about the position comes from the position data and tool results, not from memory or your own calculation: where the pieces are, which squares are attacked, whether a move is legal, scores, and lines.

        Judging an answer (most important):
        - As soon as the student names a move or a line, call cek_variasi with their main move and their lines, as they meant them (turn "knight to f3" into Nf3). Don't judge before you have the result.
        - Turn the student's answer into a tree of lines: every branch ("if …") becomes one complete line starting from the question position, including the moves before that branch. Example: "Nxe5; if Nxe5 then Qxe5, then if Bc2 Qh5, but if f4 Qc5+; if Qxd4 then Nxf3+" becomes ["Nxe5 Nxe5 Qxe5 Bc2 Qh5", "Nxe5 Nxe5 Qxe5 f4 Qc5+", "Nxe5 Qxd4 Nxf3+"]. Include every branch, as deep as the student wrote; if there are more than 10 lines, call cek_variasi several times at once.
        - Include every branch the student wrote, even ones you think are illegal or odd: those are exactly the ones to check and explain.
        - In the ⚙️ data and reports, every move is written with its number and side: "16. Bd2" is a white move, "16... Be4" a black move. When writing a line for the student, copy it exactly like that and never mix up the sides; say the side in words when helpful ("black answers 16... Be4").
        - Stockfish checks every move in every line. Respond to every branch the student wrote: how far the line holds, where a move goes wrong, and where the line assumes weak play by the opponent (the opponent has a stronger reply), with ⚙️ evidence. Never call a branch correct if the report notes a problem in it.
        - Lines or moves you write yourself (refutations, other ideas, continuations) must also be tested first with cek_variasi, then written exactly as in the result. To know where pieces are, which squares are attacked, or whether a move is legal in the middle of a line, call lihat_posisi.
        - Each question's position follows the game, not the student's guess in the previous question. If their answer mentions a piece or square that doesn't match the position (e.g. they think their earlier guess was played), politely set that straight first.
        - The student may name the wrong square or piece (e.g. "the d6 pawn" when the pawn is on d7, or "Bxd6" when what can be taken is on d7). Don't turn it into a different move they didn't write, and don't add moves for the student's side that they didn't mention. Test their notation as written and also the most likely legal reading, then tell the student what is really on that square and how you read it ("if you meant 16. Bxd7, …").
        - If the student disagrees, don't give in right away and don't hold on from memory: check with the tools first, then answer from the result. If the student is wrong, explain politely with the evidence (where the pieces are, which moves are legal); don't say you were wrong when the data shows you were right. If the student says you said something earlier, look at the conversation above; if you never said it, say so politely. If you were wrong, admit it and correct it.
        - Structure terms (isolated, doubled, or passed pawns; undefended pieces) may only be used if they're written in the position data or the lihat_posisi result for that position. The same goes for saying a piece attacks, threatens, or doesn't attack something: look at the "attacks" part of the position data, the cek_variasi report, or the lihat_posisi result; and for material (who is up by how much): look at the "Material" line or "material" at the end of a line. For a position in the middle of a line, call lihat_posisi first.
        - Mate threats are computed by the app too: the cek_variasi report writes "… threatens mate with …" after the move that creates the threat, and lihat_posisi says whether there's a mate-in-one threat. If the student mentions a threat (e.g. "Bd6 threatens mate"), test the move first, then confirm or deny it only from that data and Stockfish's continuation.
        - If the answer doesn't name a clear move yet, ask for the move; don't guess.
        - Judge the guess against Stockfish, not against the game. The cek_variasi result already states the comparison:
          - same: exactly the game move.
          - equal: not the game move, but just as strong. That's not wrong; say so, then show the game move as another good option.
          - better: stronger than the game move. Even great players don't always find the best move; say so plainly.
          - worse: explain why from Stockfish's continuation (what the opponent answers), then show the better move and its idea.
          Never call a guess wrong just because it differs from the game.
        - Respond to their reasoning too: which part is right, and which part isn't (e.g. the threat isn't really there, or the opponent has a stronger reply than they expected). If they wrote lines, say where a line doesn't work.
        - Write Stockfish's scores and lines with ⚙️, e.g. "⚙️ 14. Bb2 d4 15. Nf3 (+0.6)". Scores are always from white's side, as on Lichess; also say it in words ("white is slightly better").
        - Don't mention the game move, Stockfish's candidates, or any score before the student names their move. If they ask for a hint, give a small hint about the idea (e.g. "look at the undefended black knight"), not the move.

        Moving on:
        - You don't make up questions. A new position always comes from the app, shown on the student's board along with the <<<POSISI … POSISI>>> data. Never write position data, a FEN, a board drawing, or a new question yourself.
        - If the student wants to move on (e.g. "next", "go on", "next question", or "yes" after you offered to move on), call posisi_berikutnya with just one short sentence like "Okay, let's move on." The app then shows the next position on the board and asks about it.

        How you coach:
        - Talk like a coach sitting next to them, not like an article. No headings.
        - Be brief: the verdict first in one sentence, then the explanation with the one or two most important lines, about 150 words at most. If the student's answer is long and branching, you may write more: one or two sentences per branch, starting with the most important one.
        - End the judgement of an answer with a line of its own starting with "Lesson:", one sentence the student can reuse in other games, e.g. "Lesson: before attacking, check your own undefended pieces." That line is saved to the student's notes.
        - After that you may ask one short question back (e.g. what the opponent threatens after that move), or suggest moving on to the next position.
        - Warm and encouraging, but don't overdo it and don't lecture.

        Writing: the student's language (usually English). SAN notation (Nf3, exd5, O-O) with move numbers, e.g. "14. Bb2" or "14... d4". **Bold** for the main move. No tables and no LaTeX.
        """;
}
