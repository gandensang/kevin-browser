using System.Globalization;
using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// Bagian kevin://belajar untuk asisten AI: <c>?ai</c> (model dan kunci API
/// DeepSeek milik pemakai) dan <c>?serap</c> (materi pelajaran → catatan).
/// Alurnya: pilih dokumen di folder unduhan atau tempel teks → halaman
/// konfirmasi dengan perkiraan biaya → POST bertoken memulai pekerjaan di
/// latar → <c>?serap&amp;kerja=ID</c> memperbarui dirinya sendiri (meta refresh,
/// tanpa JavaScript) sampai catatannya tertulis.
/// </summary>
sealed class HalamanSerap(BukuCatatan buku, TimeProvider waktu, AlatSerap alat)
{
    const string Alamat = HalamanBawaan.Belajar + "?serap";
    const string AlamatAi = HalamanBawaan.Belajar + "?ai";
    const long PdfMaks = 100L * 1024 * 1024;
    const long TeksMaks = 5L * 1024 * 1024;

    readonly KlienAi klien = new(alat.Jaringan);
    readonly Penyerap penyerap = new(buku, alat.Pengaturan, new KlienAi(alat.Jaringan), waktu);

    // Teks dokumen terakhir yang dibaca: halaman konfirmasi dan tombol Serap
    // memakai teks yang sama tanpa membaca PDF dua kali. Thread utama saja.
    (string Jalur, long Ukuran, long WaktuUbah, string Teks)? terakhir;

    PengaturanAi Pengaturan => alat.Pengaturan;

    public async Task<(string Judul, string Isi)> Buat(Kueri kueri, bool post, Teks t)
    {
        if (kueri["ai"] is not null)
            return await Ai(t, kueri, post);
        if (kueri["kerja"] is { } id)
            return Kerja(t, kueri, id, post);
        if (kueri["tempel"] is not null)
            return Tempel(t, kueri, post);
        if (kueri["berkas"] is { } nama)
            return await Berkas(t, kueri, nama, post);
        return Mulai(t);
    }

    // ---------- ?ai: model dan kunci API ----------

