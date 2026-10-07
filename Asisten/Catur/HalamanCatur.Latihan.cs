using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

// kevin://catur?latihan=ID[&sisi=putih|hitam][&sesi=S]: tebak langkah sebagai
// obrolan dengan pelatih AI (PelatihCatur). Papan hanya gambar posisi soal.
// latihan.js mencari posisi penting dengan Stockfish (bukan langkah
// pembukaan yang sudah hafal, kecuali ada kesalahan), menanyakannya, dan
// mengerjakan analisis yang diminta pelatih. Hasilnya ditambahkan ke catatan
// partai itu, di bagian Pelajaran. ?mesin memasang dan melepas Stockfish.
public sealed partial class HalamanCatur
{
    // Alasan langkah partai dilewati tanpa analisis (lihat AlasanLewat).
    internal const string LewatSatu = "satu", LewatSkak = "skak", LewatAmbil = "ambil";

    /// <summary>Satu langkah sisi yang dilatih: posisinya, langkah sahnya, dan alasan dilewati (null = mungkin ditanyakan).</summary>
    internal sealed record SoalLatihan(int Ply, string? Lewat, Papan Posisi, List<Langkah> Sah);

    /// <summary>Langkah sisi itu mulai dari langkah nomor <paramref name="mulai"/>, masing-masing dengan alasan dilewati kalau ada.</summary>
    internal static List<SoalLatihan> SusunSoal(UraianPartai u, bool putih, int mulai)
    {
        var hasil = new List<SoalLatihan>();
        var papan = Papan.DariFen(u.FenAwal)!;
        for (var i = 0; i < u.Langkah.Count; i++)
        {
            var l = u.Langkah[i];
            if (papan.GiliranPutih == putih && papan.NomorLangkah >= mulai)
            {
                var sah = papan.LangkahSah();
                hasil.Add(new SoalLatihan(i, AlasanLewat(papan, sah, l, i > 0 ? u.Langkah[i - 1] : null), papan, sah));
            }
            papan = papan.Jalankan(l.Langkah);
        }
        return hasil;
    }

    // Langkah tanpa pilihan nyata tidak pernah ditanyakan (tebak langkah
    // KEVIN kadang menanyakannya): satu-satunya langkah sah, keluar dari skak
    // dengan paling banyak dua pilihan, dan mengambil kembali buah yang baru
    // dimakan kalau hanya satu buah yang bisa melakukannya. Kalau dua buah
    // bisa mengambil kembali (gxf6 atau Qxf6), pilihannya penting. Langkah
    // yang ditandai ?, ??, atau ?! oleh analisis Lichess tidak dilewati.
    internal static string? AlasanLewat(Papan papan, List<Langkah> sah, LangkahPartai l, LangkahPartai? sebelumnya)
    {
        if (l.Tanda is "?" or "??" or "?!")
            return null;
        if (sah.Count == 1)
            return LewatSatu;
        if (papan.Skak && sah.Count <= 2)
            return LewatSkak;
        var ke = l.Langkah.Ke;
        if (sebelumnya is not null && sebelumnya.San.Contains('x') && sebelumnya.Langkah.Ke == ke
            && sah.Where(s => s.Ke == ke).Select(s => s.Dari).Distinct().Count() == 1)
            return LewatAmbil;
        return null;
    }

    // Nomor langkah ply ke-i (0 = langkah pertama partai).
    static int NomorPly(Papan awal, int i) => awal.NomorLangkah + (i + (awal.GiliranPutih ? 0 : 1)) / 2;

    static string AlamatLatihan(string id, bool putih, string? sesi = null) =>
        $"{Alamat}?latihan={Uri.EscapeDataString(id)}&sisi={(putih ? "putih" : "hitam")}" + (sesi is null ? "" : $"&sesi={sesi}");

