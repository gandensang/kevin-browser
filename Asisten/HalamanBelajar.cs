using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// kevin://belajar: buku catatan pelajaran (<see cref="BukuCatatan"/>).
/// Melihat, mencari, menulis, dan menyunting catatan, tanpa JavaScript.
/// </summary>
/// <remarks>
/// Alamat: <c>kevin://belajar</c> (daftar), <c>?m=fisika&amp;c=nama</c>
/// (satu catatan; <c>&amp;sunting</c> untuk menyunting), <c>?baru</c>,
/// <c>?cari=…</c>, <c>?sumber</c>, dan <c>?buka</c> (folder catatan di
/// pengelola berkas). Menyimpan lewat formulir POST bertoken sekali pakai
/// (<see cref="TokenSekali"/>), jadi isi catatan tidak masuk ke alamat.
/// Sesudah tersimpan, halaman hasilnya langsung pindah ke alamat catatan
/// (meta refresh), supaya mundur, muat ulang, dan tab yang dipulihkan tidak
/// mengirim formulirnya lagi. Kalau token atau sidik berkas tidak cocok,
/// formulirnya tampil lagi berisi teks yang tadi diketik: tidak ada ketikan
/// yang hilang.
/// </remarks>
public sealed class HalamanBelajar(BukuCatatan buku, TimeProvider waktu, Action<string>? bukaFolder = null, AlatSerap? alat = null) : IHalaman
{
    static readonly HashSet<string> Singkatan = new(StringComparer.OrdinalIgnoreCase) { "ipa", "ips", "pjok", "ppkn", "pkn", "tik", "p5" };

    // Asisten AI (menyerap materi, tanya-jawab); tidak ada kalau platform tidak menyediakan alatnya.
    readonly HalamanSerap? serap = alat is null ? null : new HalamanSerap(buku, waktu, alat);
    readonly HalamanTanya? tanya = alat is null ? null : new HalamanTanya(buku, waktu, alat);

    public async Task<(string Judul, string Isi)> Buat(string uri, string? isiPost, Teks t)
    {
        var kueri = new Kueri(uri, isiPost);
        if (serap is not null && (kueri["ai"] is not null || kueri["serap"] is not null))
            return await serap.Buat(kueri, isiPost is not null, t);
        if (tanya is not null && kueri["tanya"] is not null)
            return tanya.Buat(kueri, isiPost is not null, t);

        var simpan = isiPost is not null && kueri["aksi"] == "simpan";
        var mapel = string.IsNullOrEmpty(kueri["m"]) ? null : kueri["m"];

        string? pesan = null;
        if (kueri["buka"] is not null && bukaFolder is not null && TokenSekali.Pakai(kueri["token"]))
        {
            Directory.CreateDirectory(buku.Folder);
            bukaFolder(buku.Folder);
            pesan = t["Folder catatan dibuka di pengelola berkas.", "The notes folder was opened in the file manager."];
        }

        return kueri["baru"] is not null ? Baru(t, kueri, mapel, simpan)
            : kueri["sumber"] is not null ? Sumber(t)
            : kueri["cari"] is { } kata ? Cari(t, kata)
            : kueri["c"] is { } nama ? SatuCatatan(t, kueri, mapel, nama, simpan)
            : mapel is not null ? DaftarMapel(t, mapel)
            : Daftar(t, pesan);
    }