    async Task<(string, string)> Ai(Teks t, Kueri kueri, bool post)
    {
        string? pesan = null;
        if (post && !TokenSekali.Pakai(kueri["token"]))
            pesan = t["Permintaan ini sudah dipakai atau kedaluwarsa. Tidak ada yang diubah.",
                "This request was already used or has expired. Nothing was changed."];
        else if (post && kueri["aksi"] == "hapus")
        {
            Pengaturan.HapusKunci();
            pesan = t["Kunci API dihapus dari laptop ini.", "The API key was removed from this laptop."];
        }
        else if (post)
        {
            var kunci = (kueri["kunci"] ?? "").Trim();
            if (kunci.Length > 0 && !PengaturanAi.KunciSah(kunci))
                pesan = t["Kunci API itu tidak wajar. Salin ulang dari platform.deepseek.com.",
                    "That API key doesn't look right. Copy it again from platform.deepseek.com."];
            else
            {
                Pengaturan.Simpan(kueri["model"] ?? "", kunci.Length > 0 ? kunci : null);
                pesan = t["Pengaturan Asisten AI disimpan.", "AI assistant settings saved."];
            }
        }

        var judul = t["Asisten AI", "AI assistant"];
        var model = string.Concat(PengaturanAi.SemuaModel.Select(m =>
            $"""<option value="{m}"{(m == Pengaturan.Model ? " selected" : "")}>{HtmlEncode(NamaModel(t, m))}</option>"""));
        var kunciAda = Pengaturan.KunciTersamar is { } samar
            ? $"""<p>{t["Kunci tersimpan:", "Saved key:"]} <code>{HtmlEncode(samar)}</code>. {await Saldo(t)}</p>"""
            : $"""<p>{t["Belum ada kunci API.", "No API key yet."]}</p>""";
        var hapus = Pengaturan.Kunci is null ? "" : $"""
            <form action="{AlamatAi}" method="post">
              <input type="hidden" name="aksi" value="hapus">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <button class="tombol bahaya" type="submit">{t["Hapus kunci", "Remove key"]}</button>
            </form>
            """;
        return (judul, $"""
            {HalamanBelajar.Jejak(t, null)}
            <h1>{judul}</h1>
            <p class="pembuka">{t["Untuk menyerap materi pelajaran jadi catatan, dengan kunci API DeepSeek milik sendiri.",
                "Turns study material into notes, with your own DeepSeek API key."]}</p>
            {HalamanPengaturan.Pesan(pesan)}
            {kunciAda}
            <form class="tulis" action="{AlamatAi}" method="post">
              <input type="hidden" name="aksi" value="simpan">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <label>{t["Model", "Model"]} <select name="model">{model}</select></label>
              <label>{t["Kunci API", "API key"]} <input type="password" name="kunci" autocomplete="off" spellcheck="false" placeholder="sk-…"></label>
              <p class="catatan">{t["Kosongkan kalau kuncinya tidak diganti.", "Leave empty to keep the current key."]}</p>
              <p class="tombol-tombol"><button class="tombol utama" type="submit">{t["Simpan", "Save"]}</button></p>
            </form>
            {hapus}
            <h2>{t["Cara mendapat kunci", "How to get a key"]}</h2>
            <ol>
              <li>{t["Buat akun di <a href=\"https://platform.deepseek.com\">platform.deepseek.com</a>.", "Create an account at <a href=\"https://platform.deepseek.com\">platform.deepseek.com</a>."]}</li>
              <li>{t["Isi saldo. Beberapa dolar cukup untuk banyak dokumen; perkiraan biaya tiap dokumen tampil sebelum diserap.", "Top up. A few dollars cover many documents; the estimated cost of each document is shown before it's sent."]}</li>
              <li>{t["Buat kunci di menu API Keys, salin, lalu tempel di atas.", "Create a key under API Keys, copy it, and paste it above."]}</li>
            </ol>
            <h2>{t["Privasi", "Privacy"]}</h2>
            <p>{t["Kunci disimpan hanya di laptop ini, dalam berkas yang hanya bisa dibaca akun Anda, dan hanya dikirim ke api.deepseek.com. Isi dokumen yang diserap dikirim ke DeepSeek untuk diolah. Menurut <a href=\"https://cdn.deepseek.com/policies/en-US/deepseek-privacy-policy.html\">kebijakan privasinya</a>, DeepSeek menyimpan data di Tiongkok dan bisa memakainya untuk melatih modelnya. Jangan menyerap dokumen yang berisi data pribadi.",
                "The key is stored only on this laptop, in a file only your account can read, and is sent only to api.deepseek.com. Documents you turn into notes are sent to DeepSeek to be processed. According to its <a href=\"https://cdn.deepseek.com/policies/en-US/deepseek-privacy-policy.html\">privacy policy</a>, DeepSeek stores data in China and may use it to train its models. Don't send documents that contain personal data."]}</p>
            """);
    }

    async Task<string> Saldo(Teks t)
    {
        try
        {
            using var batas = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var saldo = await klien.Saldo(PengaturanAi.Alamat, Pengaturan.Kunci!, batas.Token);
            var jumlah = $"{saldo.Jumlah.ToString("0.00", CultureInfo.InvariantCulture)} {saldo.MataUang}";
            if (!t.Inggris)
                jumlah = jumlah.Replace('.', ',');
            return saldo.BisaDipakai
                ? t[$"Saldo: {jumlah}.", $"Balance: {jumlah}."]
                : t[$"Saldo: {jumlah}, tidak cukup untuk dipakai. Isi saldo di platform.deepseek.com.",
                    $"Balance: {jumlah}, not enough to use. Top up at platform.deepseek.com."];
        }
        catch (GalatAi e) when (e.Kode == 401)
        {
            return t["DeepSeek menolak kunci ini. Periksa lagi kuncinya.", "DeepSeek rejected this key. Check the key again."];
        }
        catch (Exception e) when (e is GalatAi or OperationCanceledException)
        {
            return t["Saldo tidak bisa dicek sekarang.", "The balance can't be checked right now."];
        }
    }

    // ---------- ?serap: pilih dokumen ----------

