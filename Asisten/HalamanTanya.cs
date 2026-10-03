using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// kevin://belajar?tanya: belajar lewat obrolan dengan AI yang mengajar dari
/// catatan siswa (<see cref="Penanya"/>), tampil seperti aplikasi chat:
/// gelembung siswa di kanan, AI di kiri, kotak tulis di bawah.
/// <c>?tanya</c> memulai obrolan, boleh tentang satu catatan
/// (<c>&amp;m=…&amp;c=…</c>, tombol "Tanya tentang catatan ini", dengan
/// tombol saran untuk memulai). <c>?tanya&amp;obrolan=ID</c> menampilkan
/// obrolannya dan memperbarui dirinya sendiri (meta refresh, tanpa
/// JavaScript) selama AI masih menjawab. Pesan dikirim lewat POST bertoken
/// sekali pakai, lalu halamannya pindah ke alamat obrolan, jadi muat ulang
/// tidak mengirim lagi.
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

    // ---------- ?tanya: pesan pertama ----------

    (string, string) Mulai(Teks t, Kueri kueri, bool post)
    {
        var judul = t["Tanya", "Ask"];
        var mapel = string.IsNullOrEmpty(kueri["m"]) ? null : kueri["m"];
        var lampiran = kueri["c"] is { } nama ? buku.Ambil(mapel, nama) : null;
        var aksi = lampiran is null ? Alamat : AlamatTentang(lampiran);
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
            {Kepala(t, judul, lampiran, false)}
            {HalamanSerap.TanpaKunci(t, Pengaturan)}
            <div class="obrolan">
            {Sapaan(t, lampiran)}
            </div>
            {(lampiran is null ? "" : Saran(t, aksi))}
            {HalamanPengaturan.Pesan(pesan)}
            {Formulir(t, aksi, pertanyaan, true)}
            {Kaki(t, Pengaturan.ModelTanya, [])}
            """);
    }

    // Kenapa pesannya belum bisa dikirim; null kalau bisa (tokennya lalu terpakai).
    string? Masalah(Teks t, Kueri kueri, string pertanyaan) =>
        Pengaturan.Kunci is null ? t["Belum ada kunci API. Atur dulu di halaman Asisten AI.", "There's no API key yet. Set one on the AI assistant page first."]
        : pertanyaan.Trim().Length == 0 ? t["Tulis dulu pesannya.", "Type the message first."]
        : pertanyaan.Length > Penanya.MaksHurufPertanyaan
            ? t[$"Pesannya terlalu panjang: {t.Angka(pertanyaan.Length)} huruf, batasnya {t.Angka(Penanya.MaksHurufPertanyaan)}. Materi yang panjang lebih baik diserap dulu jadi catatan.",
                $"The message is too long: {t.Angka(pertanyaan.Length)} characters, the limit is {t.Angka(Penanya.MaksHurufPertanyaan)}. Long material is better turned into notes first."]
        : !TokenSekali.Pakai(kueri["token"])
            ? t["Permintaan ini sudah dipakai atau kedaluwarsa. Periksa pesannya, lalu tekan Kirim lagi.",
                "This request was already used or has expired. Check the message, then press Send again."]
        : null;

    // ---------- ?tanya&obrolan=…: obrolan dan pesan berikutnya ----------

    (string, string) Obrolan(Teks t, Kueri kueri, string id, bool post)
    {
        var judul = t["Tanya", "Ask"];
        if (penanya.Ambil(id) is not { } obrolan)
            return (judul, $"""
                {HalamanBelajar.Jejak(t, null)}
                <h1>{judul}</h1>
                {HalamanPengaturan.Pesan(t["Obrolan ini tidak ditemukan. Obrolan hanya disimpan selama browser terbuka.",
                    "This conversation wasn't found. Conversations are kept only while the browser is open."])}
                <p class="tombol-tombol"><a class="tombol" href="{Alamat}">{t["Obrolan baru", "New chat"]}</a></p>
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
                ? t["AI masih menjawab pesan sebelumnya. Tunggu sebentar.", "The AI is still answering the previous message. Wait a moment."]
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
            {Kepala(t, judul, obrolan.Lampiran, true)}
            <div class="obrolan">
            {Sapaan(t, obrolan.Lampiran)}

            """);
        foreach (var g in k.Giliran)
            Giliran(isi, t, obrolan, k, g, Tautan);
        isi.Append("</div>\n");

        if (k.Bekerja)
            isi.Append(FormulirBatal(t, obrolan)).Append('\n');
        else if (k.Giliran.Count >= Penanya.MaksGiliran)
            isi.Append(HalamanPengaturan.Pesan(t[
                "Obrolan ini sudah panjang. Mulai obrolan baru supaya browser tetap ringan; yang penting dari obrolan ini, tulis dulu jadi catatan.",
                "This chat is getting long. Start a new chat to keep the browser light; write down what matters from this one as a note first."])).Append('\n');
        else
            isi.Append(HalamanPengaturan.Pesan(pesan)).Append('\n')
                .Append(Formulir(t, AlamatObrolan(obrolan) + "#akhir", pertanyaan, false)).Append('\n');
        isi.Append(Kaki(t, obrolan.Model, k.Giliran));
        return (k.Bekerja ? t["Menjawab…", "Answering…"] : judul, isi.ToString());
    }

    void Giliran(StringBuilder isi, Teks t, Obrolan obrolan, KeadaanObrolan k, GiliranTanya g, Func<string, (string, string)?> tautan)
    {
        isi.Append("""<section class="giliran">""").Append('\n')
            .Append($"""<p class="gelembung siswa">{HtmlEncode(g.Pertanyaan)}</p>""").Append('\n');
        if (g.Jawaban is { } jawaban)
        {
            isi.Append($"""<div class="gelembung ai isi-catatan">{Markah.KeHtml(jawaban, tautan, geserJudul: 2)}</div>""").Append('\n');
            var dibaca = g.Dibaca.Count == 0 ? "" : $"""{t["Catatan yang dibaca:", "Notes read:"]} {string.Join(", ", g.Dibaca.Select(c =>
                $"""<a href="{HtmlEncode(HalamanBelajar.Alamat(c))}">{HtmlEncode(c.Judul)}</a>"""))} · """;
            isi.Append($"""<p class="info" title="{Pemakaian(t, obrolan, g)}">{dibaca}{HalamanSerap.Dolar(t, g.Biaya)}</p>""").Append('\n');
        }
        else if (g.Galat is { } galat)
            isi.Append(HalamanPengaturan.Pesan(galat)).Append('\n');
        else
        {
            var detik = (int)(waktu.GetUtcNow() - g.Mulai).TotalSeconds;
            isi.Append("""<div class="gelembung ai"><ol class="tahap">""");
            foreach (var langkah in k.Langkah)
                isi.Append($"""<li class="selesai">{HtmlEncode(langkah)}</li>""");
            isi.Append($"""<li class="sedang">{t["Berpikir…", "Thinking…"]} · {t[$"{detik} detik", $"{detik} s"]}</li></ol></div>""").Append('\n');
        }
        isi.Append("</section>\n");
    }

    // Rincian untuk tooltip di bawah jawaban (sudah aman untuk atribut HTML);
    // biayanya sendiri tampil di sana.
    static string Pemakaian(Teks t, Obrolan o, GiliranTanya g)
    {
        var model = HtmlEncode(o.Model);
        var masuk = t.Angka(g.TokenCache + g.TokenBaru);
        var detik = (int)((g.Selesai ?? g.Mulai) - g.Mulai).TotalSeconds;
        return t[$"{model} · {masuk} token masuk ({t.Angka(g.TokenCache)} dari cache), {t.Angka(g.TokenKeluar)} keluar · {detik} detik",
            $"{model} · {masuk} tokens in ({t.Angka(g.TokenCache)} cached), {t.Angka(g.TokenKeluar)} out · {detik} s"];
    }

    // Judul, tombol obrolan baru (kalau sudah mengobrol), dan catatan yang dibahas.
    static string Kepala(Teks t, string judul, Catatan? lampiran, bool sudahMengobrol) => $"""
        <div class="judul-obrolan"><h1>{judul}</h1>{(sudahMengobrol ? $"""<a class="tombol" href="{Alamat}">{t["Obrolan baru", "New chat"]}</a>""" : "")}</div>
        {Tentang(t, lampiran)}
        """;

    static string Tentang(Teks t, Catatan? c) => c is null ? "" :
        $"""<p class="catatan">{t["Tentang catatan", "About the note"]} <a href="{HtmlEncode(HalamanBelajar.Alamat(c))}">{HtmlEncode(c.Judul)}</a>{(c.Mapel is null ? "" : $" ({HtmlEncode(HalamanBelajar.NamaMapel(c.Mapel))})")}</p>""";

    // Sapaan pembuka. Tetap, bukan dari AI, jadi tanpa biaya.
    static string Sapaan(Teks t, Catatan? c) => $"""<div class="gelembung ai"><p>{(c is null
        ? t["Halo! Mau belajar apa hari ini? Tanyakan apa saja tentang pelajaranmu. Aku akan mencarinya di catatanmu, lalu menjelaskannya pelan-pelan.",
            "Hi! What do you want to learn today? Ask me anything about your lessons. I'll look in your notes, then explain it step by step."]
        : t[$"Halo! Kita bahas catatan <strong>{HtmlEncode(c.Judul)}</strong>, ya. Bagian mana yang ingin kamu pahami?",
            $"Hi! Let's go through the note <strong>{HtmlEncode(c.Judul)}</strong>. Which part do you want to understand?"])}</p></div>""";

    // Tombol saran untuk memulai obrolan tentang satu catatan; tulisannya jadi pesan pertama.
    string Saran(Teks t, string aksi)
    {
        var mati = Pengaturan.Kunci is null ? " disabled" : "";
        var tombol = string.Join("\n  ", new[]
        {
            t["Jelaskan catatan ini pelan-pelan", "Walk me through this note"],
            t["Apa inti catatan ini?", "What's the main idea?"],
            t["Uji pemahamanku", "Quiz me on it"],
        }.Select(s => $"""<button class="tombol" type="submit" name="pertanyaan" value="{HtmlEncode(s)}"{mati}>{HtmlEncode(s)}</button>"""));
        return $"""
            <form class="saran" action="{HtmlEncode(aksi)}" method="post">
              <input type="hidden" name="aksi" value="tanya">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              {tombol}
            </form>
            """;
    }

    string Formulir(Teks t, string aksi, string pertanyaan, bool pertama) => $"""
        <form class="kirim menempel" action="{HtmlEncode(aksi)}" method="post">
          <input type="hidden" name="aksi" value="tanya">
          <input type="hidden" name="token" value="{TokenSekali.Buat()}">
          <textarea id="akhir" name="pertanyaan" rows="2" autofocus aria-label="{t["Pesan", "Message"]}" placeholder="{(pertama
              ? t["mis. Apa bedanya gaya gesek statis dan kinetis?", "e.g. What's the difference between static and kinetic friction?"]
              : t["Tulis jawabanmu, atau tanya lagi", "Type your answer, or ask something else"])}">
        {HtmlEncode(pertanyaan)}</textarea>
          <button class="tombol utama" type="submit"{(Pengaturan.Kunci is null ? " disabled" : "")}>{t["Kirim", "Send"]}</button>
        </form>
        """;

    // Selama AI menjawab, kotak tulis dimatikan (halaman ini dimuat ulang tiap
    // 2 detik, jadi ketikannya akan hilang) dan Kirim berganti Batalkan.
    static string FormulirBatal(Teks t, Obrolan o) => $"""
        <form class="kirim" action="{HtmlEncode(AlamatObrolan(o) + "#akhir")}" method="post">
          <input type="hidden" name="aksi" value="batal">
          <textarea id="akhir" rows="2" disabled aria-label="{t["Pesan", "Message"]}" placeholder="{t["Tunggu jawabannya dulu…", "Wait for the answer…"]}"></textarea>
          <button class="tombol" type="submit">{t["Batalkan", "Cancel"]}</button>
        </form>
        """;

    static string Kaki(Teks t, string model, IReadOnlyList<GiliranTanya> giliran)
    {
        var biaya = giliran.Count == 0 ? "" : $" · {t["biaya obrolan ini", "this chat cost"]} {HalamanSerap.Dolar(t, giliran.Sum(g => g.Biaya))}";
        return $"""<p class="catatan">{t["Model:", "Model:"]} {HtmlEncode(model)} · <a href="{HalamanBawaan.Belajar}?ai">{t["ubah", "change"]}</a>{biaya}<br>{t[
            "Pesanmu dan catatan yang dibaca AI dikirim ke DeepSeek. Obrolan tidak disimpan; yang penting, tulis jadi catatan.",
            "Your messages and the notes the AI reads are sent to DeepSeek. Conversations aren't saved; write down what matters as a note."]}</p>""";
    }

    static string Rapikan(string pertanyaan) => pertanyaan.Replace("\r\n", "\n").Trim();
}