    // Beranda Belajar: aksi utama, lalu satu kartu per mata pelajaran dengan
    // catatan terbarunya. Mata pelajaran dengan banyak catatan dibuka lengkap
    // di halamannya sendiri (?m=…), jadi beranda tetap pendek.
    (string, string) Daftar(Teks t, string? pesan)
    {
        var semua = buku.Semua();
        var isi = new StringBuilder(Kepala(t, t["Belajar", "Learn"], jejak: false, aksi: KotakCari(t, ""),
            keterangan: t["Catatan pelajaranmu, tersimpan sebagai berkas biasa di laptop ini.", "Your study notes, saved as ordinary files on this laptop."]));
        isi.Append(HalamanPengaturan.Pesan(pesan)).Append('\n')
            .Append($"""<nav class="aksi-belajar" aria-label="{t["Mulai", "Start"]}">""").Append('\n');
        if (tanya is not null)
            isi.Append(KartuAksi(HalamanBawaan.Belajar + "?tanya", Ikon.Tanya, t["Tanya AI", "Ask AI"],
                t["Belajar sambil mengobrol, dijelaskan pelan-pelan dari catatanmu.", "Learn by chatting, explained step by step from your notes."], true));
        if (serap is not null)
            isi.Append(KartuAksi(HalamanBawaan.Belajar + "?serap", Ikon.Serap, t["Serap materi", "Turn material into notes"],
                t["PDF atau teks pelajaran diolah jadi catatan per topik.", "A PDF or lesson text, turned into notes by topic."], false));
        isi.Append(KartuAksi(HalamanBawaan.Belajar + "?baru", Ikon.Tulis, t["Tulis catatan", "Write a note"],
            t["Catatan baru yang kamu ketik sendiri.", "A new note you type yourself."], tanya is null)).Append("</nav>\n");

        if (semua.Count == 0)
            isi.Append($"""
                <div class="kosong">
                <p><strong>{t["Belum ada catatan.", "No notes yet."]}</strong></p>
                <p>{t["Mulai dengan menyerap materi pelajaran atau menulis catatan sendiri. Berkas .md yang ditaruh di folder catatan, misalnya dari aplikasi lain, juga muncul di sini.",
                    "Start by turning study material into notes, or write one yourself. Any .md file placed in the notes folder, for example by another app, shows up here too."]}</p>
                </div>

                """);
        else
        {
            isi.Append($"""<h2 class="judul-bagian">{t["Mata pelajaran", "Subjects"]} <span>{Jumlah(t, semua.Count)}</span></h2>""")
                .Append("\n<div class=\"kisi-mapel\">\n");
            foreach (var kelompok in semua.GroupBy(c => c.Mapel).OrderBy(g => g.Key is null).ThenBy(g => NamaMapel(g.Key), StringComparer.OrdinalIgnoreCase))
                isi.Append(KartuMapel(t, kelompok.Key, [.. Urut(kelompok)]));
            isi.Append("</div>\n");
        }

        var folder = $"<code>{HtmlEncode(Tampilan(buku.Folder))}</code>";
        var tautan = bukaFolder is null ? ""
            : $""" · <a href="{HalamanBawaan.Belajar}?buka&amp;token={TokenSekali.Buat()}">{t["Buka foldernya", "Open the folder"]}</a>""";
        if (buku.AdaSumber)
            tautan += $""" · <a href="{HalamanBawaan.Belajar}?sumber">{t["Dokumen sumber", "Source documents"]}</a>""";
        if (serap is not null)
            tautan += $""" · <a href="{HalamanBawaan.Belajar}?ai">{t["Asisten AI", "AI assistant"]}</a>""";
        isi.Append($"""
            <p class="kaki-belajar">{Ikon.Folder}<span>{t[$"Tersimpan di {folder}, satu folder per mata pelajaran. Berkasnya bisa dibuka dengan penyunting teks apa saja.",
                $"Saved in {folder}, one folder per subject. The files open in any text editor."]}{tautan}</span></p>
            """);
        return (t["Belajar", "Learn"], isi.ToString());
    }

    static string KartuAksi(string alamat, string ikon, string judul, string keterangan, bool utama) => $"""
        <a class="kartu-aksi{(utama ? " utama" : "")}" href="{alamat}"><span class="ikon-kotak">{ikon}</span><span><strong>{judul}</strong><small>{keterangan}</small></span></a>

        """;

    const int KartuMaks = 5;