    (string, string) Mulai(Teks t)
    {
        var judul = t["Serap materi", "Turn material into notes"];
        var isi = new StringBuilder($"""
            {HalamanBelajar.Jejak(t, null)}
            <h1>{judul}</h1>
            <p class="pembuka">{t["AI mengolah materi pelajaran jadi catatan per topik: yang penting dibawa, yang dilewati disebutkan.",
                "AI turns study material into notes, one per topic: what matters is kept, and what was skipped is listed."]}</p>
            {TanpaKunci(t)}
            <h2>{t["Dari folder unduhan", "From the downloads folder"]}</h2>

            """);

        var dokumen = DaftarDokumen.Terbaru(alat.FolderUnduhan);
        if (dokumen.Count == 0)
            isi.Append($"""<p>{t[$"Tidak ada PDF, .txt, atau .md di <code>{HtmlEncode(HalamanBelajar.Tampilan(alat.FolderUnduhan))}</code>.",
                $"There are no PDF, .txt, or .md files in <code>{HtmlEncode(HalamanBelajar.Tampilan(alat.FolderUnduhan))}</code>."]}</p>""");
        else
        {
            isi.Append($"""<div class="tabel"><table><thead><tr><th>{t["Berkas", "File"]}</th><th class="angka">{t["Ukuran", "Size"]}</th><th>{t["Diubah", "Changed"]}</th></tr></thead><tbody>""").Append('\n');
            foreach (var d in dokumen)
            {
                var sudah = buku.SudahDiserap(d.Nama, d.Ukuran, d.WaktuUbah) is null ? "" : $""" <span class="catatan">· {t["sudah diserap", "already done"]}</span>""";
                isi.Append($"""<tr><td><a href="{HtmlEncode($"{Alamat}&berkas={Uri.EscapeDataString(d.Nama)}")}">{HtmlEncode(d.Nama)}</a>{sudah}</td><td class="angka">{t.Ukuran(d.Ukuran)}</td><td>{t.Tanggal(d.Diubah)}</td></tr>""").Append('\n');
            }
            isi.Append("</tbody></table></div>\n");
        }

        isi.Append($"""
            <h2>{t["Atau tempel teksnya", "Or paste the text"]}</h2>
            <p>{t["Dari dokumen lain, halaman web, atau ketikan sendiri.", "From another document, a web page, or your own typing."]}</p>
            <p><a class="tombol" href="{HtmlEncode(Alamat + "&tempel")}">{t["Tempel teks", "Paste text"]}</a></p>
            {InfoModel(t)}
            """);
        return (judul, isi.ToString());
    }

    // ---------- ?serap&berkas=…: konfirmasi, lalu mulai ----------

    async Task<(string, string)> Berkas(Teks t, Kueri kueri, string nama, bool post)
    {
        var judul = t["Serap materi", "Turn material into notes"];
        if (DaftarDokumen.Ambil(alat.FolderUnduhan, nama) is not { } dok)
            return (judul, Kerangka(t, judul, $"""<p class="pesan">{t["Berkas itu tidak ada di folder unduhan.", "That file isn't in the downloads folder."]}</p>"""));

        if (buku.SudahDiserap(dok.Nama, dok.Ukuran, dok.WaktuUbah) is { } jadi)
            return (judul, Kerangka(t, judul, $"""
                <p class="pesan">{t[$"Dokumen ini sudah pernah diserap jadi: {HtmlEncode(jadi)}. Tidak perlu diserap lagi; kalau berkasnya berubah, ia bisa diserap ulang.",
                    $"This document was already turned into: {HtmlEncode(jadi)}. No need to do it again; if the file changes, it can be done again."]}</p>
                <p><a class="tombol" href="{HalamanBawaan.Belajar}">{t["Lihat catatan", "See the notes"]}</a></p>
                """));

        var (teks, galat) = await BacaDokumen(t, dok);
        if (teks is null)
            return (judul, Kerangka(t, judul, $"""<p class="pesan">{HtmlEncode(galat!)}</p>"""));

        var aksi = $"{Alamat}&berkas={Uri.EscapeDataString(dok.Nama)}";
        string? pesan = null;
        if (post && kueri["aksi"] == "mulai")
        {
            if (Mulai(t, kueri, dok.Nama, dok.Ukuran, dok.WaktuUbah, teks) is { } kerja)
                return HalamanBelajar.Pindah(t, $"{Alamat}&kerja={kerja.Id}");
            pesan = Masalah(t, kueri);
        }

        var halaman = teks.Count(c => c == '\f') + 1;
        var info = dok.Nama.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? t[$"{halaman} halaman, ", $"{halaman} pages, "] : "";
        return (judul, Kerangka(t, judul, $"""
            {TanpaKunci(t)}
            {HalamanPengaturan.Pesan(pesan)}
            <div class="kartu"><p><strong>{HtmlEncode(dok.Nama)}</strong><br>{info}{t.Ukuran(dok.Ukuran)}, {t[$"{t.Angka(teks.Length)} huruf teks", $"{t.Angka(teks.Length)} characters of text"]}</p></div>
            <form class="tulis" action="{HtmlEncode(aksi)}" method="post">
              <input type="hidden" name="aksi" value="mulai">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              {HalamanBelajar.PilihanMapel(t, buku, kueri["mapel"] ?? "", kueri["mapel-baru"] ?? "")}
              {Perkiraan(t, teks.Length)}
              <p class="tombol-tombol"><button class="tombol utama" type="submit"{(Pengaturan.Kunci is null ? " disabled" : "")}>{t["Serap", "Make notes"]}</button>
              <a class="tombol" href="{Alamat}">{t["Batal", "Cancel"]}</a></p>
            </form>
            """));
    }

