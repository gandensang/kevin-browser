using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// kevin://belajar?tanya: tanya-jawab tentang pelajaran, dijawab AI dari
/// catatan siswa (<see cref="Penanya"/>). <c>?tanya</c> memulai obrolan, boleh
/// tentang satu catatan (<c>&amp;m=…&amp;c=…</c>, tombol "Tanya tentang
/// catatan ini"). <c>?tanya&amp;obrolan=ID</c> menampilkan obrolannya dan
/// memperbarui dirinya sendiri (meta refresh, tanpa JavaScript) selama AI
/// masih menjawab. Pertanyaan dikirim lewat POST bertoken sekali pakai, lalu
/// halamannya pindah ke alamat obrolan, jadi muat ulang tidak bertanya lagi.
/// </summary>
sealed class HalamanTanya(BukuCatatan buku, TimeProvider waktu, AlatSerap alat)
{
    const string Alamat = HalamanBawaan.Belajar + "?tanya";

    readonly Penanya penanya = new(buku, alat.Pengaturan, new KlienAi(alat.Jaringan), waktu);

    PengaturanAi Pengaturan => alat.Pengaturan;

    /// <summary>"Tanya tentang catatan ini".</summary>
    internal static string AlamatTentang(Catatan c) =>
        Alamat + (c.Mapel is null ? "" : $"&m={Uri.EscapeDataString(c.Mapel)}") + $"&c={Uri.EscapeDataString(c.Nama)}";

    static string AlamatObrolan(Obrolan o) => $"{Alamat}&obrolan={o.Id}";

    public (string Judul, string Isi) Buat(Kueri kueri, bool post, Teks t) =>
        kueri["obrolan"] is { } id ? Obrolan(t, kueri, id, post) : Mulai(t, kueri, post);

    // ---------- ?tanya: pertanyaan pertama ----------

    (string, string) Mulai(Teks t, Kueri kueri, bool post)
    {
        var judul = t["Tanya", "Ask"];
        var mapel = string.IsNullOrEmpty(kueri["m"]) ? null : kueri["m"];
        var lampiran = kueri["c"] is { } nama ? buku.Ambil(mapel, nama) : null;
        var pertanyaan = kueri["pertanyaan"] ?? "";
        string? pesan = null;
        if (post && kueri["aksi"] == "tanya")
        {
            pesan = Masalah(t, kueri, pertanyaan);
            if (pesan is null)
            {
                var obrolan = penanya.Baru(lampiran);
                penanya.Tanya(obrolan, Rapikan(pertanyaan), t);
                return HalamanBelajar.Pindah(t, AlamatObrolan(obrolan) + "#akhir", judul);
            }
        }

        return (judul, $"""
            {HalamanBelajar.Jejak(t, null)}
            <h1>{judul}</h1>
            <p class="pembuka">{t["Tanya apa saja tentang pelajaranmu. AI mencari jawabannya di catatanmu dulu, lalu menyebut catatan yang dipakainya.",
                "Ask anything about your lessons. The AI looks for the answer in your notes first, and names the notes it used."]}</p>
            {HalamanSerap.TanpaKunci(t, Pengaturan)}
            {Tentang(t, lampiran)}
            {HalamanPengaturan.Pesan(pesan)}
            {Formulir(t, lampiran is null ? Alamat : AlamatTentang(lampiran), pertanyaan, true)}
            {Kaki(t, Pengaturan.ModelTanya)}
            """);
    }

    // Kenapa pertanyaannya belum bisa dikirim; null kalau bisa (tokennya lalu terpakai).
    string? Masalah(Teks t, Kueri kueri, string pertanyaan) =>
        Pengaturan.Kunci is null ? t["Belum ada kunci API. Atur dulu di halaman Asisten AI.", "There's no API key yet. Set one on the AI assistant page first."]
        : pertanyaan.Trim().Length == 0 ? t["Tulis dulu pertanyaannya.", "Type the question first."]
        : pertanyaan.Length > Penanya.MaksHurufPertanyaan
            ? t[$"Pertanyaannya terlalu panjang: {t.Angka(pertanyaan.Length)} huruf, batasnya {t.Angka(Penanya.MaksHurufPertanyaan)}. Materi yang panjang lebih baik diserap dulu jadi catatan.",
                $"The question is too long: {t.Angka(pertanyaan.Length)} characters, the limit is {t.Angka(Penanya.MaksHurufPertanyaan)}. Long material is better turned into notes first."]
        : !TokenSekali.Pakai(kueri["token"])
            ? t["Permintaan ini sudah dipakai atau kedaluwarsa. Periksa pertanyaannya, lalu tekan Tanya lagi.",
                "This request was already used or has expired. Check the question, then press Ask again."]
        : null;