    // Satu mata pelajaran di beranda: catatan terbaru, paling banyak KartuMaks.
    string KartuMapel(Teks t, string? mapel, List<Catatan> catatan)
    {
        var nama = mapel is null ? t["Tanpa mata pelajaran", "No subject"] : NamaMapel(mapel);
        var judul = mapel is null ? HtmlEncode(nama) : $"""<a href="{HtmlEncode(AlamatMapel(mapel))}">{HtmlEncode(nama)}</a>""";
        var isi = new StringBuilder($"""
            <section class="kartu-mapel {Warna(mapel)}">
            <header>{Lencana(mapel)}<div><h3>{judul}</h3><span class="jumlah">{Jumlah(t, catatan.Count)}</span></div></header>
            <ul>

            """);
        // Tanpa mata pelajaran tidak punya halaman sendiri, jadi semuanya tampil di sini.
        foreach (var c in mapel is null ? catatan : catatan.Take(KartuMaks))
            isi.Append(BarisCatatan(t, c)).Append('\n');
        isi.Append("</ul>\n");
        if (mapel is not null)
            isi.Append("<footer>")
                .Append(catatan.Count > KartuMaks
                    ? $"""<a href="{HtmlEncode(AlamatMapel(mapel))}">{t[$"Semua {catatan.Count} catatan", $"All {catatan.Count} notes"]}</a>"""
                    : "")
                .Append($"""<a class="tambah" href="{HtmlEncode(AlamatBaru(mapel))}">+ {t["Tulis catatan", "Write a note"]}</a></footer>""")
                .Append('\n');
        return isi.Append("</section>\n").ToString();
    }

    string BarisCatatan(Teks t, Catatan c) =>
        $"""<li><a href="{HtmlEncode(Alamat(c))}"><span>{HtmlEncode(c.Judul)}</span><time>{Tanggal(t, c.Diubah)}</time></a></li>""";

    // Tanggal di daftar: tanpa tahun kalau tahun ini.
    string Tanggal(Teks t, DateTime diubah) => t.TanggalSingkat(diubah, diubah.Year != waktu.GetLocalNow().Year);

    // ?m=fisika: semua catatan satu mata pelajaran.
    (string, string) DaftarMapel(Teks t, string mapel)
    {
        var catatan = Urut(buku.Semua().Where(c => c.Mapel == mapel)).ToList();
        if (catatan.Count == 0)
            return TidakAda(t);
        var nama = NamaMapel(mapel);
        var aksi = $"""<a class="tombol utama" href="{HtmlEncode(AlamatBaru(mapel))}">{Ikon.Tulis}{t["Tulis catatan", "Write a note"]}</a>""";
        if (tanya is not null)
            aksi += $"""<a class="tombol" href="{HalamanBawaan.Belajar}?tanya">{Ikon.Tanya}{t["Tanya AI", "Ask AI"]}</a>""";
        var isi = new StringBuilder(Kepala(t, Lencana(mapel) + HtmlEncode(nama), keterangan: Jumlah(t, catatan.Count), aksi: aksi, kelas: Warna(mapel)));
        isi.Append("<ul class=\"daftar-catatan\">\n");
        foreach (var c in catatan)
            isi.Append(BarisCatatan(t, c)).Append('\n');
        return (nama, isi.Append("</ul>\n").ToString());
    }

    (string, string) SatuCatatan(Teks t, Kueri kueri, string? mapel, string nama, bool simpan)
    {
        if (!BukuCatatan.NamaAman(nama) || (mapel is not null && !BukuCatatan.NamaAman(mapel)))
            return TidakAda(t);
        if (simpan)
            return SimpanSuntingan(t, kueri, mapel, nama);
        if (buku.Ambil(mapel, nama) is not { } c || buku.Baca(mapel, nama) is not { } isi)
            return TidakAda(t);
        if (kueri["sunting"] is not null)
            return Sunting(t, c, isi, buku.Sidik(mapel, nama), null);

        var pesan = kueri["disimpan"] is not null ? t["Catatan disimpan.", "Note saved."]
            : kueri["dibuat"] is not null ? t["Catatan baru disimpan.", "New note saved."]
            : null;
        return Lihat(t, c, isi, pesan);
    }

