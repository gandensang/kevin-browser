using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// kevin://catur: partai dari akun Lichess dan Chess.com (hanya nama akun,
/// tanpa login) atau dari PGN yang ditempel, diputar ulang di papan sendiri.
/// Satu-satunya halaman kevin:// yang memakai JavaScript (papan.js, catur.js,
/// latihan.js); daftar, formulir, dan aksi tetap halaman biasa tanpa JS.
/// </summary>
/// <remarks>
/// Alamat: <c>kevin://catur</c> (daftar, dengan saringan <c>hasil</c>,
/// <c>warna</c>, <c>situs</c>, <c>cari</c>, <c>n</c>), <c>?ambil</c> (ambil
/// partai terbaru akun yang dihubungkan), <c>?akun</c>, <c>?tempel</c>, dan
/// <c>?partai=ID</c> (pemutar; POST <c>aksi=simpan</c> menyimpannya jadi
/// catatan di mata pelajaran Catur), <c>?latihan=ID</c> (tebak langkah
/// bersama pelatih AI), dan <c>?mesin</c> (Stockfish).
/// </remarks>
public sealed partial class HalamanCatur(KoleksiPartai koleksi, MesinCatur mesin, BukuCatatan buku, TimeProvider waktu, PelatihCatur? pelatih = null)
    : IHalaman, IDataHalaman
{
    const string Alamat = "kevin://catur";
    const string MapelCatur = "catur";
    const int PerHalaman = 50;

    // Hasil "Ambil partai terbaru", ditampilkan sekali di halaman sesudahnya.
    string? pesanAmbil;

    AkunCatur Akun => koleksi.Akun;

    public async Task<(string Judul, string Isi)> Buat(string uri, string? isiPost, Teks t)
    {
        var kueri = new Kueri(uri, isiPost);
        var post = isiPost is not null;
        return kueri["latihan"] is { } idLatihan ? Latihan(t, kueri, idLatihan, post)
            : kueri["mesin"] is not null ? await PasangMesin(t, kueri, post)
            : kueri["partai"] is { } id ? Lihat(t, kueri, id, post)
            : kueri["tempel"] is not null ? await Tempel(t, kueri, post)
            : kueri["akun"] is not null ? AturAkun(t, kueri, post)
            : kueri["ambil"] is not null ? await Ambil(t)
            : Daftar(t, kueri);
    }

    // ---------- data untuk skrip halaman catur ----------

    const string AwalMesin = "kevin-mesin://mesin/";

    /// <summary>
    /// Berkas mesin (kevin-mesin://mesin/…), kemajuan pemasangannya, dan JSON
    /// untuk latihan.js; null untuk halaman biasa.
    /// </summary>
    public async Task<(byte[] Isi, string Jenis)?> Data(string uri, string? isiPost, Teks t)
    {
        if (uri.StartsWith(AwalMesin, StringComparison.OrdinalIgnoreCase))
        {
            var nama = uri[AwalMesin.Length..];
            var akhir = nama.IndexOfAny(['?', '#']);
            if (akhir >= 0)
                nama = nama[..akhir];
            if (nama == "perantara.html")
                return (Encoding.UTF8.GetBytes(Perantara), "text/html");
            return mesin.Baca(nama) is { } isi ? (isi, nama.EndsWith(".wasm", StringComparison.Ordinal) ? "application/wasm" : "text/javascript") : null;
        }
        var kueri = new Kueri(uri, isiPost);
        if (kueri["mesin"] is not null && kueri["kemajuan"] is not null)
            return Json(json =>
            {
                var (bait, total) = mesin.Kemajuan;
                json.WriteNumber("bait", bait);
                json.WriteNumber("total", total);
            });
        if (kueri["mesin"] is not null && kueri["pasang"] is not null && isiPost is not null)
            return await PasangLewatSkrip(kueri, t);
        if (kueri["latihan"] is not null && isiPost is not null && (kueri["soal"] ?? kueri["kirim"] ?? kueri["lanjut"] ?? kueri["selesai"]) is not null)
            return await DataLatihan(kueri, t);
        return null;
    }

    // Halaman perantara di kevin-mesin://: menjalankan Stockfish sebagai Worker
    // dan meneruskan pesan UCI dari dan ke halaman catur. Perintah hanya
    // diterima dari kevin://catur. Keluarannya dikirim ke '*' karena WebKit
    // tidak mencocokkan asal skema lokal sebagai tujuan postMessage (terlihat
    // 7 Okt 2026: dengan tujuan 'kevin://catur' tidak ada yang sampai); isinya
    // hanya keluaran mesin, dan halaman catur memeriksa pengirimnya.
    const string Perantara = $$"""
        <!doctype html><meta charset="utf-8"><script>
        'use strict';
        let mesin;
        try {
          mesin = new Worker('{{MesinCatur.Skrip}}#' + encodeURIComponent(new URL('{{MesinCatur.Wasm}}', location.href)));
          mesin.onmessage = e => parent.postMessage({ mesin: String(e.data) }, '*');
          mesin.onerror = () => parent.postMessage({ galat: true }, '*');
        } catch (e) {
          parent.postMessage({ galat: true }, '*');
        }
        addEventListener('message', e => {
          if (e.origin === 'kevin://catur' && mesin && typeof e.data === 'string')
            mesin.postMessage(e.data);
        });
        parent.postMessage({ siap: true }, '*');
        </script>
        """;

    // ---------- kevin://catur: daftar partai ----------

    (string, string) Daftar(Teks t, Kueri kueri)
    {
        var pesan = kueri["diambil"] is not null ? Interlocked.Exchange(ref pesanAmbil, null) : null;
        var hasil = kueri["hasil"];
        var warna = kueri["warna"];
        var situs = kueri["situs"];
        var cari = (kueri["cari"] ?? "").Trim();
        var semua = koleksi.Semua();
        var isi = new StringBuilder(HalamanBelajar.Kepala(t, t["Catur", "Chess"], jejak: false,
            aksi: semua.Count == 0 ? "" : KotakCari(t, hasil, warna, situs, cari),
            keterangan: t["Partaimu dari Lichess dan Chess.com, diputar ulang di papan sendiri.", "Your games from Lichess and Chess.com, replayed on our own board."]));
        isi.Append("<div class=\"baris-akun\">")
            .Append(Akun.Ada ? $"""<a class="tombol utama" href="{Alamat}?ambil">{Ikon.Unduh}{t["Ambil partai terbaru", "Get latest games"]}</a>""" : "")
            .Append($"""<a class="tombol" href="{Alamat}?tempel">{Ikon.Tempel}{t["Tempel PGN", "Paste PGN"]}</a>""")
            .Append(Akun.Ada ? KeteranganAkun(t) : "")
            .Append("</div>\n")
            .Append(HalamanPengaturan.Pesan(pesan)).Append('\n');
        if (!Akun.Ada)
            isi.Append(KartuAkun(t));

        if (semua.Count == 0)
        {
            isi.Append($"""
                <div class="kosong">
                <p><strong>{t["Belum ada partai.", "No games yet."]}</strong></p>
                <p>{(Akun.Ada
                    ? t["Tekan Ambil partai terbaru, atau tempel PGN sebuah partai.", "Press Get latest games, or paste the PGN of a game."]
                    : t["Hubungkan akun Lichess atau Chess.com di atas, atau tempel PGN sebuah partai.", "Connect a Lichess or Chess.com account above, or paste the PGN of a game."])}</p>
                </div>

                """);
            return (t["Catur", "Chess"], isi.Append(Tautan()).ToString());
        }

        var cocok = semua.Where(p =>
        {
            var putih = SisiSaya(p);
            return (hasil is null || HasilSaya(p, putih) == hasil)
                && (warna is null || (putih is { } w && (w ? "putih" : "hitam") == warna))
                && (situs is null || Situs(p) == situs)
                && (cari.Length == 0 || $"{p.Putih} {p.Hitam} {Pembukaan(p)} {p["Event"]}".Contains(cari, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        var n = int.TryParse(kueri["n"], out var jumlah) && jumlah > 0 ? Math.Min(jumlah, KoleksiPartai.MaksPerSumber * 3) : PerHalaman;

        isi.Append(Saringan(t, hasil, warna, situs, cari));

        if (cocok.Count == 0)
            isi.Append($"<p class=\"kosong\">{t["Tidak ada partai yang cocok dengan saringan ini.", "No games match this filter."]}</p>\n");
        else
        {
            isi.Append("<ul class=\"daftar-partai\">\n");
            foreach (var p in cocok.Take(n))
                isi.Append(BarisPartai(t, p)).Append('\n');
            isi.Append("</ul>\n");
            if (cocok.Count > n)
                isi.Append($"""<p class="tengah"><a class="tombol" href="{HtmlEncode(AlamatSaringan(hasil, warna, situs, cari, n + PerHalaman))}">{t[$"Tampilkan lebih banyak ({cocok.Count - n} lagi)", $"Show more ({cocok.Count - n} more)"]}</a></p>""").Append('\n');
        }
        return (t["Catur", "Chess"], isi.Append(Tautan()).ToString());
    }

    static string Tautan() => """<link rel="stylesheet" href="kevin://catur.css">""";

    string KartuAkun(Teks t) => $"""
                <form class="tulis kartu-akun" action="{Alamat}?akun" method="post">
                  <input type="hidden" name="token" value="{TokenSekali.Buat()}">
                  <p><strong>{t["Hubungkan akunmu", "Connect your account"]}</strong><br><span class="catatan">{t[
                      "Cukup nama akunnya: partai di Lichess dan Chess.com terbuka untuk umum, jadi tidak perlu login atau kata sandi.",
                      "Just the account name: games on Lichess and Chess.com are public, so no login or password is needed."]}</span></p>
                  <div class="baris">
                  <label>Lichess <input type="text" name="lichess" autocomplete="off" spellcheck="false" placeholder="{t["nama akun", "username"]}"></label>
                  <label>Chess.com <input type="text" name="chesscom" autocomplete="off" spellcheck="false" placeholder="{t["nama akun", "username"]}"></label>
                  </div>
                  <p class="tombol-tombol"><button class="tombol utama" type="submit">{t["Hubungkan", "Connect"]}</button></p>
                </form>

                """;

    string KeteranganAkun(Teks t)
    {
        var bagian = new List<string>();
        if (Akun.Lichess is { } l)
            bagian.Add($"Lichess <strong>{HtmlEncode(l)}</strong>");
        if (Akun.ChessCom is { } c)
            bagian.Add($"Chess.com <strong>{HtmlEncode(c)}</strong>");
        return $"""<span class="catatan akun">{t["Akun:", "Accounts:"]} {string.Join(" · ", bagian)} · <a href="{Alamat}?akun">{t["ubah", "change"]}</a></span>""";
    }

    string Saringan(Teks t, string? hasil, string? warna, string? situs, string cari)
    {
        string Pilihan(string label, string? h, string? w, string? s, bool aktif) =>
            $"""<a class="pilihan{(aktif ? " aktif" : "")}" href="{HtmlEncode(AlamatSaringan(h, w, s, cari, 0))}"{(aktif ? " aria-current=\"true\"" : "")}>{label}</a>""";
        var sb = new StringBuilder("<nav class=\"saringan\" aria-label=\"").Append(t["Saringan", "Filter"]).Append("\">\n<div>");
        sb.Append(Pilihan(t["Semua", "All"], null, warna, situs, hasil is null))
            .Append(Pilihan(t["Menang", "Won"], "menang", warna, situs, hasil == "menang"))
            .Append(Pilihan(t["Kalah", "Lost"], "kalah", warna, situs, hasil == "kalah"))
            .Append(Pilihan(t["Seri", "Drawn"], "seri", warna, situs, hasil == "seri"));
        sb.Append("</div><div>")
            .Append(Pilihan(t["Putih", "White"], hasil, warna == "putih" ? null : "putih", situs, warna == "putih"))
            .Append(Pilihan(t["Hitam", "Black"], hasil, warna == "hitam" ? null : "hitam", situs, warna == "hitam"));
        sb.Append("</div><div>")
            .Append(Pilihan("Lichess", hasil, warna, situs == "lichess" ? null : "lichess", situs == "lichess"))
            .Append(Pilihan("Chess.com", hasil, warna, situs == "chesscom" ? null : "chesscom", situs == "chesscom"))
            .Append(Pilihan(t["Tempelan", "Pasted"], hasil, warna, situs == "pgn" ? null : "pgn", situs == "pgn"));
        return sb.Append("</div>\n</nav>\n").ToString();
    }

    // Cari lawan atau pembukaan; saringan yang aktif ikut. Enter mencari.
    static string KotakCari(Teks t, string? hasil, string? warna, string? situs, string cari)
    {
        var sb = new StringBuilder($"""<form class="cari-catatan" action="{Alamat}" method="get" role="search">{Ikon.Cari}""");
        foreach (var (nama, nilai) in new[] { ("hasil", hasil), ("warna", warna), ("situs", situs) })
            if (nilai is not null)
                sb.Append($"""<input type="hidden" name="{nama}" value="{HtmlEncode(nilai)}">""");
        return sb.Append($"""<input type="search" name="cari" value="{HtmlEncode(cari)}" placeholder="{t["Cari lawan atau pembukaan", "Search opponent or opening"]}" aria-label="{t["Cari lawan atau pembukaan", "Search opponent or opening"]}"></form>""").ToString();
    }

    static string AlamatSaringan(string? hasil, string? warna, string? situs, string cari, int n)
    {
        var bagian = new List<string>();
        foreach (var (nama, nilai) in new[] { ("hasil", hasil), ("warna", warna), ("situs", situs), ("cari", cari.Length == 0 ? null : cari) })
            if (nilai is not null)
                bagian.Add($"{nama}={Uri.EscapeDataString(nilai)}");
        if (n > 0)
            bagian.Add($"n={n}");
        return bagian.Count == 0 ? Alamat : $"{Alamat}?{string.Join('&', bagian)}";
    }

    string BarisPartai(Teks t, Partai p)
    {
        var putih = SisiSaya(p);
        var hasil = HasilSaya(p, putih);
        var lencana = hasil is null
            ? $"""<span class="hasil">{HtmlEncode(p.Hasil == "1/2-1/2" ? "½-½" : p.Hasil)}</span>"""
            : $"""<span class="hasil {hasil}">{hasil switch { "menang" => t["Menang", "Won"], "kalah" => t["Kalah", "Lost"], _ => t["Seri", "Draw"] }}</span>""";
        string Pemain(bool sisiPutih) =>
            HtmlEncode(sisiPutih ? p.Putih : p.Hitam) + (p[sisiPutih ? "WhiteElo" : "BlackElo"] is { } elo ? $" <small>({HtmlEncode(elo)})</small>" : "");
        var judul = putih is { } w
            ? $"""<span class="bidak-kecil {(w ? "putih" : "hitam")}" title="{(w ? t["Kamu putih", "You played white"] : t["Kamu hitam", "You played black"])}"></span>{t["vs", "vs"]} {Pemain(!w)}"""
            : $"{Pemain(true)} {t["vs", "vs"]} {Pemain(false)}";
        var keterangan = string.Join(" · ", new[] { Kecepatan(t, p), NamaSitus(t, p) }.Where(s => s is not null).Select(s => HtmlEncode(s)));
        var pembukaan = Pembukaan(p) is { } nama ? $"""<small class="pembukaan">{HtmlEncode(nama)}</small>""" : "";
        var tanggal = p.Waktu is { } d ? $"<time>{t.TanggalSingkat(d, d.Year != waktu.GetUtcNow().Year)}</time>" : "";
        return $"""<li><a href="{Alamat}?partai={Uri.EscapeDataString(p.Id)}">{lencana}<span class="isi"><strong>{judul}</strong>{pembukaan}</span><span class="ket"><small>{keterangan}</small>{tanggal}</span></a></li>""";
    }

    // ---------- ?partai=ID: pemutar partai ----------

    (string, string) Lihat(Teks t, Kueri kueri, string id, bool post)
    {
        if (koleksi.Cari(id) is not { } p)
            return TidakAda(t);
        string? pesan = null;
        if (post && kueri["aksi"] == "simpan")
        {
            if (!TokenSekali.Pakai(kueri["token"]))
                pesan = t["Permintaan ini sudah dipakai atau kedaluwarsa. Tekan Simpan ke catatan lagi.", "This request was already used or has expired. Press Save as a note again."];
            else if (CariCatatan(p) is { } ada)
                return HalamanBelajar.Pindah(t, HalamanBelajar.Alamat(ada));   // sudah pernah disimpan: tidak dobel
            else if (buku.Tulis(MapelCatur, JudulCatatan(t, p), IsiCatatan(t, p), waktu.GetLocalNow().DateTime) is { } catatan)
                return HalamanBelajar.Pindah(t, HalamanBelajar.Alamat(catatan));
            else
                pesan = t["Catatannya tidak bisa disimpan.", "The note couldn't be saved."];
        }

        var u = p.Urai();
        var saya = SisiSaya(p);
        var balik = saya == false;
        var judul = $"{p.Putih} vs {p.Hitam}";
        var keterangan = string.Join(" · ", new[] { p["Event"], p.Waktu is { } d ? t.TanggalSingkat(d) : null, TeksHasil(t, p) }.Where(s => s is not null)
            .Select(s => HtmlEncode(s!)));
        var aksi = new StringBuilder($"""
            <a class="tombol utama" href="{HtmlEncode($"{Alamat}?latihan={Uri.EscapeDataString(p.Id)}")}">{Ikon.Tebak}{t["Tebak langkah", "Guess the move"]}</a>
            <form action="{HtmlEncode($"{Alamat}?partai={Uri.EscapeDataString(p.Id)}")}" method="post"><input type="hidden" name="aksi" value="simpan"><input type="hidden" name="token" value="{TokenSekali.Buat()}"><button class="tombol" type="submit">{Ikon.Buku}{t["Simpan ke catatan", "Save as a note"]}</button></form>
            """);
        // Situs catur dibuka di tab baru, supaya pemutar partai tetap ada (permintaan pemakai 7 Okt 2026).
        if (p.Tautan is { } tautan)
            aksi.Append($"""<a class="tombol" href="{HtmlEncode(tautan)}" target="_blank" rel="noopener">{Ikon.Keluar}{(p.Id.StartsWith("chesscom-", StringComparison.Ordinal) ? t["Buka di Chess.com", "Open on Chess.com"] : t["Buka di Lichess", "Open on Lichess"])}</a>""");

        var isi = new StringBuilder($"""
            <header class="kepala-partai">
              <p class="jejak"><a href="{Alamat}">{t["Catur", "Chess"]}</a></p><h1>{HtmlEncode(judul)}</h1><p class="keterangan">{keterangan}</p>
            </header>
            {HalamanPengaturan.Pesan(pesan)}
            {(u.Galat is null ? "" : HalamanPengaturan.Pesan(t[$"PGN ini berhenti di langkah {u.Galat}: langkah itu tidak sah. Langkah sebelumnya tetap bisa diputar.",
                $"This PGN stops at move {u.Galat}: that move isn't legal. The moves before it can still be replayed."]))}
            <div class="tata-papan">
            <div class="kolom-papan{(balik ? " terbalik" : "")}">
              {Pemain(p, false)}
              <div class="papan-wadah"><div class="bilah-nilai" hidden><div></div></div><div id="papan" class="papan" aria-label="{t["Papan catur", "Chessboard"]}"></div></div>
              {Pemain(p, true)}
            </div>
            <aside class="kolom-langkah">
            <div class="aksi-partai">{aksi}</div>

            """);
        if (Pembukaan(p) is { } pembukaan)
            isi.Append($"""<p class="pembukaan-partai">{HtmlEncode((p["ECO"] is { } eco ? eco + " " : "") + pembukaan)}</p>""").Append('\n');
        isi.Append(DaftarLangkah(u)).Append('\n')
            .Append($"""
                <div class="kendali">
                  <button class="tombol" type="button" data-aksi="awal" title="{t["Awal", "Start"]}" aria-label="{t["Awal", "Start"]}">{Ikon.KeAwal}</button>
                  <button class="tombol" type="button" data-aksi="mundur" title="{t["Mundur (←)", "Back (←)"]}" aria-label="{t["Mundur", "Back"]}">{Ikon.Mundur}</button>
                  <button class="tombol" type="button" data-aksi="maju" title="{t["Maju (→)", "Forward (→)"]}" aria-label="{t["Maju", "Forward"]}">{Ikon.Maju}</button>
                  <button class="tombol" type="button" data-aksi="akhir" title="{t["Akhir", "End"]}" aria-label="{t["Akhir", "End"]}">{Ikon.KeAkhir}</button>
                  <button class="tombol" type="button" data-aksi="balik" title="{t["Balik papan", "Flip board"]}" aria-label="{t["Balik papan", "Flip board"]}">{Ikon.Balik}</button>
                </div>

                """)
            .Append($"""<p id="komentar" class="komentar" hidden></p>""").Append('\n')
            .Append($"""<p class="catatan"><a id="analisis" href="https://lichess.org/analysis" target="_blank" rel="noopener">{Ikon.Keluar}{t["Analisis posisi ini di Lichess", "Analyse this position on Lichess"]}</a></p>""").Append('\n')
            .Append("</aside>\n</div>\n")
            .Append("""<script type="application/json" id="data-partai">""").Append(DataPartai(u, balik)).Append("</script>\n")
            .Append(Tautan()).Append('\n')
            .Append("""<script src="kevin://papan.js"></script>""").Append('\n')
            .Append("""<script src="kevin://catur.js"></script>""");
        return (judul, isi.ToString());
    }

    static string Pemain(Partai p, bool putih)
    {
        var nama = putih ? p.Putih : p.Hitam;
        var elo = p[putih ? "WhiteElo" : "BlackElo"] is { } e ? $" <small>{HtmlEncode(e)}</small>" : "";
        return $"""<div class="pemain {(putih ? "putih" : "hitam")}"><span class="bidak-kecil {(putih ? "putih" : "hitam")}"></span><span>{HtmlEncode(nama)}{elo}</span></div>""";
    }

    // Langkah per baris "12. e4 e5"; tombolnya dipakai catur.js. Partai dari
    // FEN yang dimulai hitam diawali "12. …".
    static string DaftarLangkah(UraianPartai u)
    {
        var awal = Papan.DariFen(u.FenAwal)!;
        var nomor = awal.NomorLangkah;
        var giliranPutih = awal.GiliranPutih;
        var sb = new StringBuilder("<ol class=\"daftar-langkah\" id=\"langkah\">");
        for (var i = 0; i < u.Langkah.Count; i++)
        {
            if (giliranPutih || i == 0)
                sb.Append($"<li><span class=\"no\">{nomor}.</span>").Append(giliranPutih ? "" : "<span class=\"lewat\">…</span>");
            var l = u.Langkah[i];
            var kelas = l.Tanda switch { "??" => "t-blunder", "?" => "t-salah", "?!" => "t-ragu", "!" or "!!" => "t-bagus", "!?" => "t-menarik", _ => null };
            sb.Append($"""<button type="button" data-ply="{i + 1}"{(kelas is null ? "" : $" class=\"{kelas}\"")}>{HtmlEncode(l.San)}{(l.Tanda is null ? "" : $"<span class=\"tanda\">{HtmlEncode(l.Tanda)}</span>")}</button>""");
            if (!giliranPutih)
            {
                sb.Append("</li>");
                nomor++;
            }
            giliranPutih = !giliranPutih;
        }
        if (!giliranPutih)
            sb.Append("</li>");
        return sb.Append("</ol>").ToString();
    }

    // Data untuk catur.js. Encoder bawaan meng-escape < > & ' ", jadi aman di
    // dalam <script>.
    static string DataPartai(UraianPartai u, bool balik)
    {
        using var aliran = new MemoryStream();
        using (var json = new Utf8JsonWriter(aliran, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            json.WriteStartObject();
            json.WriteString("fen0", u.FenAwal);
            json.WriteBoolean("balik", balik);
            json.WriteStartArray("langkah");
            foreach (var l in u.Langkah)
            {
                json.WriteStartObject();
                json.WriteString("fen", l.Fen);
                json.WriteString("dari", Papan.NamaKotak(l.Langkah.Dari));
                json.WriteString("ke", Papan.NamaKotak(l.Langkah.Ke));
                if (l.Nilai is { } nilai)
                    json.WriteNumber("nilai", nilai);
                if (l.Mat is { } mat)
                    json.WriteNumber("mat", mat);
                if (l.Komentar is { } komentar)
                    json.WriteString("komentar", komentar);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(aliran.ToArray());
    }

    // Partai sendiri: "Partai vs lawan, 5 Okt 2026" (nama berkasnya jadi
    // …-partai-vs-lawan-…, seperti catatan catur KEVIN); partai orang lain:
    // "Putih vs Hitam, 5 Okt 2026".
    string JudulCatatan(Teks t, Partai p) =>
        (SisiSaya(p) is { } putih ? $"{t["Partai vs", "Game vs"]} {(putih ? p.Hitam : p.Putih)}" : $"{p.Putih} vs {p.Hitam}")
        + (p.Waktu is { } d ? $", {t.TanggalSingkat(d)}" : "");

    // Catatan partai ini di mata pelajaran Catur, dikenali dari tautannya ke papan.
    Catatan? CariCatatan(Partai p)
    {
        var tautan = $"{Alamat}?partai={Uri.EscapeDataString(p.Id)}";
        return buku.Semua().FirstOrDefault(c => c.Mapel == MapelCatur && buku.Baca(c.Mapel, c.Nama) is { } isi && isi.Contains(tautan, StringComparison.Ordinal));
    }

    // Catatan partai di mata pelajaran Catur: kepala, langkah, dan tempat
    // pelajarannya. Baris "Sumber:" paling atas tampil di bawah judul.
    // Langkahnya di blok ```pgn (tanpa itu "1. e4 …" terbaca daftar
    // bernomor), dipotong per ±64 huruf supaya muat di kertas catatan.
    string IsiCatatan(Teks t, Partai p)
    {
        var u = p.Urai();
        var token = new List<string>();
        var papan = Papan.DariFen(u.FenAwal)!;
        var (nomor, putih) = (papan.NomorLangkah, papan.GiliranPutih);
        for (var i = 0; i < u.Langkah.Count; i++)
        {
            token.Add((putih ? $"{nomor}. " : i == 0 ? $"{nomor}... " : "") + u.Langkah[i].San);
            if (!putih)
                nomor++;
            putih = !putih;
        }
        token.Add(p.Hasil);
        var baris = new StringBuilder();
        var panjang = 0;
        foreach (var k in token)
        {
            if (panjang > 0 && panjang + 1 + k.Length > 64)
            {
                baris.Append('\n');
                panjang = 0;
            }
            else if (panjang > 0)
            {
                baris.Append(' ');
                panjang++;
            }
            baris.Append(k);
            panjang += k.Length;
        }
        var sumber = string.Join(", ", new[] { NamaSitus(t, p), p.Waktu is { } d ? t.TanggalSingkat(d) : null, Kecepatan(t, p) }.Where(s => s is not null));
        var hasil = $"- {t["Hasil", "Result"]}: {TeksHasil(t, p)}{(p["Termination"] is { } akhir ? $" ({akhir})" : "")}";
        if (Pembukaan(p) is { } buka)
            hasil += $"\n- {t["Pembukaan", "Opening"]}: {(p["ECO"] is { } eco ? eco + " " : "")}{buka}";
        return $"""
            Sumber: {p.Tautan ?? t["PGN tempelan", "pasted PGN"]} ({sumber})

            {hasil}

            [{t["Buka di papan Catur", "Open on the Chess board"]}]({Alamat}?partai={Uri.EscapeDataString(p.Id)})

            ## {t["Langkah", "Moves"]}

            ```pgn
            {baris}
            ```

            {MomenLichess(t, p)}## {t["Pelajaran", "Lessons"]}

            {t["(Tulis sendiri, atau latih partai ini dengan Tebak langkah: hasilnya ditambahkan ke catatan ini.)",
                "(Write your own, or practise this game with Guess the move: the results are added to this note.)"]}
            """;
    }

    // Langkah bertanda ?, ??, ?! dari analisis Lichess, milik sisi sendiri
    // (atau kedua sisi kalau bukan partai sendiri). Komentar Lichess
    // "(0.25 → -0.30) Mistake. Nf3 was best." dijadikan kalimat sendiri.
    string MomenLichess(Teks t, Partai p)
    {
        var u = p.Urai();
        var saya = SisiSaya(p);
        var papan = Papan.DariFen(u.FenAwal)!;
        var (nomor, putih) = (papan.NomorLangkah, papan.GiliranPutih);
        var baris = new List<string>();
        foreach (var l in u.Langkah)
        {
            if (l.Tanda is "??" or "?" or "?!" && (saya is null || saya == putih))
                baris.Add($"- {nomor}{(putih ? "." : "...")} {l.San}{l.Tanda}: {Penilaian(t, l)}");
            if (!putih)
                nomor++;
            putih = !putih;
        }
        return baris.Count == 0 ? ""
            : $"## {t["Momen penting (analisis Lichess)", "Key moments (Lichess analysis)"]}\n\n{string.Join('\n', baris)}\n\n";
    }

    static string Penilaian(Teks t, LangkahPartai l)
    {
        var teks = l.Tanda switch { "??" => "blunder", "?" => t["kesalahan", "mistake"], _ => t["ketidaktepatan", "inaccuracy"] };
        var k = l.Komentar ?? "";
        if (k.StartsWith('(') && k.IndexOf(')') is var tutup and > 1)
            teks += $" ({(t.Inggris ? k[1..tutup] : k[1..tutup].Replace('.', ','))})";
        var terbaik = k.IndexOf(" was best", StringComparison.Ordinal);
        if (terbaik > 0)
            teks += $". {t["Lebih baik", "Better"]}: {k[(k.LastIndexOf(' ', terbaik - 1) + 1)..terbaik]}";
        return teks + ".";
    }

    // ---------- ?tempel ----------

    async Task<(string, string)> Tempel(Teks t, Kueri kueri, bool post)
    {
        var judul = t["Tempel PGN", "Paste PGN"];
        var teks = kueri["pgn"] ?? "";
        string? pesan = null;
        if (post)
        {
            if (teks.Trim().Length == 0)
                pesan = t["Tempel dulu PGN atau tautan partainya.", "Paste the PGN or the game link first."];
            else if (!TokenSekali.Pakai(kueri["token"]))
                pesan = t["Permintaan ini sudah dipakai atau kedaluwarsa. Periksa teksnya, lalu tekan Buka lagi.", "This request was already used or has expired. Check the text, then press Open again."];
            else if (KoleksiPartai.IdLichess(teks) is { } id)
            {
                try
                {
                    using var batas = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    if (await koleksi.AmbilSatuLichess(id, batas.Token) is { } p)
                        return HalamanBelajar.Pindah(t, $"{Alamat}?partai={Uri.EscapeDataString(p.Id)}", judul);
                    pesan = t["Partai itu kosong.", "That game has no moves."];
                }
                catch (Exception e) when (e is GalatAi or OperationCanceledException)
                {
                    pesan = PesanGalat(t, "Lichess", id, e);
                }
            }
            else
            {
                var (p, galat) = koleksi.Tempel(teks);
                if (p is not null)
                    return HalamanBelajar.Pindah(t, $"{Alamat}?partai={Uri.EscapeDataString(p.Id)}", judul);
                pesan = galat is { Length: > 0 }
                    ? t[$"Langkah {galat} tidak sah. Periksa PGN-nya.", $"Move {galat} isn't legal. Check the PGN."]
                    : t["Tidak ada langkah catur yang terbaca di teks itu.", "No chess moves could be read in that text."];
            }
        }
        return (judul, $"""
            {Kepala(t, judul, t["Dari Lichess, Chess.com, aplikasi lain, atau kertas notasi yang sudah diketik.", "From Lichess, Chess.com, another app, or a typed-up score sheet."])}
            {HalamanPengaturan.Pesan(pesan)}
            <form class="tulis" action="{Alamat}?tempel" method="post">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <label for="pgn">{t["PGN atau tautan partai Lichess", "PGN or a Lichess game link"]}</label>
              <textarea id="pgn" name="pgn" rows="12" spellcheck="false" autofocus placeholder="1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 …">
            {HtmlEncode(teks)}</textarea>
              <p class="catatan">{t["Notasinya boleh longgar: \"Ngf3\", \"0-0\", atau \"e2e4\" juga terbaca.", "The notation can be loose: \"Ngf3\", \"0-0\", or \"e2e4\" also work."]}</p>
              <p class="tombol-tombol"><button class="tombol utama" type="submit">{t["Buka", "Open"]}</button> <a class="tombol" href="{Alamat}">{t["Batal", "Cancel"]}</a></p>
            </form>
            {Tautan()}
            """);
    }

    // ---------- ?akun ----------

    (string, string) AturAkun(Teks t, Kueri kueri, bool post)
    {
        var judul = t["Akun catur", "Chess accounts"];
        var lichess = kueri["lichess"] ?? Akun.Lichess ?? "";
        var chessCom = kueri["chesscom"] ?? Akun.ChessCom ?? "";
        string? pesan = null;
        if (post)
        {
            var salah = new[] { ("Lichess", lichess), ("Chess.com", chessCom) }.Where(a => a.Item2.Trim().Length > 0 && !AkunCatur.NamaSah(a.Item2.Trim())).Select(a => a.Item1).ToList();
            if (salah.Count > 0)
                pesan = t[$"Nama akun {string.Join(" dan ", salah)} tidak sah: hanya huruf, angka, _ dan -, 2–30 huruf.",
                    $"The {string.Join(" and ", salah)} username isn't valid: only letters, digits, _ and -, 2–30 characters."];
            else if (!TokenSekali.Pakai(kueri["token"]))
                pesan = t["Permintaan ini sudah dipakai atau kedaluwarsa. Periksa isiannya, lalu simpan lagi.", "This request was already used or has expired. Check the fields, then save again."];
            else
            {
                Akun.Simpan(lichess, chessCom);
                return HalamanBelajar.Pindah(t, Akun.Ada ? Alamat + "?ambil" : Alamat, t["Catur", "Chess"]);
            }
        }
        return (judul, $"""
            {Kepala(t, judul, t["Cukup nama akunnya: partai di Lichess dan Chess.com terbuka untuk umum, jadi tidak perlu login atau kata sandi. Kosongkan untuk melepas akun.",
                "Just the account name: games on Lichess and Chess.com are public, so no login or password is needed. Leave a field empty to disconnect it."])}
            {HalamanPengaturan.Pesan(pesan)}
            <form class="tulis" action="{Alamat}?akun" method="post">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <div class="baris">
              <label>Lichess <input type="text" name="lichess" value="{HtmlEncode(lichess)}" autocomplete="off" spellcheck="false" placeholder="{t["nama akun", "username"]}"></label>
              <label>Chess.com <input type="text" name="chesscom" value="{HtmlEncode(chessCom)}" autocomplete="off" spellcheck="false" placeholder="{t["nama akun", "username"]}"></label>
              </div>
              <p class="tombol-tombol"><button class="tombol utama" type="submit">{t["Simpan", "Save"]}</button> <a class="tombol" href="{Alamat}">{t["Batal", "Cancel"]}</a></p>
            </form>
            {Tautan()}
            """);
    }

    // ---------- ?ambil ----------

    // Mengambil partai terbaru, lalu pindah ke daftar (muat ulang tidak
    // mengambil lagi). Pesannya diingat untuk halaman berikutnya.
    async Task<(string, string)> Ambil(Teks t)
    {
        if (!Akun.Ada)
            return HalamanBelajar.Pindah(t, Alamat + "?akun", t["Catur", "Chess"]);
        var pesan = new List<string>();
        using var batas = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        if (Akun.Lichess is { } lichess)
            pesan.Add(await AmbilDari(t, "Lichess", lichess, batal => koleksi.AmbilLichess(lichess, batal), batas.Token));
        if (Akun.ChessCom is { } chessCom)
            pesan.Add(await AmbilDari(t, "Chess.com", chessCom, batal => koleksi.AmbilChessCom(chessCom, batal), batas.Token));
        pesanAmbil = string.Join(" ", pesan);
        return HalamanBelajar.Pindah(t, Alamat + "?diambil", t["Catur", "Chess"]);
    }

    static async Task<string> AmbilDari(Teks t, string situs, string nama, Func<CancellationToken, Task<int>> ambil, CancellationToken batal)
    {
        try
        {
            var baru = await ambil(batal);
            return baru == 0 ? t[$"{situs}: tidak ada partai baru.", $"{situs}: no new games."] : t[$"{situs}: {baru} partai baru.", baru == 1 ? $"{situs}: 1 new game." : $"{situs}: {baru} new games."];
        }
        catch (Exception e) when (e is GalatAi or OperationCanceledException or JsonException)
        {
            return PesanGalat(t, situs, nama, e);
        }
    }

    static string PesanGalat(Teks t, string situs, string nama, Exception e) => e switch
    {
        GalatAi { Kode: 404 } => t[$"{situs}: “{nama}” tidak ditemukan.", $"{situs}: “{nama}” wasn't found."],
        GalatAi { Kode: 429 } => t[$"{situs} meminta menunggu sebentar. Coba lagi semenit lagi.", $"{situs} asked us to wait. Try again in a minute."],
        GalatAi { Kode: 0 } or OperationCanceledException => t[$"{situs} tidak bisa dihubungi. Periksa sambungan internet.", $"Couldn't reach {situs}. Check the internet connection."],
        GalatAi g => t[$"{situs} menjawab galat {g.Kode}.", $"{situs} answered with error {g.Kode}."],
        _ => t[$"Jawaban {situs} tidak bisa dibaca.", $"{situs}'s answer couldn't be read."],
    };

    // ---------- bantuan ----------

    static string Kepala(Teks t, string judul, string keterangan) => $"""
        <header class="kepala-belajar">
          <div><p class="jejak"><a href="{Alamat}">{t["Catur", "Chess"]}</a></p><h1>{judul}</h1><p class="keterangan">{keterangan}</p></div>
        </header>
        """;

    static (string, string) TidakAda(Teks t)
    {
        var judul = t["Partai tidak ditemukan", "Game not found"];
        return (judul, $"""
            {Kepala(t, judul, t["Mungkin akunnya sudah dilepas, atau partainya sudah tergeser partai yang lebih baru.", "The account may have been disconnected, or newer games pushed it out."])}
            <p><a class="tombol" href="{Alamat}">{t["Semua partai", "All games"]}</a></p>
            {Tautan()}
            """);
    }

    // Sisi akun sendiri di partai itu: true putih, false hitam, null bukan partai sendiri.
    bool? SisiSaya(Partai p)
    {
        foreach (var nama in new[] { Akun.Lichess, Akun.ChessCom })
        {
            if (nama is null)
                continue;
            if (p.Putih.Equals(nama, StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.Hitam.Equals(nama, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return null;
    }

    static string? HasilSaya(Partai p, bool? putih) => (putih, p.Hasil) switch
    {
        (null, _) => null,
        (_, "1/2-1/2") => "seri",
        (true, "1-0") or (false, "0-1") => "menang",
        (true, "0-1") or (false, "1-0") => "kalah",
        _ => null,
    };

    static string? TeksHasil(Teks t, Partai p) => p.Hasil switch
    {
        "1-0" => t["Putih menang (1-0)", "White won (1-0)"],
        "0-1" => t["Hitam menang (0-1)", "Black won (0-1)"],
        "1/2-1/2" => t["Seri (½-½)", "Draw (½-½)"],
        _ => t["Belum selesai", "Unfinished"],
    };

    static string Situs(Partai p) =>
        p.Id.StartsWith("lichess-", StringComparison.Ordinal) ? "lichess" : p.Id.StartsWith("chesscom-", StringComparison.Ordinal) ? "chesscom" : "pgn";

    static string NamaSitus(Teks t, Partai p) => Situs(p) switch
    {
        "lichess" => "Lichess",
        "chesscom" => "Chess.com",
        _ => t["PGN", "PGN"],
    };

    /// <summary>Nama pembukaan: kepala Opening (Lichess), atau dari alamat ECOUrl (Chess.com).</summary>
    internal static string? Pembukaan(Partai p)
    {
        if (p["Opening"] is { } nama)
            return nama;
        if (p["ECOUrl"] is { } url && url.IndexOf("/openings/", StringComparison.Ordinal) is var awal and >= 0)
        {
            var slug = Uri.UnescapeDataString(url[(awal + 10)..]).Replace('-', ' ').Trim();
            return slug.Length > 0 ? slug : null;
        }
        return null;
    }

    /// <summary>"Blitz 5+0", "Rapid 10+5", "Harian"; dari TimeControl (detik+tambahan), seperti pembagian Lichess.</summary>
    internal static string? Kecepatan(Teks t, Partai p)
    {
        var tc = p["TimeControl"];
        if (tc is null)
            return null;
        if (tc.Contains('/'))
            return t["Harian", "Daily"];
        var bagian = tc.Split('+');
        if (!int.TryParse(bagian[0], NumberStyles.None, CultureInfo.InvariantCulture, out var dasar))
            return null;
        var tambah = bagian.Length > 1 && int.TryParse(bagian[1], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ? x : 0;
        var perkiraan = dasar + 40 * tambah;
        var jenis = perkiraan < 180 ? "Bullet" : perkiraan < 480 ? "Blitz" : perkiraan < 1500 ? "Rapid" : t["Klasik", "Classical"];
        // "5", "1.5", "45s"; tanpa format angka .NET ("0.#" menyeret ±8 KB kode).
        var persepuluh = dasar * 10 / 60;
        var menit = dasar % 60 == 0 ? (dasar / 60).ToString(CultureInfo.InvariantCulture)
            : dasar < 60 ? $"{dasar}s"
            : $"{persepuluh / 10}.{persepuluh % 10}";
        return $"{jenis} {menit}+{tambah}";
    }
}