    (string, string) Latihan(Teks t, Kueri kueri, string id, bool post)
    {
        if (koleksi.Cari(id) is not { } p)
            return TidakAda(t);
        var putih = kueri["sisi"] switch { "putih" => true, "hitam" => false, _ => SisiSaya(p) ?? p.Hasil != "0-1" };
        var judul = t["Tebak langkah", "Guess the move"];
        var sesi = pelatih?.Ambil(kueri["sesi"]) is { } ada && ada.Partai.Id == p.Id && ada.Putih == putih ? ada : null;

        string? pesan = null;
        if (post && kueri["aksi"] == "simpan")
        {
            if (sesi is null || sesi.Hasil.Count == 0)
                pesan = t["Belum ada jawaban yang dinilai untuk disimpan.", "There are no judged answers to save yet."];
            else if (!TokenSekali.Pakai(kueri["token"]))
                pesan = t["Permintaan ini sudah kedaluwarsa. Obrolanmu masih ada: tekan Simpan ke catatan sekali lagi.",
                    "This request has expired. Your chat is still here: press Save as a note once more."];
            else if (SimpanLatihan(t, p, sesi) is { } catatan)
                return HalamanBelajar.Pindah(t, HalamanBelajar.Alamat(catatan));
            else
                pesan = t["Catatannya tidak bisa disimpan.", "The note couldn't be saved."];
        }

        // Halaman ini untuk berpikir: judul dan keterangan partai cukup satu
        // baris kecil, pilihan dan tombol kecil di atas papan, kolom obrolan
        // hanya obrolan dan kotak tulis. Model dan catatan privasi di bawah,
        // di luar layar pertama. Lebar halaman: catur.css (main.catur:has(.tata-latihan)).
        var keterangan = string.Join(" · ", new[] { judul, $"{p.Putih} vs {p.Hitam}", p.Waktu is { } d ? t.TanggalSingkat(d) : null }
            .Where(x => x is not null).Select(x => HtmlEncode(x)));
        var isi = new StringBuilder($"""
            <p class="jejak jejak-latihan"><a href="{Alamat}">{t["Catur", "Chess"]}</a> › <a href="{HtmlEncode($"{Alamat}?partai={Uri.EscapeDataString(p.Id)}")}">{t["Partai", "Game"]}</a> › {keterangan}</p>
            {HalamanPengaturan.Pesan(pesan)}

            """);

        if (!mesin.Terpasang)
            return (judul, isi.Append(KartuMesin(t, AlamatLatihan(p.Id, putih))).Append(Tautan()).ToString());
        if (pelatih?.Pengaturan.Kunci is null)
            return (judul, isi.Append(KartuKunci(t)).Append(Tautan()).ToString());

        sesi ??= pelatih.Baru(p, putih);
        var alamat = AlamatLatihan(p.Id, putih, sesi.Id);
        isi.Append($"""
            <div class="tata-latihan">
            <div class="kolom-kiri">
              <div class="alat-latihan">
                {PilihanSisi(t, p.Id, putih)}
                <button class="tombol" type="button" id="berikutnya" disabled>{t["Posisi berikutnya", "Next position"]}{Ikon.Maju}</button>
                <form action="{HtmlEncode(alamat)}" method="post"><input type="hidden" name="aksi" value="simpan"><input type="hidden" name="token" value="{TokenSekali.Buat()}"><button class="tombol" type="submit" id="simpan"{(sesi.Hasil.Count == 0 ? " disabled" : "")}>{Ikon.Buku}{t["Simpan ke catatan", "Save as a note"]}</button></form>
              </div>
              <div class="kolom-papan{(putih ? "" : " terbalik")}">
                {Pemain(p, false)}
                <div class="papan-wadah"><div id="papan" class="papan" aria-label="{t["Papan catur (posisi soal)", "Chessboard (question position)"]}"></div></div>
                {Pemain(p, true)}
              </div>
            </div>
            <section class="kolom-obrolan" aria-label="{t["Obrolan dengan pelatih", "Chat with the coach"]}">
              <div class="obrolan" id="obrolan" aria-live="polite">
              {string.Join('\n', sesi.Transkrip)}
              </div>
              <form class="kirim" id="kirim">
                <div class="komposer">
                <textarea id="pesan" rows="1" disabled aria-label="{t["Jawabanmu", "Your answer"]}" placeholder="{t["mis. Nf3, supaya kuda menjaga e5", "e.g. Nf3, so the knight guards e5"]}"></textarea>
                <button class="tombol utama" type="submit" id="tombol-kirim" disabled>{Ikon.Kirim}{t["Kirim", "Send"]}</button>
                </div>
              </form>
            </section>
            </div>
            <p class="catatan kaki-latihan">{t["Pelatih:", "Coach:"]} {HtmlEncode(sesi.Model)} · <a href="{HalamanBawaan.Belajar}?ai">{t["ubah", "change"]}</a> · {t[$"dinilai {MesinCatur.Nama} di laptop ini", $"judged by {MesinCatur.Nama} on this laptop"]} · <a href="{Alamat}?mesin">{t["tentang mesinnya", "about the engine"]}</a><br>{t[
                "Jawabanmu dan posisi papan dikirim ke DeepSeek; obrolannya tidak disimpan, hasilnya bisa disimpan ke catatan.",
                "Your answers and the board position are sent to DeepSeek; the chat isn't kept, but the results can be saved as a note."]}</p>
            <script type="application/json" id="data-latihan">{DataLatihan(t, sesi, alamat)}</script>
            {Tautan()}
            <script src="kevin://papan.js"></script>
            <script src="kevin://latihan.js"></script>
            """);
        return (judul, isi.ToString());
    }