    (string, string) Lihat(Teks t, Catatan c, string isi, string? pesan)
    {
        var penaut = buku.Penaut(c.Mapel);
        var (sumber, teks) = PisahSumber(TanpaJudul(isi));
        var badan = Markah.KeHtml(teks, sasaran => penaut(sasaran) is { } tujuan ? (Alamat(tujuan), tujuan.Judul) : null, geserJudul: 1);
        var jalur = Tampilan(Path.Combine(buku.Folder, c.Mapel ?? "", c.Nama + ".md"));

        var meta = new StringBuilder();
        if (c.Mapel is not null)
            meta.Append($"""<a class="chip-mapel {Warna(c.Mapel)}" href="{HtmlEncode(AlamatMapel(c.Mapel))}">{HtmlEncode(NamaMapel(c.Mapel))}</a>""");
        meta.Append($"""<span>{t["Diubah", "Changed"]} {t.TanggalSingkat(c.Diubah)}</span>""");
        if (sumber is not null)
            meta.Append($"""<span>{t["Sumber:", "Source:"]} {HtmlEncode(sumber)}</span>""");

        var samping = new StringBuilder();
        if (tanya is not null)
            samping.Append($"""<a class="tombol utama" href="{HtmlEncode(HalamanTanya.AlamatTentang(c))}">{Ikon.Tanya}{t["Tanya tentang catatan ini", "Ask about this note"]}</a>""").Append('\n');
        samping.Append($"""<a class="tombol" href="{HtmlEncode(Alamat(c) + "&sunting")}">{Ikon.Tulis}{t["Sunting", "Edit"]}</a>""").Append('\n');
        if (c.Mapel is not null)
        {
            samping.Append($"""<h2>{HtmlEncode(NamaMapel(c.Mapel))}</h2>""").Append("\n<ul class=\"catatan-lain\">\n");
            foreach (var lain in Urut(buku.Semua().Where(x => x.Mapel == c.Mapel)).Take(15))
                samping.Append($"""<li><a href="{HtmlEncode(Alamat(lain))}"{(lain.Nama == c.Nama ? " aria-current=\"page\"" : "")}>{HtmlEncode(lain.Judul)}</a></li>""").Append('\n');
            samping.Append("</ul>\n");
        }
        samping.Append($"""<p class="berkas">{t["Berkas", "File"]} <code>{HtmlEncode(jalur)}</code></p>""");

        return (c.Judul, $"""
            <div class="tata-catatan">
            <article class="kertas">
            {Jejak(t, c.Mapel)}
            <h1>{HtmlEncode(c.Judul)}</h1>
            <p class="meta">{meta}</p>
            {HalamanPengaturan.Pesan(pesan)}
            <div class="isi-catatan">
            {badan}</div>
            </article>
            <aside class="samping">
            {samping}
            </aside>
            </div>
            """);
    }

    // Baris "Sumber: …" paling atas (ditulis waktu materi diserap) jadi
    // keterangan di bawah judul, bukan paragraf pertama.
    static (string? Sumber, string Isi) PisahSumber(string isi)
    {
        var baris = isi.Split('\n').ToList();
        var letak = baris.FindIndex(b => b.Trim().Length > 0);
        if (letak < 0 || !baris[letak].TrimStart().StartsWith("Sumber:", StringComparison.OrdinalIgnoreCase))
            return (null, isi);
        var sumber = baris[letak].Trim()["Sumber:".Length..].Trim();
        baris.RemoveAt(letak);
        return (sumber.Length == 0 ? null : sumber, string.Join('\n', baris));
    }

