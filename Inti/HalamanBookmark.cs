using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser;

/// <summary>
/// Bookmark di halaman bawaan: kotak pintasan di beranda, dan
/// kevin://bookmark untuk menghapus dan mengganti nama. Menyimpan bookmark
/// hanya lewat browser (Ctrl+D, tombol bintang), tidak lewat alamat, jadi
/// halaman tidak bisa menambah bookmark sendiri. Judul berasal dari situs,
/// jadi semua teks di-escape.
/// </summary>
static class HalamanBookmark
{
    // Beranda tetap ringkas; sisanya di kevin://bookmark.
    const int PintasanMaks = 12;

    /// <summary>Untuk beranda, di bawah kotak cari.</summary>
    public static string Pintasan(ILayanan layanan)
    {
        var t = layanan.Preferensi.Teks;
        var semua = layanan.Bookmark.Semua();
        if (semua.Count == 0)
            return t[
                """
                <p class="catatan tengah">Tekan <kbd>Ctrl</kbd>+<kbd>D</kbd> di sebuah halaman
                untuk menyimpannya ke sini.</p>
                """,
                """
                <p class="catatan tengah">Press <kbd>Ctrl</kbd>+<kbd>D</kbd> on a page
                to save it here.</p>
                """];

        var isi = new StringBuilder("""<nav class="pintasan" aria-label="Bookmark">""");
        foreach (var b in semua.Take(PintasanMaks))
            isi.Append($"""<a href="{HtmlEncode(b.Uri)}" title="{HtmlEncode(b.Nama)}">{Huruf(b)}<span>{HtmlEncode(b.Nama)}</span></a>""");
        isi.Append("</nav>");
        var kelola = semua.Count > PintasanMaks
            ? t[$"Semua bookmark ({semua.Count})", $"All bookmarks ({semua.Count})"]
            : t["Kelola bookmark", "Manage bookmarks"];
        isi.Append($"""<p class="catatan tengah"><a href="{HalamanBawaan.Bookmark}">{kelola}</a></p>""");
        return isi.ToString();
    }

    /// <summary>kevin://bookmark, dengan aksi hapus dan ganti nama bertoken sekali pakai.</summary>
    public static string Buat(Kueri kueri, ILayanan layanan)
    {
        var t = layanan.Preferensi.Teks;
        string? pesan = null;
        if (long.TryParse(kueri["hapus"], out var hapus) && TokenSekali.Pakai(kueri["token"]))
        {
            layanan.Bookmark.HapusSatu(hapus);
            pesan = t["Satu bookmark dihapus.", "One bookmark removed."];
        }
        else if (long.TryParse(kueri["ganti"], out var ganti) && TokenSekali.Pakai(kueri["token"]))
        {
            layanan.Bookmark.GantiNama(ganti, kueri["judul"] ?? "");
            pesan = t["Nama bookmark diubah.", "Bookmark renamed."];
        }

        var semua = layanan.Bookmark.Semua();
        var token = TokenSekali.Buat();
        var catatan = t[
            $"""
            Tekan <kbd>Ctrl</kbd>+<kbd>D</kbd> atau tombol bintang di sebelah kotak
            alamat untuk menyimpan halaman. {PintasanMaks} yang pertama tampil di
            <a href="{HalamanBawaan.Beranda}">beranda</a>.
            """,
            $"""
            Press <kbd>Ctrl</kbd>+<kbd>D</kbd> or the star button next to the address
            bar to save a page. The first {PintasanMaks} appear on the
            <a href="{HalamanBawaan.Beranda}">home page</a>.
            """];
        var isi = new StringBuilder($"""
            <h1>{t["Bookmark", "Bookmarks"]}</h1>
            {HalamanPengaturan.Pesan(pesan)}
            <p class="catatan">{catatan}</p>
            """);

        if (semua.Count == 0)
            return isi.Append($"<p>{t["Belum ada bookmark.", "No bookmarks yet."]}</p>").ToString();

        var labelNama = t["Nama bookmark", "Bookmark name"];
        var labelHapus = t["Hapus bookmark", "Remove bookmark"];
        isi.Append("""<ul class="bookmark">""");
        foreach (var b in semua)
            isi.Append($"""
                <li>{Huruf(b)}
                <form action="{HalamanBawaan.Bookmark}" method="get">
                <input type="hidden" name="ganti" value="{b.Id}"><input type="hidden" name="token" value="{token}">
                <input type="text" name="judul" value="{HtmlEncode(b.Judul)}" placeholder="{HtmlEncode(b.Host)}" aria-label="{labelNama}">
                <button class="tombol" type="submit">{t["Simpan", "Save"]}</button>
                </form>
                <a class="domain" href="{HtmlEncode(b.Uri)}" title="{HtmlEncode(b.Uri)}">{HtmlEncode(b.Host)}</a>
                <a class="hapus" href="{HalamanBawaan.Bookmark}?hapus={b.Id}&amp;token={token}" title="{labelHapus}" aria-label="{labelHapus}">×</a></li>
                """);
        return isi.Append("</ul>").ToString();
    }

    // Kotak huruf pertama nama situs, berselang terang/gelap seperti petak
    // catur. Tanpa ikon situs: tidak ada yang perlu diunduh atau disimpan.
    static string Huruf(Bookmark b)
    {
        var host = b.Host.StartsWith("www.", StringComparison.Ordinal) ? b.Host[4..] : b.Host;
        var huruf = host.Length > 0 ? char.ToUpperInvariant(host[0]).ToString() : "?";
        var terang = host.Sum(c => c) % 2 == 0 ? " terang" : "";
        return $"""<span class="huruf{terang}" aria-hidden="true">{HtmlEncode(huruf)}</span>""";
    }
}