    // ---------- ?serap&tempel: teks tempelan ----------

    (string, string) Tempel(Teks t, Kueri kueri, bool post)
    {
        var judul = t["Tempel teks", "Paste text"];
        var nama = kueri["judul"] ?? "";
        var teks = (kueri["teks"] ?? "").Replace("\r\n", "\n");
        string? pesan = null;
        if (post && kueri["aksi"] == "mulai")
        {
            if (nama.Trim().Length == 0)
                pesan = t["Beri judul dulu, misalnya \"Catatan papan tulis 3 Oktober\".", "Give it a title first, for example \"Whiteboard notes, 3 October\"."];
            else if (teks.Trim().Length < 50)
                pesan = t["Teksnya terlalu pendek untuk dijadikan catatan.", "The text is too short to turn into notes."];
            else if (teks.Length > Penyerap.MaksHuruf)
                pesan = TerlaluPanjang(t, teks.Length);
            else if (Mulai(t, kueri, t[$"tempelan: {nama.Trim()}", $"pasted: {nama.Trim()}"], Encoding.UTF8.GetByteCount(teks),
                         waktu.GetUtcNow().ToUnixTimeSeconds(), teks) is { } kerja)
                return HalamanBelajar.Pindah(t, $"{Alamat}&kerja={kerja.Id}");
            else
                pesan = Masalah(t, kueri);
        }
        return (judul, Kerangka(t, judul, $"""
            {TanpaKunci(t)}
            {HalamanPengaturan.Pesan(pesan)}
            <form class="tulis" action="{HtmlEncode(Alamat + "&tempel")}" method="post">
              <input type="hidden" name="aksi" value="mulai">
              <input type="hidden" name="token" value="{TokenSekali.Buat()}">
              <label>{t["Judul", "Title"]} <input type="text" name="judul" value="{HtmlEncode(nama)}" required placeholder="{t["mis. Catatan papan tulis 3 Oktober", "e.g. Whiteboard notes, 3 October"]}"></label>
              {HalamanBelajar.PilihanMapel(t, buku, kueri["mapel"] ?? "", kueri["mapel-baru"] ?? "")}
              <label for="teks">{t["Teks", "Text"]}</label>
              <textarea id="teks" name="teks" rows="16">
            {HtmlEncode(teks)}</textarea>
              {Perkiraan(t, Math.Max(teks.Length, 3000))}
              <p class="tombol-tombol"><button class="tombol utama" type="submit"{(Pengaturan.Kunci is null ? " disabled" : "")}>{t["Serap", "Make notes"]}</button>
              <a class="tombol" href="{Alamat}">{t["Batal", "Cancel"]}</a></p>
            </form>
            """));
    }

    // Pekerjaan baru kalau token, kunci, dan mata pelajarannya beres; null kalau tidak.
    PekerjaanSerap? Mulai(Teks t, Kueri kueri, string sumber, long ukuran, long waktuUbah, string teks) =>
        Pengaturan.Kunci is not null && NamaMapel(kueri) is { } mapel && TokenSekali.Pakai(kueri["token"])
            ? penyerap.Mulai(sumber, ukuran, waktuUbah, teks, mapel, t)
            : null;

