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
public sealed class HalamanBelajar(BukuCatatan buku, TimeProvider waktu, Action<string>? bukaFolder = null) : IHalaman
{
    static readonly HashSet<string> Singkatan = new(StringComparer.OrdinalIgnoreCase) { "ipa", "ips", "pjok", "ppkn", "pkn", "tik", "p5" };

    public Task<(string Judul, string Isi)> Buat(string uri, string? isiPost, Teks t)
    {
        var kueri = new Kueri(uri, isiPost);
        var simpan = isiPost is not null && kueri["aksi"] == "simpan";
        var mapel = string.IsNullOrEmpty(kueri["m"]) ? null : kueri["m"];

        string? pesan = null;
        if (kueri["buka"] is not null && bukaFolder is not null && TokenSekali.Pakai(kueri["token"]))
        {
            Directory.CreateDirectory(buku.Folder);
            bukaFolder(buku.Folder);
            pesan = t["Folder catatan dibuka di pengelola berkas.", "The notes folder was opened in the file manager."];
        }

        var hasil = kueri["baru"] is not null ? Baru(t, kueri, mapel, simpan)
            : kueri["sumber"] is not null ? Sumber(t)
            : kueri["cari"] is { } kata ? Cari(t, kata)
            : kueri["c"] is { } nama ? SatuCatatan(t, kueri, mapel, nama, simpan)
            : Daftar(t, pesan);
        return Task.FromResult(hasil);
    }

    (string, string) Daftar(Teks t, string? pesan)
    {
        var semua = buku.Semua();
        var isi = new StringBuilder($"""
            <h1>{t["Belajar", "Learn"]}</h1>
            <p class="pembuka">{t["Catatan pelajaran, tersimpan sebagai berkas biasa di laptop ini.",
                "Study notes, saved as ordinary files on this laptop."]}</p>
            {HalamanPengaturan.Pesan(pesan)}
            {KotakCari(t, "")}
            <p class="tombol-tombol"><a class="tombol utama" href="{HalamanBawaan.Belajar}?baru">{t["Tulis catatan", "Write a note"]}</a>{(buku.AdaSumber
                ? $""" <a class="tombol" href="{HalamanBawaan.Belajar}?sumber">{t["Dokumen sumber", "Source documents"]}</a>"""
                : "")}</p>

            """);

        if (semua.Count == 0)
            isi.Append($"""
                <p>{t["Belum ada catatan. Tulis yang pertama dengan tombol di atas. Berkas .md yang ditaruh di folder catatan, misalnya dari aplikasi lain, juga muncul di sini.",
                    "No notes yet. Write the first one with the button above. Any .md file placed in the notes folder, for example by another app, shows up here too."]}</p>

                """);

        // Banyak catatan: daftarnya tertutup, cukup nama mata pelajarannya.
        var terbuka = semua.Count <= 40 ? " open" : "";
        foreach (var kelompok in semua.GroupBy(c => c.Mapel).OrderBy(g => g.Key is null).ThenBy(g => NamaMapel(g.Key), StringComparer.OrdinalIgnoreCase))
        {
            var nama = kelompok.Key is null ? t["Tanpa mata pelajaran", "No subject"] : NamaMapel(kelompok.Key);
            isi.Append($"""
                <details class="mapel"{terbuka}>
                <summary><strong>{HtmlEncode(nama)}</strong> <span class="catatan">{Jumlah(t, kelompok.Count())}</span></summary>
                <ul class="daftar-catatan">

                """);
            foreach (var c in Urut(kelompok))
                isi.Append($"""<li><a href="{HtmlEncode(Alamat(c))}">{HtmlEncode(c.Judul)}</a></li>""").Append('\n');
            if (kelompok.Key is not null)
                isi.Append($"""<li class="tambah"><a href="{HtmlEncode($"{HalamanBawaan.Belajar}?baru&m={Uri.EscapeDataString(kelompok.Key)}")}">+ {t["Tulis catatan", "Write a note"]}</a></li>""").Append('\n');
            isi.Append("</ul>\n</details>\n");
        }

        var folder = $"<code>{HtmlEncode(Tampilan(buku.Folder))}</code>";
        var buka = bukaFolder is null ? ""
            : $""" · <a href="{HalamanBawaan.Belajar}?buka&amp;token={TokenSekali.Buat()}">{t["Buka foldernya", "Open the folder"]}</a>""";
        isi.Append($"""
            <p class="catatan">{t[$"Tersimpan di {folder}, satu folder per mata pelajaran. Berkasnya bisa dibuka dengan penyunting teks apa saja.",
                $"Saved in {folder}, one folder per subject. The files open in any text editor."]}{buka}</p>
            """);
        return (t["Belajar", "Learn"], isi.ToString());
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
        var badan = Markah.KeHtml(TanpaJudul(isi), sasaran => penaut(sasaran) is { } tujuan ? Alamat(tujuan) : null, geserJudul: 1);
        var jalur = Tampilan(Path.Combine(buku.Folder, c.Mapel ?? "", c.Nama + ".md"));
        return (c.Judul, $"""
            {Jejak(t, c.Mapel)}
            <h1>{HtmlEncode(c.Judul)}</h1>
            {HalamanPengaturan.Pesan(pesan)}
            <article class="isi-catatan">
            {badan}</article>
            <p class="tombol-tombol"><a class="tombol" href="{HtmlEncode(Alamat(c) + "&sunting")}">{t["Sunting", "Edit"]}</a></p>
            <p class="catatan">{t["Berkas", "File"]} <code>{HtmlEncode(jalur)}</code> · {t["diubah", "changed"]} {t.Tanggal(c.Diubah)}</p>
            """);
    }