    (string, string) Sunting(Teks t, Catatan c, string isi, string sidik, string? pesan)
    {
        var judul = t["Sunting catatan", "Edit note"];
        return (judul, $"""
            {Kepala(t, judul, c.Mapel)}
            {HalamanPengaturan.Pesan(pesan)}
            <form class="tulis" action="{HtmlEncode(Alamat(c))}" method="post">
              <input type="hidden" name="aksi" value="simpan">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <input type="hidden" name="sidik" value="{HtmlEncode(sidik)}">
              <label for="isi">{HtmlEncode(c.Judul)}</label>
              <textarea id="isi" name="isi" rows="22" spellcheck="false">
            {HtmlEncode(isi)}</textarea>
              {PetunjukFormat(t)}
              <p class="tombol-tombol"><button class="tombol utama" type="submit">{t["Simpan", "Save"]}</button>
              <a class="tombol" href="{HtmlEncode(Alamat(c))}">{t["Batal", "Cancel"]}</a></p>
            </form>
            """);
    }

    (string, string) SimpanSuntingan(Teks t, Kueri kueri, string? mapel, string nama)
    {
        var isi = kueri["isi"] ?? "";
        var sidik = kueri["sidik"] ?? "";
        var c = buku.Ambil(mapel, nama) ?? new Catatan(mapel, nama, BukuCatatan.JudulDariNama(nama), waktu.GetLocalNow().DateTime);
        if (!TokenSekali.Pakai(kueri["token"]))
            return Sunting(t, c, isi, sidik, t[
                "Permintaan ini sudah dipakai atau kedaluwarsa, jadi catatannya belum disimpan. Periksa teks di bawah, lalu simpan lagi.",
                "This request was already used or has expired, so the note was not saved. Check the text below, then save again."]);

        return buku.Simpan(mapel, nama, isi, sidik) switch
        {
            HasilSimpan.Tersimpan => Pindah(t, Alamat(c) + "&disimpan"),
            HasilSimpan.BerubahDiDisk => Sunting(t, c, isi, buku.Sidik(mapel, nama), t[
                "Berkas ini berubah sejak dibuka, mungkin disunting aplikasi lain. Teks di bawah belum disimpan. Simpan lagi untuk menimpa versi di disk.",
                "This file changed after it was opened, maybe in another app. The text below was not saved yet. Save again to replace the version on disk."]),
            _ => Sunting(t, c, isi, sidik, t["Catatan ini tidak bisa disimpan.", "This note could not be saved."]),
        };
    }

    (string, string) Baru(Teks t, Kueri kueri, string? mapel, bool simpan)
    {
        var terpilih = kueri["mapel"] ?? mapel ?? "";
        var mapelBaru = kueri["mapel-baru"] ?? "";
        var judul = kueri["judul"] ?? "";
        var isi = kueri["isi"] ?? "";
        if (!simpan)
            return FormBaru(t, terpilih, "", "", "", null);
        if (!TokenSekali.Pakai(kueri["token"]))
            return FormBaru(t, terpilih, mapelBaru, judul, isi, t[
                "Permintaan ini sudah dipakai atau kedaluwarsa, jadi catatannya belum disimpan. Periksa teks di bawah, lalu simpan lagi.",
                "This request was already used or has expired, so the note was not saved. Check the text below, then save again."]);

        var namaMapel = mapelBaru.Trim().Length > 0 ? mapelBaru : terpilih;
        if (namaMapel.Trim().Length == 0)
            return FormBaru(t, terpilih, mapelBaru, judul, isi, t["Pilih atau tulis dulu mata pelajarannya.", "Choose or type the subject first."]);
        if (judul.Trim().Length == 0)
            return FormBaru(t, terpilih, mapelBaru, judul, isi, t["Judulnya belum diisi.", "The title is empty."]);
        if (buku.Tulis(namaMapel, judul, isi, waktu.GetLocalNow().DateTime) is not { } c)
            return FormBaru(t, terpilih, mapelBaru, judul, isi, t[
                "Catatan ini tidak bisa disimpan. Coba nama mata pelajaran atau judul lain.",
                "This note could not be saved. Try another subject name or title."]);
        return Pindah(t, Alamat(c) + "&dibuat");
    }