    string? NamaMapel(Kueri kueri)
    {
        var baru = kueri["mapel-baru"] ?? "";
        var mapel = baru.Trim().Length > 0 ? baru : kueri["mapel"] ?? "";
        return mapel.Trim().Length == 0 ? null : buku.NamaFolderMapel(mapel);
    }

    // Kenapa Mulai menolak, dengan urutan yang sama.
    string Masalah(Teks t, Kueri kueri) =>
        Pengaturan.Kunci is null ? t["Belum ada kunci API. Atur dulu di halaman Asisten AI.", "There's no API key yet. Set one on the AI assistant page first."]
        : NamaMapel(kueri) is null ? t["Pilih atau tulis dulu mata pelajarannya.", "Choose or type the subject first."]
        : t["Permintaan ini sudah dipakai atau kedaluwarsa. Periksa isiannya, lalu tekan Serap lagi.",
            "This request was already used or has expired. Check the form, then press Make notes again."];

    // ---------- ?serap&kerja=…: kemajuan dan hasil ----------

    (string, string) Kerja(Teks t, Kueri kueri, string id, bool post)
    {
        if (penyerap.Ambil(id) is not { } kerja)
            return (t["Serap materi", "Turn material into notes"], Kerangka(t, t["Serap materi", "Turn material into notes"],
                $"""<p class="pesan">{t["Pekerjaan ini tidak ditemukan; mungkin browser sudah dibuka ulang. Catatan yang sudah jadi ada di halaman Belajar.",
                    "This job wasn't found; the browser may have been restarted. Finished notes are on the Learn page."]}</p>"""));
        // Tanpa token: halaman ini memuat ulang dirinya tiap 3 detik, dan token
        // baru tiap kali akan menggeser token formulir lain (TokenSekali hanya
        // mengingat 32). Membatalkan dua kali tidak merugikan apa-apa.
        if (post && kueri["aksi"] == "batal")
            kerja.Batal.Cancel();

        var k = kerja.Keadaan;
        var detik = (int)((k.Selesai ?? waktu.GetUtcNow()) - kerja.Mulai).TotalSeconds;
        var tujuan = $"{HtmlEncode(kerja.Sumber)} → {HtmlEncode(HalamanBelajar.NamaMapel(kerja.Mapel))}";
        switch (k.Tahap)
        {
            case TahapSerap.Diolah or TahapSerap.Menulis:
            {
                var judul = t["Menyerap…", "Working…"];
                var diterima = k.HurufDiterima > 0 ? t[$", {t.Angka(k.HurufDiterima)} huruf diterima", $", {t.Angka(k.HurufDiterima)} characters received"] : "";
                return (judul, $"""
                    <meta http-equiv="refresh" content="3">
                    {HalamanBelajar.Jejak(t, null)}
                    <h1>{judul}</h1>
                    <p class="pembuka">{tujuan}</p>
                    <ol class="tahap">
                      <li class="selesai">{t["Dokumen dibaca", "Document read"]}</li>
                      <li class="{(k.Tahap == TahapSerap.Diolah ? "sedang" : "selesai")}">{t[$"Diolah DeepSeek ({kerja.Model})", $"Processed by DeepSeek ({kerja.Model})"]} · {t[$"{detik} detik", $"{detik} s"]}{diterima}</li>
                      <li class="{(k.Tahap == TahapSerap.Menulis ? "sedang" : "")}">{t["Catatan ditulis", "Notes written"]}</li>
                    </ol>
                    <p class="catatan">{t["Halaman ini diperbarui sendiri. Boleh ditinggal; catatannya tetap ditulis.",
                        "This page updates itself. You can leave it; the notes are still written."]}</p>
                    <form action="{HtmlEncode($"{Alamat}&kerja={kerja.Id}")}" method="post">
                      <input type="hidden" name="aksi" value="batal">
                      <button class="tombol" type="submit">{t["Batalkan", "Cancel"]}</button>
                    </form>
                    """);
            }
            case TahapSerap.Selesai:
            {
                var judul = t["Selesai", "Done"];
                var daftar = string.Concat(k.Hasil.Select(c => $"""<li><a href="{HtmlEncode(HalamanBelajar.Alamat(c))}">{HtmlEncode(c.Judul)}</a></li>"""));
                return (judul, $"""
                    {HalamanBelajar.Jejak(t, null)}
                    <h1>{judul}</h1>
                    {HalamanPengaturan.Pesan(t[$"{k.Hasil.Count} catatan baru dari {kerja.Sumber}.", k.Hasil.Count == 1 ? $"1 new note from {kerja.Sumber}." : $"{k.Hasil.Count} new notes from {kerja.Sumber}."])}
                    <ul class="daftar-catatan">{daftar}</ul>
                    <p class="catatan">{Pemakaian(t, kerja, k, detik)}</p>
                    <p class="tombol-tombol"><a class="tombol" href="{HalamanBawaan.Belajar}">{t["Semua catatan", "All notes"]}</a>
                    <a class="tombol" href="{Alamat}">{t["Serap materi lain", "Another document"]}</a></p>
                    """);
            }
            default:
            {
                var judul = k.Tahap == TahapSerap.Dibatalkan ? t["Dibatalkan", "Cancelled"] : t["Gagal", "Failed"];
                var pesan = k.Tahap == TahapSerap.Dibatalkan
                    ? t["Penyerapan dibatalkan. Tidak ada catatan yang ditulis.", "Cancelled. No notes were written."]
                    : k.Galat!;
                return (judul, $"""
                    {HalamanBelajar.Jejak(t, null)}
                    <h1>{judul}</h1>
                    <p class="pembuka">{tujuan}</p>
                    {HalamanPengaturan.Pesan(pesan)}
                    {(k.Jawaban is null ? "" : $"""<p class="catatan">{Pemakaian(t, kerja, k, detik)}</p>""")}
                    <p class="tombol-tombol"><a class="tombol" href="{Alamat}">{t["Kembali", "Back"]}</a></p>
                    """);
            }
        }
    }