    // ---------- ?tanya&obrolan=…: obrolan dan pertanyaan lanjutan ----------

    (string, string) Obrolan(Teks t, Kueri kueri, string id, bool post)
    {
        var judul = t["Tanya", "Ask"];
        if (penanya.Ambil(id) is not { } obrolan)
            return (judul, $"""
                {HalamanBelajar.Jejak(t, null)}
                <h1>{judul}</h1>
                {HalamanPengaturan.Pesan(t["Obrolan ini tidak ditemukan. Obrolan hanya disimpan selama browser terbuka.",
                    "This conversation wasn't found. Conversations are kept only while the browser is open."])}
                <p class="tombol-tombol"><a class="tombol" href="{Alamat}">{t["Pertanyaan baru", "New question"]}</a></p>
                """);

        var pertanyaan = kueri["pertanyaan"] ?? "";
        string? pesan = null;
        // Tanpa token, seperti membatalkan serap: halaman ini memuat ulang
        // dirinya, dan membatalkan dua kali tidak merugikan apa-apa.
        if (post && kueri["aksi"] == "batal")
            obrolan.Batalkan();
        else if (post && kueri["aksi"] == "tanya")
        {
            pesan = obrolan.Keadaan.Bekerja
                ? t["AI masih menjawab pertanyaan sebelumnya. Tunggu sebentar.", "The AI is still answering the previous question. Wait a moment."]
                : Masalah(t, kueri, pertanyaan);
            if (pesan is null && penanya.Tanya(obrolan, Rapikan(pertanyaan), t))
                return HalamanBelajar.Pindah(t, AlamatObrolan(obrolan) + "#akhir", judul);
        }

        var k = obrolan.Keadaan;
        // Daftar catatan baru dibaca kalau ada [[tautan]]: halaman ini dimuat ulang tiap 2 detik.
        Func<string, Catatan?>? penaut = null;
        (string, string)? Tautan(string sasaran) =>
            (penaut ??= buku.Penaut(obrolan.Lampiran?.Mapel))(sasaran) is { } c ? (HalamanBelajar.Alamat(c), c.Judul) : null;
        var isi = new StringBuilder();
        // Selama AI bekerja, halaman ini memuat ulang dirinya tiap 2 detik.
        if (k.Bekerja)
            isi.Append("""<meta http-equiv="refresh" content="2">""").Append('\n');
        isi.Append($"""
            {HalamanBelajar.Jejak(t, null)}
            <h1>{judul}</h1>
            {Tentang(t, obrolan.Lampiran)}

            """);
        for (var i = 0; i < k.Giliran.Count; i++)
            Giliran(isi, t, obrolan, k, k.Giliran[i], i == k.Giliran.Count - 1, Tautan);

        if (k.Bekerja)
            isi.Append($"""
                <form action="{HtmlEncode(AlamatObrolan(obrolan) + "#akhir")}" method="post">
                  <input type="hidden" name="aksi" value="batal">
                  <button class="tombol" type="submit">{t["Batalkan", "Cancel"]}</button>
                </form>

                """);
        else
            isi.Append(HalamanPengaturan.Pesan(pesan)).Append('\n')
                .Append(Formulir(t, AlamatObrolan(obrolan) + "#akhir", pertanyaan, false)).Append('\n');
        isi.Append(Kaki(t, obrolan.Model));
        return (k.Bekerja ? t["Menjawab…", "Answering…"] : judul, isi.ToString());
    }