    (string, string) FormBaru(Teks t, string terpilih, string mapelBaru, string judul, string isi, string? pesan)
    {
        var judulHalaman = t["Tulis catatan", "Write a note"];
        return (judulHalaman, $"""
            {Kepala(t, judulHalaman)}
            {HalamanPengaturan.Pesan(pesan)}
            <form class="tulis" action="{HalamanBawaan.Belajar}?baru" method="post">
              <input type="hidden" name="aksi" value="simpan">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              {PilihanMapel(t, buku, terpilih, mapelBaru)}
              <label>{t["Judul", "Title"]} <input type="text" name="judul" value="{HtmlEncode(judul)}" required></label>
              <label for="isi">{t["Isi", "Text"]}</label>
              <textarea id="isi" name="isi" rows="18" spellcheck="false">
            {HtmlEncode(isi)}</textarea>
              {PetunjukFormat(t)}
              <p class="tombol-tombol"><button class="tombol utama" type="submit">{t["Simpan", "Save"]}</button>
              <a class="tombol" href="{HalamanBawaan.Belajar}">{t["Batal", "Cancel"]}</a></p>
            </form>
            """);
    }

    (string, string) Cari(Teks t, string kata)
    {
        var judul = t["Cari di catatan", "Search notes"];
        var isi = new StringBuilder(Kepala(t, judul, aksi: KotakCari(t, kata)));
        var bagian = kata.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var hasil = buku.Cari(kata);
        if (bagian.Length == 0)
            isi.Append($"<p>{t["Ketik kata yang dicari.", "Type a word to search for."]}</p>");
        else if (hasil.Count == 0)
            isi.Append($"<p>{t[$"Tidak ada catatan yang memuat “{HtmlEncode(kata)}”.", $"No notes contain “{HtmlEncode(kata)}”."]}</p>");
        else
        {
            isi.Append($"""<p class="catatan">{Jumlah(t, hasil.Count)}</p>""").Append("\n<ul class=\"hasil-cari\">\n");
            foreach (var h in hasil)
            {
                var mapel = h.Catatan.Mapel is null ? t["Tanpa mata pelajaran", "No subject"] : NamaMapel(h.Catatan.Mapel);
                isi.Append($"""<li><a href="{HtmlEncode(Alamat(h.Catatan))}">{Sorot(h.Catatan.Judul, bagian)}</a> <span class="catatan">· {HtmlEncode(mapel)}</span>""")
                    .Append($"""<br><span class="cuplikan">{Sorot(h.Cuplikan, bagian)}</span></li>""").Append('\n');
            }
            isi.Append("</ul>\n");
        }
        return (judul, isi.ToString());
    }

    (string, string) Sumber(Teks t)
    {
        var judul = t["Dokumen sumber", "Source documents"];
        var isi = buku.BacaSumber();
        return (judul, $"""
            {Kepala(t, judul, keterangan: t["Dokumen yang sudah diolah jadi catatan.", "Documents already turned into notes."])}
            {(isi is null ? $"<p>{t["Belum ada.", "None yet."]}</p>" : $"<article class=\"isi-catatan\">\n{Markah.KeHtml(TanpaJudul(isi), geserJudul: 1)}</article>")}
            """);
    }

    // Halaman sesudah formulir tersimpan: langsung pindah ke alamat catatan,
    // tanpa JavaScript. Penundaan 0 membuat WebKit mengganti entri riwayat
    // POST ini, bukan menambah entri baru.
    internal static (string, string) Pindah(Teks t, string alamat, string? judul = null)
    {
        judul ??= t["Tersimpan", "Saved"];
        return (judul, $"""
            <meta http-equiv="refresh" content="0; url={HtmlEncode(alamat)}">
            <h1>{judul}</h1>
            <p><a href="{HtmlEncode(alamat)}">{t["Lanjut", "Continue"]}</a></p>
            """);
    }