    static string Pemakaian(Teks t, PekerjaanSerap kerja, KeadaanSerap k, int detik)
    {
        var j = k.Jawaban!;
        var masuk = t.Angka(j.TokenCache + j.TokenBaru);
        return t[$"Token: {masuk} masuk ({t.Angka(j.TokenCache)} dari cache), {t.Angka(j.TokenKeluar)} keluar · biaya ±{Dolar(t, k.Biaya)} · {kerja.Model}, {detik} detik.",
            $"Tokens: {masuk} in ({t.Angka(j.TokenCache)} cached), {t.Angka(j.TokenKeluar)} out · cost ±{Dolar(t, k.Biaya)} · {kerja.Model}, {detik} s."];
    }

    // ---------- bagian bersama ----------

    async Task<(string? Teks, string? Galat)> BacaDokumen(Teks t, Dokumen dok)
    {
        if (terakhir is { } s && s.Jalur == dok.Jalur && s.Ukuran == dok.Ukuran && s.WaktuUbah == dok.WaktuUbah)
            return (s.Teks, null);
        var pdf = dok.Nama.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (dok.Ukuran > (pdf ? PdfMaks : TeksMaks))
            return (null, t["Berkas ini terlalu besar untuk diserap.", "This file is too big to turn into notes."]);
        string? teks;
        try
        {
            using var batas = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            // Sinkron di thread latar: IO berkas async menambah binary tanpa manfaat di sini.
            teks = pdf ? await alat.PdfKeTeks(dok.Jalur, batas.Token) : await Task.Run(() => File.ReadAllText(dok.Jalur), batas.Token);
        }
        catch (FileNotFoundException) when (pdf)
        {
            return (null, t["Pembaca PDF (pdftotext) belum terpasang. Pasang dengan: sudo apt install poppler-utils",
                "The PDF reader (pdftotext) isn't installed. Install it with: sudo apt install poppler-utils"]);
        }
        catch (OperationCanceledException)
        {
            return (null, t["Membaca berkas ini terlalu lama.", "Reading this file took too long."]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            teks = null;
        }
        if (teks is null)
            return (null, t["Berkas ini tidak bisa dibaca.", "This file could not be read."]);
        teks = Rapikan(teks);
        if (teks.Replace("\f", "").Trim().Length < 50)
            return (null, t["Hampir tidak ada teks di berkas ini. Kalau isinya hasil pindaian (gambar), teksnya belum bisa dibaca.",
                "There's almost no text in this file. If it's a scan (images), its text can't be read yet."]);
        if (teks.Length > Penyerap.MaksHuruf)
            return (null, TerlaluPanjang(t, teks.Length));
        terakhir = (dok.Jalur, dok.Ukuran, dok.WaktuUbah, teks);
        return (teks, null);
    }

    // Spasi di ujung baris dan baris kosong berlebih dibuang: token lebih
    // sedikit. Pemisah halaman pdftotext (\f) tetap, untuk menghitung halaman.
    static string Rapikan(string teks)
    {
        var hasil = new StringBuilder(teks.Length);
        var kosong = 0;
        foreach (var baris in teks.Replace("\r\n", "\n").Split('\n'))
        {
            var b = baris.TrimEnd();
            if (b.Length == 0 && ++kosong > 1)
                continue;
            if (b.Length > 0)
                kosong = 0;
            hasil.Append(b).Append('\n');
        }
        return hasil.ToString().Trim();
    }

    static string TerlaluPanjang(Teks t, int huruf) =>
        t[$"Teksnya terlalu panjang: {t.Angka(huruf)} huruf, batasnya {t.Angka(Penyerap.MaksHuruf)}. Pecah dokumennya, atau tempel bagian yang perlu saja.",
            $"The text is too long: {t.Angka(huruf)} characters, the limit is {t.Angka(Penyerap.MaksHuruf)}. Split the document, or paste only the part you need."];

    string Perkiraan(Teks t, int huruf)
    {
        var (masuk, keluar) = HargaAi.PerkiraanToken(huruf, PromptSerap.Sistem(t).Length);
        var model = Pengaturan.Model;
        var biaya = HargaAi.Biaya(model, 0, masuk, keluar, waktu.GetUtcNow());
        var jam = HargaAi.JamSibuk(waktu.GetUtcNow())
            ? t["jam sibuk DeepSeek; di luar jam sibuk separuhnya", "DeepSeek peak hours; half price off-peak"]
            : t["di luar jam sibuk DeepSeek", "DeepSeek off-peak hours"];
        return $"""<p class="catatan">{t[$"Perkiraan kasar: ±{t.Angka(masuk)} token masuk, ±{t.Angka(keluar)} token keluar, biaya ±{Dolar(t, biaya)} dengan {model} ({jam}). Isi teks dikirim ke DeepSeek untuk diolah.",
            $"Rough estimate: ±{t.Angka(masuk)} tokens in, ±{t.Angka(keluar)} tokens out, cost ±{Dolar(t, biaya)} with {model} ({jam}). The text is sent to DeepSeek to be processed."]}</p>""";
    }

    string TanpaKunci(Teks t) => Pengaturan.Kunci is not null ? "" :
        $"""<p class="pesan">{t[$"Belum ada kunci API DeepSeek. Atur dulu di <a href=\"{AlamatAi}\">Asisten AI</a>.",
            $"There's no DeepSeek API key yet. Set one on the <a href=\"{AlamatAi}\">AI assistant</a> page first."]}</p>""";

    string InfoModel(Teks t) =>
        $"""<p class="catatan">{t["Model:", "Model:"]} {HtmlEncode(NamaModel(t, Pengaturan.Model))} · <a href="{AlamatAi}">{t["ubah", "change"]}</a></p>""";

    static string NamaModel(Teks t, string model) => model switch
    {
        "deepseek-flash" => t["DeepSeek Flash: ±4× lebih murah", "DeepSeek Flash: about 4× cheaper"],
        _ => t["DeepSeek V4 Pro: lebih teliti memilah mana yang penting", "DeepSeek V4 Pro: better at judging what matters"],
    };

    // "$0,021" atau "$0.021"
    internal static string Dolar(Teks t, double jumlah)
    {
        var teks = jumlah < 0.001 ? "<$0.001" : "$" + jumlah.ToString(jumlah < 1 ? "0.000" : "0.00", CultureInfo.InvariantCulture);
        return HtmlEncode(t.Inggris ? teks : teks.Replace('.', ','));
    }

    string Kerangka(Teks t, string judul, string isi) => $"""
        {HalamanBelajar.Jejak(t, null)}
        <h1>{judul}</h1>
        {isi}
        """;
}