    static string PilihanSisi(Teks t, string id, bool putih)
    {
        string Pilihan(string label, bool w) =>
            $"""<a class="pilihan{(w == putih ? " aktif" : "")}" href="{HtmlEncode(AlamatLatihan(id, w))}"{(w == putih ? " aria-current=\"true\"" : "")}>{label}</a>""";
        return $"""<nav class="pilih-sisi" aria-label="{t["Sisi", "Side"]}">{Pilihan(t["Sebagai putih", "As white"], true)}{Pilihan(t["Sebagai hitam", "As black"], false)}</nav>""";
    }

    // Data untuk latihan.js: semua langkah partai (FEN sesudahnya, UCI, nomor,
    // alasan dilewati), keadaan sesi, dan teks antarmuka.
    static string DataLatihan(Teks t, SesiLatihan sesi, string alamat)
    {
        var u = sesi.Uraian;
        var awal = Papan.DariFen(u.FenAwal)!;
        var lewat = SusunSoal(u, sesi.Putih, 1).Where(s => s.Lewat is not null).ToDictionary(s => s.Ply, s => s.Lewat!);
        using var aliran = new MemoryStream();
        using (var json = new Utf8JsonWriter(aliran, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            json.WriteStartObject();
            json.WriteString("fen0", u.FenAwal);
            json.WriteBoolean("putih", sesi.Putih);
            json.WriteString("perantara", AwalMesin + "perantara.html");
            json.WriteString("alamat", alamat);
            json.WriteString("sesi", sesi.Id);
            json.WriteNumber("soal", sesi.Soal);
            json.WriteBoolean("perluAwal", sesi.PerluAwal);
            json.WriteStartArray("langkah");
            for (var i = 0; i < u.Langkah.Count; i++)
            {
                var l = u.Langkah[i];
                json.WriteStartObject();
                json.WriteString("fen", l.Fen);
                json.WriteString("uci", l.Langkah.Uci);
                json.WriteString("no", $"{NomorPly(awal, i)}{((i % 2 == 0) == awal.GiliranPutih ? "." : "...")}");
                if (lewat.TryGetValue(i, out var alasan))
                    json.WriteString("lewat", alasan);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            if (sesi.TugasTertunda is { } tugas)
                TulisTugas(json, tugas);
            json.WriteStartObject("teks");
            foreach (var (kunci, nilai) in TeksLatihan(t))
                json.WriteString(kunci, nilai);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(aliran.ToArray());
    }

    static void TulisTugas(Utf8JsonWriter json, IReadOnlyList<TugasMesin> tugas)
    {
        json.WriteStartArray("tugas");
        foreach (var x in tugas)
        {
            json.WriteStartObject();
            json.WriteString("fen", x.Fen);
            json.WriteNumber("n", x.Jumlah);
            json.WriteStartArray("cari");
            foreach (var c in x.Cari)
                json.WriteStringValue(c);
            json.WriteEndArray();
            json.WriteNumber("d", x.Kedalaman);
            json.WriteNumber("ms", x.Waktu);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    // Teks yang ditulis latihan.js; {0} diganti di sana.
    static (string, string)[] TeksLatihan(Teks t) =>
    [
        ("memuat", t["Memuat mesin catur…", "Loading the chess engine…"]),
        ("mencari", t["Mencari posisi penting… (langkah {0})", "Looking for an important position… (move {0})"]),
        ("membaca", t["Pelatih memikirkan jawabanmu… (bisa sampai satu-dua menit)", "The coach is thinking about your answer… (this can take a minute or two)"]),
        ("menguji", t["Stockfish menguji jawabanmu… {0}/{1}", "Stockfish is testing your answer… {0}/{1}"]),
        ("menganalisis", t["Stockfish masih menganalisis posisi ini…", "Stockfish is still analysing this position…"]),
        ("mesinGagal", t["Mesin catur tidak bisa dijalankan. Coba muat ulang halaman ini.", "The chess engine couldn't start. Try reloading this page."]),
        ("selesai", t["Tidak ada lagi posisi penting untuk sisimu di partai ini. Simpan hasilnya ke catatan, atau coba sebagai sisi lain.",
            "There are no more important positions for your side in this game. Save the results as a note, or try the other side."]),
        ("putus", t["Pelatih tidak bisa dihubungi. Coba kirim lagi.", "Couldn't reach the coach. Try sending again."]),
    ];

    static string KartuKunci(Teks t) => $"""
        <div class="tulis kartu-mesin">
          <p><strong>{t["Tebak langkah memakai pelatih AI dari Belajar.", "Guess the move uses the AI coach from Learn."]}</strong><br><span class="catatan">{t[
              "Pelatih membaca jawabanmu, meminta Stockfish mengujinya, lalu menjelaskan. Isi dulu kunci API DeepSeek; kunci yang sama dipakai Belajar.",
              "The coach reads your answer, has Stockfish test it, then explains. Set a DeepSeek API key first; Learn uses the same key."]}</span></p>
          <p class="tombol-tombol"><a class="tombol utama" href="{HalamanBawaan.Belajar}?ai">{Ikon.Kunci}{t["Atur kunci DeepSeek", "Set the DeepSeek key"]}</a></p>
        </div>

        """;

    // ---------- data untuk latihan.js ----------

    // POST dari latihan.js (fetch): soal baru, pesan siswa, hasil Stockfish.
    async Task<(byte[], string)?> DataLatihan(Kueri kueri, Teks t)
    {
        if (pelatih?.Ambil(kueri["sesi"]) is not { } sesi || sesi.Partai.Id != kueri["latihan"])
            return Json(json => json.WriteString("galat", t["Sesi latihan ini sudah tidak ada. Muat ulang halamannya.", "This practice session is gone. Reload the page."]));
        if (kueri["soal"] is not null)
            return int.TryParse(kueri["ply"], out var ply) && pelatih.Soal(sesi, ply, t) is { } html
                ? Json(json => json.WriteString("html", html))
                : Json(json => json.WriteString("galat", t["Soal itu tidak bisa ditanyakan.", "That question can't be asked."]));
        // Dengan mode berpikir satu panggilan bisa 1–2 menit (pohon 10 variasi:
        // ±5.000–12.000 token berpikir), dan penjaga bisa menambah putaran;
        // paling banyak PelatihCatur.MaksPutaranTool putaran per permintaan.
        using var batas = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var balasan = kueri["kirim"] is not null
            ? await pelatih.Kirim(sesi, kueri["pesan"], kueri["awal"], t, batas.Token)
            : await pelatih.Lanjut(sesi, kueri["hasil"], t, batas.Token);
        return Json(json =>
        {
            if (balasan.Siswa is { } siswa)
                json.WriteString("siswa", siswa);
            if (balasan.Tugas is { } tugas)
                TulisTugas(json, tugas);
            if (balasan.Ai is { } ai)
                json.WriteString("ai", ai);
            if (balasan.Galat is { } galat)
                json.WriteString("galat", galat);
            json.WriteNumber("dinilai", balasan.Dinilai);
            if (balasan.Berikutnya)
                json.WriteBoolean("berikutnya", true);
        });
    }

    static (byte[], string) Json(Action<Utf8JsonWriter> isi)
    {
        using var aliran = new MemoryStream();
        using (var json = new Utf8JsonWriter(aliran, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            json.WriteStartObject();
            isi(json);
            json.WriteEndObject();
        }
        return (aliran.ToArray(), "application/json");
    }

    // ---------- simpan ke catatan ----------

    // Hasil latihan masuk ke catatan partai ini (dibuat kalau belum ada).
    // Kalau berkasnya kebetulan diubah aplikasi lain di antara baca dan tulis,
    // dicoba sekali lagi dengan isi terbarunya.
    Catatan? SimpanLatihan(Teks t, Partai p, SesiLatihan sesi)
    {
        var bagian = BagianLatihan(t, p, sesi);
        for (var coba = 0; coba < 2; coba++)
        {
            if (CariCatatan(p) is not { } ada)
                return buku.Tulis(MapelCatur, JudulCatatan(t, p), TambahPelajaran(t, IsiCatatan(t, p), bagian), waktu.GetLocalNow().DateTime);
            var sidik = buku.Sidik(ada.Mapel, ada.Nama);
            if (buku.Baca(ada.Mapel, ada.Nama) is { } isi
                && buku.Simpan(ada.Mapel, ada.Nama, TambahPelajaran(t, isi, bagian), sidik) == HasilSimpan.Tersimpan)
                return ada;
        }
        return null;
    }

    // Menambahkan bagian di akhir catatan, di bawah judul Pelajaran (dibuat
    // kalau tidak ada). Kalimat pengisi Pelajaran dari IsiCatatan dibuang,
    // juga "(belum diisi)" dari versi sebelumnya.
    internal static string TambahPelajaran(Teks t, string isi, string bagian)
    {
        var baris = isi.Replace("\r\n", "\n").Split('\n')
            .Where(b => !b.StartsWith("(Tulis sendiri, atau latih partai ini", StringComparison.Ordinal)
                && !b.StartsWith("(Write your own, or practise this game", StringComparison.Ordinal)
                && b.Trim() is not ("(belum diisi)" or "(not filled in yet)"));
        var teks = string.Join('\n', baris).TrimEnd();
        if (!teks.Split('\n').Any(b => b.Trim() is "## Pelajaran" or "## Lessons"))
            teks += $"\n\n## {t["Pelajaran", "Lessons"]}";
        return $"{teks}\n\n{bagian.Trim()}\n";
    }

    // Satu bagian per latihan: ringkasan, lalu tiap posisi dengan tebakan,
    // penilaian, ⚙️ langkah terbaik, FEN, dan pelajaran dari pelatih, supaya
    // catatannya tetap berguna tanpa browser ini (seperti catatan catur KEVIN).
    string BagianLatihan(Teks t, Partai p, SesiLatihan sesi)
    {
        var u = sesi.Uraian;
        List<HasilSoal> hasil;
        lock (sesi.Gembok)
            hasil = [.. sesi.Hasil.OrderBy(h => h.Ply)];
        int Jumlah(string banding) => hasil.Count(h => h.Banding == banding);

        var sb = new StringBuilder($"### {t["Tebak langkah", "Guess the move"]}, {t.TanggalSingkat(waktu.GetLocalNow().DateTime)}\n\n");
        sb.Append(t[$"Sebagai {(sesi.Putih ? "putih" : "hitam")}, dinilai {MesinCatur.Nama}, dijelaskan {sesi.Model}: {hasil.Count} posisi penting; sama dengan partai {Jumlah("sama")}, setara {Jumlah("setara")}, lebih baik {Jumlah("lebih-baik")}, lebih buruk {Jumlah("lebih-buruk")}.",
            $"As {(sesi.Putih ? "white" : "black")}, judged by {MesinCatur.Nama}, explained by {sesi.Model}: {hasil.Count} important {(hasil.Count == 1 ? "position" : "positions")}; same as the game {Jumlah("sama")}, equal {Jumlah("setara")}, better {Jumlah("lebih-baik")}, worse {Jumlah("lebih-buruk")}."]).Append("\n\n");
        foreach (var h in hasil)
        {
            var posisi = Papan.DariFen(h.Ply == 0 ? u.FenAwal : u.Langkah[h.Ply - 1].Fen)!;
            if (posisi.DariNotasi(h.Tebakan) is not { } tebakan)
                continue;
            var partai = Label(posisi, u.Langkah[h.Ply].San);
            sb.Append($"- [{Label(posisi, posisi.San(tebakan))}]({Alamat}?partai={Uri.EscapeDataString(p.Id)}#{h.Ply}): ")
                .Append(h.Banding switch
                {
                    "sama" => t["sama dengan partai", "same as the game"],
                    "setara" => t[$"setara dengan partai ({partai})", $"equal to the game ({partai})"],
                    "lebih-baik" => t[$"lebih baik dari partai ({partai})", $"better than the game ({partai})"],
                    _ => t[$"lebih buruk dari partai ({partai})", $"worse than the game ({partai})"],
                })
                .Append(", ").Append(PelatihCatur.TeksJenis(t, h.Jenis))
                .Append(h.Hilang > 0 ? t[$", peluang menang −{h.Hilang}%", $", winning chance −{h.Hilang}%"] : "").Append('.');
            if (h.Banding != "sama" && Variasi(posisi, h.Terbaik, 6) is { Length: > 0 } terbaik)
                sb.Append($" ⚙️ {t["Terbaik", "Best"]}: {terbaik}.");
            sb.Append($" FEN `{posisi.Fen()}`");
            if (h.Pelajaran is { } pelajaran)
                sb.Append($" **{t["Pelajaran", "Lesson"]}:** {pelajaran.Replace('\n', ' ')}");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // "14. Nf3" atau "14... Nf6".
    static string Label(Papan posisi, string san) => $"{posisi.NomorLangkah}{(posisi.GiliranPutih ? "." : "...")} {san}";

    /// <summary>
    /// Deretan langkah UCI dari mesin sebagai notasi bernomor, mis.
    /// "14. Bd3 Nc6 15. O-O" atau "14... Nc6 15. O-O". Berhenti di langkah yang
    /// tidak sah. <paramref name="setiapNomor"/>: langkah hitam juga bernomor
    /// ("14. Bd3 14... Nc6"), untuk teks yang dibaca AI: dengan notasi biasa
    /// AI pernah menukar pihak langkah ("16. Bd2 Be4" dibacanya "17. Be4").
    /// </summary>
    internal static string Variasi(Papan posisi, string? uci, int maks, bool setiapNomor = false)
    {
        var sb = new StringBuilder();
        var papan = posisi;
        foreach (var bagian in (uci ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maks))
        {
            if (papan.DariNotasi(bagian) is not { } langkah || bagian.Length is < 4 or > 5)
                break;
            if (sb.Length > 0)
                sb.Append(' ');
            if (papan.GiliranPutih)
                sb.Append(papan.NomorLangkah).Append(". ");
            else if (sb.Length == 0 || setiapNomor)
                sb.Append(papan.NomorLangkah).Append("... ");
            sb.Append(papan.San(langkah));
            papan = papan.Jalankan(langkah);
        }
        return sb.ToString();
    }

    // ---------- ?mesin ----------

    async Task<(string, string)> PasangMesin(Teks t, Kueri kueri, bool post)
    {
        var judul = t["Mesin catur", "Chess engine"];
        var kembali = kueri["kembali"] is { } k && k.StartsWith(Alamat + "?", StringComparison.Ordinal) ? k : null;
        string? pesan = null;
        if (post)
        {
            if (!TokenSekali.Pakai(kueri["token"]))
                pesan = t["Permintaan ini sudah dipakai atau kedaluwarsa. Tekan tombolnya lagi.", "This request was already used or has expired. Press the button again."];
            else if (kueri["aksi"] == "lepas")
            {
                mesin.Hapus();
                pesan = t[$"{MesinCatur.Nama} sudah dilepas.", $"{MesinCatur.Nama} was removed."];
            }
            else if ((pesan = await JalankanPasang(t)) is null)
                return HalamanBelajar.Pindah(t, kembali ?? Alamat + "?mesin", judul);
        }

        var isi = new StringBuilder(Kepala(t, judul, t["Untuk menguji jawaban di Tebak langkah.", "Used to test answers in Guess the move."]))
            .Append('\n').Append(HalamanPengaturan.Pesan(pesan)).Append('\n');
        if (!mesin.Terpasang)
            isi.Append(KartuMesin(t, kembali));
        else
            isi.Append($"""
                <div class="tulis kartu-mesin">
                  <p><strong>{t[$"{MesinCatur.Nama} sudah terpasang.", $"{MesinCatur.Nama} is installed."]}</strong><br><span class="catatan">{LisensiMesin(t)}</span></p>
                  <form action="{Alamat}?mesin" method="post"><input type="hidden" name="aksi" value="lepas"><input type="hidden" name="token" value="{TokenSekali.Buat()}">
                  <p class="tombol-tombol">{(kembali is null ? "" : $"""<a class="tombol utama" href="{HtmlEncode(kembali)}">{t["Kembali ke latihan", "Back to practice"]}</a> """)}<button class="tombol" type="submit">{t["Lepas mesinnya", "Remove the engine"]}</button></p></form>
                </div>

                """);
        return (judul, isi.Append(Tautan()).ToString());
    }

    // Memasang Stockfish; null kalau berhasil, selain itu pesan galatnya.
    async Task<string?> JalankanPasang(Teks t)
    {
        try
        {
            using var batas = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await mesin.Pasang(batas.Token);
            return null;
        }
        catch (InvalidDataException)
        {
            return t["Berkas yang terunduh tidak sama dengan yang diharapkan, jadi tidak dipakai. Coba lagi nanti.", "The downloaded file isn't the expected one, so it wasn't used. Try again later."];
        }
        catch (IOException)
        {
            return t["Mesinnya tidak bisa disimpan. Periksa sisa ruang disk.", "The engine couldn't be saved. Check the free disk space."];
        }
        catch (Exception e) when (e is GalatAi or OperationCanceledException)
        {
            return PesanGalat(t, "GitHub", "", e);
        }
    }

    // ?mesin&pasang lewat fetch (KartuMesin): {"ok": true}, atau galat dengan
    // token baru supaya tombolnya bisa ditekan lagi.
    async Task<(byte[], string)> PasangLewatSkrip(Kueri kueri, Teks t)
    {
        var galat = !TokenSekali.Pakai(kueri["token"])
            ? t["Permintaan ini sudah dipakai atau kedaluwarsa. Tekan tombolnya lagi.", "This request was already used or has expired. Press the button again."]
            : await JalankanPasang(t);
        return Json(json =>
        {
            if (galat is null)
                json.WriteBoolean("ok", true);
            else
            {
                json.WriteString("galat", galat);
                json.WriteString("token", TokenSekali.Buat());
            }
        });
    }

    // Ajakan memasang Stockfish: sekali klik, diunduh dan diperiksa sendiri.
    // Dengan JavaScript, pemasangannya lewat fetch dan halaman ini tetap
    // hidup: tombolnya mati (tidak terkirim dua kali; MesinCatur.Pasang juga
    // tidak mengunduh dua kali), kemajuan unduhan tampil (?mesin&kemajuan,
    // tiap 0,4 detik), lalu pindah ke tujuannya. Dengan formulir POST biasa,
    // WebKit tidak memperbarui tampilan halaman selama menunggu jawabannya
    // (terlihat 7 Okt 2026: tombol mati dan bilah kemajuan tidak pernah
    // tergambar). Formulir biasa tetap jadi cadangan kalau fetch gagal.
    // Skripnya tanpa komentar: ikut terkirim ke halaman, juga yang berbahasa Inggris.
    static string KartuMesin(Teks t, string? kembali) => $$"""
        <div class="tulis kartu-mesin">
          <p><strong>{{t[$"Tebak langkah memakai mesin catur {MesinCatur.Nama}.", $"Guess the move uses the {MesinCatur.Nama} chess engine."]}}</strong><br><span class="catatan">{{t[
              $"Cukup sekali pasang: {t.Ukuran(MesinCatur.Ukuran)} diunduh, lalu mesinnya berjalan di laptop ini tanpa internet. ",
              $"Install it once: {t.Ukuran(MesinCatur.Ukuran)} is downloaded, then the engine runs on this laptop without internet. "]}}{{LisensiMesin(t)}}</span></p>
          <form id="pasang-mesin" action="{{Alamat}}?mesin" method="post" data-tujuan="{{HtmlEncode(kembali ?? Alamat + "?mesin")}}"><input type="hidden" name="aksi" value="pasang"><input type="hidden" name="token" value="{{TokenSekali.Buat()}}">{{(kembali is null ? "" : $"""<input type="hidden" name="kembali" value="{HtmlEncode(kembali)}">""")}}
          <p class="tombol-tombol"><button class="tombol utama" type="submit">{{Ikon.Unduh}}{{t[$"Pasang {MesinCatur.Nama}", $"Install {MesinCatur.Nama}"]}}</button></p>
          <p class="kemajuan-unduh" hidden><progress max="100" value="0"></progress> <span data-teks="{{HtmlEncode(t["Mengunduh… {0}%", "Downloading… {0}%"])}}">{{t["Menghubungi GitHub…", "Contacting GitHub…"]}}</span></p></form>
        </div>
        <script>
        'use strict';
        (() => {
          const form = document.getElementById('pasang-mesin');
          const tombol = form.querySelector('button'), kemajuan = form.querySelector('.kemajuan-unduh');
          const bilah = kemajuan.querySelector('progress'), teks = kemajuan.querySelector('span'), awal = teks.textContent;
          form.addEventListener('submit', e => {
            e.preventDefault();
            if (tombol.disabled)
              return;
            tombol.disabled = true;
            kemajuan.hidden = bilah.hidden = false;
            bilah.value = 0;
            teks.textContent = awal;
            let selesai = false;
            const tanya = () => {
              if (selesai)
                return;
              fetch('kevin://catur?mesin&kemajuan').then(r => r.json()).then(k => {
                if (!selesai && k.total > 0) {
                  const persen = Math.floor(100 * k.bait / k.total);
                  bilah.value = persen;
                  teks.textContent = teks.dataset.teks.replace('{0}', persen);
                }
              }).catch(() => {}).finally(() => setTimeout(tanya, 400));
            };
            setTimeout(tanya, 300);
            fetch('kevin://catur?mesin&pasang', { method: 'POST', body: new URLSearchParams(new FormData(form)) })
              .then(r => r.json()).then(j => {
                selesai = true;
                if (j.ok) {
                  location.replace(form.dataset.tujuan);
                  return;
                }
                bilah.hidden = true;
                teks.textContent = j.galat;
                form.elements.token.value = j.token;
                tombol.disabled = false;
              }).catch(() => form.submit());
          });
        })();
        </script>

        """;

    static string LisensiMesin(Teks t) => t[
        """Stockfish adalah perangkat lunak bebas berlisensi GPL-3.0, versi WebAssembly dari <a href="https://github.com/nmrugg/stockfish.js">stockfish.js</a>, disimpan terpisah dari Kevin Browser.""",
        """Stockfish is free software under the GPL-3.0, in the WebAssembly build from <a href="https://github.com/nmrugg/stockfish.js">stockfish.js</a>, stored separately from Kevin Browser."""];
}