    (string, string) Sunting(Teks t, Catatan c, string isi, string sidik, string? pesan)
    {
        var judul = t["Sunting catatan", "Edit note"];
        return (judul, $"""
            {Jejak(t, c.Mapel)}
            <h1>{judul}</h1>
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
        var semuaMapel = buku.Mapel();
        var pilihan = semuaMapel.Count == 0 ? "" : $"""
            <label>{t["Mata pelajaran", "Subject"]}
            <select name="mapel"><option value="">–</option>{string.Concat(semuaMapel.Select(m =>
                $"""<option value="{HtmlEncode(m)}"{(m.Equals(terpilih, StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>{HtmlEncode(NamaMapel(m))}</option>"""))}</select></label>
            """;
        var judulHalaman = t["Tulis catatan", "Write a note"];
        return (judulHalaman, $"""
            {Jejak(t, null)}
            <h1>{judulHalaman}</h1>
            {HalamanPengaturan.Pesan(pesan)}
            <form class="tulis" action="{HalamanBawaan.Belajar}?baru" method="post">
              <input type="hidden" name="aksi" value="simpan">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <div class="baris">{pilihan}
              <label>{(semuaMapel.Count == 0 ? t["Mata pelajaran", "Subject"] : t["atau yang baru", "or a new one"])}
              <input type="text" name="mapel-baru" value="{HtmlEncode(mapelBaru)}" placeholder="{t["mis. Kimia", "e.g. Chemistry"]}"></label></div>
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
        var isi = new StringBuilder($"""
            {Jejak(t, null)}
            <h1>{judul}</h1>
            {KotakCari(t, kata)}

            """);
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
            {Jejak(t, null)}
            <h1>{judul}</h1>
            <p class="pembuka">{t["Dokumen yang sudah diolah jadi catatan.", "Documents already turned into notes."]}</p>
            {(isi is null ? $"<p>{t["Belum ada.", "None yet."]}</p>" : $"<article class=\"isi-catatan\">\n{Markah.KeHtml(TanpaJudul(isi), geserJudul: 1)}</article>")}
            """);
    }

    // Halaman sesudah formulir tersimpan: langsung pindah ke alamat catatan,
    // tanpa JavaScript. Penundaan 0 membuat WebKit mengganti entri riwayat
    // POST ini, bukan menambah entri baru.
    static (string, string) Pindah(Teks t, string alamat)
    {
        var judul = t["Tersimpan", "Saved"];
        return (judul, $"""
            <meta http-equiv="refresh" content="0; url={HtmlEncode(alamat)}">
            <h1>{judul}</h1>
            <p><a href="{HtmlEncode(alamat)}">{t["Buka catatannya", "Open the note"]}</a></p>
            """);
    }

    static (string, string) TidakAda(Teks t)
    {
        var judul = t["Catatan tidak ditemukan", "Note not found"];
        return (judul, $"""
            <h1>{judul}</h1>
            <p>{t["Mungkin sudah dipindah atau dihapus.", "It may have been moved or deleted."]}</p>
            <p><a href="{HalamanBawaan.Belajar}">{t["Semua catatan", "All notes"]}</a></p>
            """);
    }

    static string KotakCari(Teks t, string kata) =>
        $"""
        <form class="cari kiri" action="{HalamanBawaan.Belajar}" method="get" role="search">
          <input type="search" name="cari" value="{HtmlEncode(kata)}" placeholder="{t["Cari di catatan", "Search notes"]}" aria-label="{t["Cari di catatan", "Search notes"]}">
          <button type="submit">{t["Cari", "Search"]}</button>
        </form>
        """;

    static string Jejak(Teks t, string? mapel) =>
        $"""<p class="jejak"><a href="{HalamanBawaan.Belajar}">{t["Belajar", "Learn"]}</a>{(mapel is null ? "" : $" › {HtmlEncode(NamaMapel(mapel))}")}</p>""";

    static string PetunjukFormat(Teks t) =>
        t[
            """<p class="catatan">Format Markdown: <code># Judul</code>, <code>## Subjudul</code>, <code>- daftar</code>, <code>**tebal**</code>, dan <code>[[nama-catatan]]</code> untuk menautkan catatan lain.</p>""",
            """<p class="catatan">Markdown format: <code># Heading</code>, <code>## Subheading</code>, <code>- list</code>, <code>**bold**</code>, and <code>[[note-name]]</code> to link another note.</p>"""];

    static string Jumlah(Teks t, int n) => t[$"{n} catatan", n == 1 ? "1 note" : $"{n} notes"];

    static string Alamat(Catatan c) =>
        HalamanBawaan.Belajar + "?" + (c.Mapel is null ? "" : $"m={Uri.EscapeDataString(c.Mapel)}&") + $"c={Uri.EscapeDataString(c.Nama)}";

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
    static string Tampilan(string jalur)
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