    void Giliran(StringBuilder isi, Teks t, Obrolan obrolan, KeadaanObrolan k, GiliranTanya g, bool terakhir, Func<string, (string, string)?> tautan)
    {
        isi.Append(terakhir ? """<section class="giliran" id="akhir">""" : """<section class="giliran">""").Append('\n')
            .Append($"""<p class="pertanyaan">{HtmlEncode(g.Pertanyaan)}</p>""").Append('\n');
        if (g.Jawaban is { } jawaban)
        {
            isi.Append($"""<div class="isi-catatan jawaban">{Markah.KeHtml(jawaban, tautan, geserJudul: 2)}</div>""").Append('\n');
            if (g.Dibaca.Count > 0)
                isi.Append($"""<p class="catatan">{t["Catatan yang dibaca:", "Notes read:"]} {string.Join(", ", g.Dibaca.Select(c =>
                    $"""<a href="{HtmlEncode(HalamanBelajar.Alamat(c))}">{HtmlEncode(c.Judul)}</a>"""))}</p>""").Append('\n');
            isi.Append($"""<p class="catatan">{Pemakaian(t, obrolan, g)}</p>""").Append('\n');
        }
        else if (g.Galat is { } galat)
            isi.Append(HalamanPengaturan.Pesan(galat)).Append('\n');
        else
        {
            var detik = (int)(waktu.GetUtcNow() - g.Mulai).TotalSeconds;
            isi.Append("""<ol class="tahap">""");
            foreach (var langkah in k.Langkah)
                isi.Append($"""<li class="selesai">{HtmlEncode(langkah)}</li>""");
            isi.Append($"""<li class="sedang">{t["Berpikir…", "Thinking…"]} · {t[$"{detik} detik", $"{detik} s"]}</li></ol>""").Append('\n');
        }
        isi.Append("</section>\n");
    }

    static string Pemakaian(Teks t, Obrolan o, GiliranTanya g)
    {
        var masuk = t.Angka(g.TokenCache + g.TokenBaru);
        var detik = (int)((g.Selesai ?? g.Mulai) - g.Mulai).TotalSeconds;
        var biaya = HalamanSerap.Dolar(t, g.Biaya);
        return t[$"{o.Model} · {masuk} token masuk ({t.Angka(g.TokenCache)} dari cache), {t.Angka(g.TokenKeluar)} keluar · biaya {biaya} · {detik} detik",
            $"{o.Model} · {masuk} tokens in ({t.Angka(g.TokenCache)} cached), {t.Angka(g.TokenKeluar)} out · cost {biaya} · {detik} s"];
    }

    string Formulir(Teks t, string aksi, string pertanyaan, bool pertama) => $"""
        <form class="tulis" action="{HtmlEncode(aksi)}" method="post">
          <input type="hidden" name="aksi" value="tanya">
          <input type="hidden" name="token" value="{TokenSekali.Buat()}">
          <textarea class="tanya" name="pertanyaan" rows="3"{(pertama ? " autofocus" : "")} aria-label="{t["Pertanyaan", "Question"]}" placeholder="{(pertama
              ? t["mis. Apa bedanya gaya gesek statis dan kinetis?", "e.g. What's the difference between static and kinetic friction?"]
              : t["Pertanyaan lanjutan", "Follow-up question"])}">
        {HtmlEncode(pertanyaan)}</textarea>
          <p class="tombol-tombol"><button class="tombol utama" type="submit"{(Pengaturan.Kunci is null ? " disabled" : "")}>{t["Tanya", "Ask"]}</button>{(pertama ? ""
              : $""" <a class="tombol" href="{Alamat}">{t["Pertanyaan baru", "New question"]}</a>""")}</p>
        </form>
        """;

    static string Tentang(Teks t, Catatan? c) => c is null ? "" :
        $"""<p>{t["Tentang catatan", "About the note"]} <a href="{HtmlEncode(HalamanBelajar.Alamat(c))}">{HtmlEncode(c.Judul)}</a>{(c.Mapel is null ? "" : $" ({HtmlEncode(HalamanBelajar.NamaMapel(c.Mapel))})")}</p>""";

    static string Kaki(Teks t, string model) =>
        $"""<p class="catatan">{t["Model:", "Model:"]} {HtmlEncode(model)} · <a href="{HalamanBawaan.Belajar}?ai">{t["ubah", "change"]}</a><br>{t[
            "Pertanyaanmu dan catatan yang dibaca AI dikirim ke DeepSeek. Obrolan tidak disimpan; yang penting, tulis jadi catatan.",
            "Your questions and the notes the AI reads are sent to DeepSeek. Conversations aren't saved; write down what matters as a note."]}</p>""";

    static string Rapikan(string pertanyaan) => pertanyaan.Replace("\r\n", "\n").Trim();
}