    /// <summary>Pilih mata pelajaran yang sudah ada, atau tulis yang baru (isian "mapel" dan "mapel-baru").</summary>
    internal static string PilihanMapel(Teks t, BukuCatatan buku, string terpilih, string mapelBaru)
    {
        var semuaMapel = buku.Mapel();
        var pilihan = semuaMapel.Count == 0 ? "" : $"""
            <label>{t["Mata pelajaran", "Subject"]}
            <select name="mapel"><option value="">–</option>{string.Concat(semuaMapel.Select(m =>
                $"""<option value="{HtmlEncode(m)}"{(m.Equals(terpilih, StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>{HtmlEncode(NamaMapel(m))}</option>"""))}</select></label>
            """;
        return $"""
            <div class="baris">{pilihan}
            <label>{(semuaMapel.Count == 0 ? t["Mata pelajaran", "Subject"] : t["atau yang baru", "or a new one"])}
            <input type="text" name="mapel-baru" value="{HtmlEncode(mapelBaru)}" placeholder="{t["mis. Kimia", "e.g. Chemistry"]}"></label></div>
            """;
    }

    static (string, string) TidakAda(Teks t)
    {
        var judul = t["Catatan tidak ditemukan", "Note not found"];
        return (judul, $"""
            {Kepala(t, judul)}
            <p>{t["Mungkin sudah dipindah atau dihapus.", "It may have been moved or deleted."]}</p>
            <p><a href="{HalamanBawaan.Belajar}">{t["Semua catatan", "All notes"]}</a></p>
            """);
    }

    // Satu isian tanpa tombol: Enter mengirimnya (tanpa JavaScript).
    static string KotakCari(Teks t, string kata) =>
        $"""<form class="cari-catatan" action="{HalamanBawaan.Belajar}" method="get" role="search">{Ikon.Cari}<input type="search" name="cari" value="{HtmlEncode(kata)}" placeholder="{t["Cari di catatan", "Search notes"]}" aria-label="{t["Cari di catatan", "Search notes"]}"></form>""";

    internal static string Jejak(Teks t, string? mapel) =>
        $"""<p class="jejak"><a href="{HalamanBawaan.Belajar}">{t["Belajar", "Learn"]}</a>{(mapel is null ? ""
            : $""" › <a href="{HtmlEncode(AlamatMapel(mapel))}">{HtmlEncode(NamaMapel(mapel))}</a>""")}</p>""";

    /// <summary>
    /// Kepala halaman Belajar: jejak, judul (HTML), keterangan, dan aksi di
    /// kanan (tombol, kotak cari).
    /// </summary>
    internal static string Kepala(Teks t, string judul, string? mapel = null, string? keterangan = null, string aksi = "", bool jejak = true, string kelas = "") =>
        $"""
        <header class="kepala-belajar{(kelas.Length == 0 ? "" : " " + kelas)}">
          <div>{(jejak ? Jejak(t, mapel) : "")}<h1>{judul}</h1>{(keterangan is null ? "" : $"<p class=\"keterangan\">{keterangan}</p>")}</div>{(aksi.Length == 0 ? "" : $"\n  <div class=\"aksi\">{aksi}</div>")}
        </header>
        """;

    // "Bahasa Indonesia" → "BI", "Fisika" → "F", "IPA" → "IPA".
    static string Lencana(string? mapel)
    {
        var kata = NamaMapel(mapel).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var huruf = kata.Length == 0 ? "•"
            : kata.Length > 1 ? $"{kata[0][0]}{kata[1][0]}"
            : Singkatan.Contains(kata[0]) ? kata[0]
            : kata[0][..1];
        return $"""<span class="lencana-mapel" aria-hidden="true">{HtmlEncode(huruf.ToUpperInvariant())}</span>""";
    }

    // Warna tetap per mata pelajaran, dihitung dari namanya; string.GetHashCode
    // tidak bisa dipakai karena berubah setiap proses.
    static string Warna(string? mapel)
    {
        var h = 0u;
        foreach (var ch in mapel ?? "")
            h = h * 31 + ch;
        return $"warna-{h % 8}";
    }

    static string PetunjukFormat(Teks t) =>
        t[
            """<p class="catatan">Format Markdown: <code># Judul</code>, <code>## Subjudul</code>, <code>- daftar</code>, <code>**tebal**</code>, dan <code>[[nama-catatan]]</code> untuk menautkan catatan lain.</p>""",
            """<p class="catatan">Markdown format: <code># Heading</code>, <code>## Subheading</code>, <code>- list</code>, <code>**bold**</code>, and <code>[[note-name]]</code> to link another note.</p>"""];

    static string Jumlah(Teks t, int n) => t[$"{n} catatan", n == 1 ? "1 note" : $"{n} notes"];

    internal static string Alamat(Catatan c) =>
        HalamanBawaan.Belajar + "?" + (c.Mapel is null ? "" : $"m={Uri.EscapeDataString(c.Mapel)}&") + $"c={Uri.EscapeDataString(c.Nama)}";

    static string AlamatMapel(string mapel) => $"{HalamanBawaan.Belajar}?m={Uri.EscapeDataString(mapel)}";

    static string AlamatBaru(string mapel) => $"{HalamanBawaan.Belajar}?baru&m={Uri.EscapeDataString(mapel)}";

    // Yang terbaru dulu (awalan tanggal nama berkas), lalu menurut judul.
    static IEnumerable<Catatan> Urut(IEnumerable<Catatan> catatan) =>
        catatan.OrderByDescending(c => BukuCatatan.AwalanTanggal(c.Nama), StringComparer.Ordinal)
            .ThenBy(c => c.Judul, StringComparer.OrdinalIgnoreCase);

    /// <summary>"bahasa-indonesia" → "Bahasa Indonesia", "ipa" → "IPA".</summary>
    internal static string NamaMapel(string? folder)
    {
        var kata = (folder ?? "").Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return kata.Length == 0 ? folder ?? ""
            : string.Join(' ', kata.Select(k => Singkatan.Contains(k) ? k.ToUpperInvariant() : char.ToUpperInvariant(k[0]) + k[1..]));
    }

    // Judul "# …" sudah jadi <h1> halaman, jadi dibuang dari isinya.
    static string TanpaJudul(string isi)
    {
        var baris = isi.Replace("\r\n", "\n").Split('\n').ToList();
        var letak = baris.Take(40).ToList().FindIndex(b => BukuCatatan.JudulBaris(b) is not null);
        if (letak >= 0)
            baris.RemoveAt(letak);
        return string.Join('\n', baris);
    }

    // "/home/kevin/kevin-catatan" → "~/kevin-catatan"
    internal static string Tampilan(string jalur)
    {
        var rumah = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return rumah.Length > 1 && (jalur == rumah || jalur.StartsWith(rumah + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            ? "~" + jalur[rumah.Length..]
            : jalur;
    }

    // Teks di-escape, kata yang dicari ditandai <mark>.
    static string Sorot(string teks, string[] kata)
    {
        var tanda = new bool[teks.Length];
        foreach (var k in kata)
            for (var i = teks.IndexOf(k, StringComparison.OrdinalIgnoreCase); i >= 0; i = teks.IndexOf(k, i + k.Length, StringComparison.OrdinalIgnoreCase))
                Array.Fill(tanda, true, i, Math.Min(k.Length, teks.Length - i));
        var hasil = new StringBuilder();
        for (var i = 0; i < teks.Length;)
        {
            var j = i;
            while (j < teks.Length && tanda[j] == tanda[i])
                j++;
            var potongan = HtmlEncode(teks[i..j]);
            hasil.Append(tanda[i] ? $"<mark>{potongan}</mark>" : potongan);
            i = j;
        }
        return hasil.ToString();
    }
}
