using System.Net;
using System.Text;

namespace KevinBrowser;

/// <summary>
/// Halaman bawaan di skema <c>kevin://</c>: beranda, kisah, panduan, dan
/// seterusnya. Isinya tertanam di binary (folder Halaman/), jadi tetap satu
/// berkas dan tetap terbuka tanpa internet.
/// </summary>
/// <remarks>
/// Alamatnya <c>kevin://nama</c>. Nama tanpa titik adalah halaman: isi
/// Halaman/nama.html (Halaman/en/nama.html kalau bahasanya Inggris)
/// dipasang ke kerangka.html (kepala, menu, kaki). Nama bertitik adalah
/// berkas apa adanya, mis. <c>kevin://gaya.css</c>. Yang disajikan hanya
/// berkas yang tertanam, jadi tidak ada jalan ke berkas lain di disk.
/// Pengaturan, Riwayat, dan Bookmark dibuat saat diminta dari data
/// <see cref="ILayanan"/> (HalamanPengaturan.cs, dst). Belajar dibuat proyek
/// Asisten, lewat <see cref="ILayanan.Belajar"/>.
/// </remarks>
public static class HalamanBawaan
{
    public const string Skema = "kevin";
    public const string Beranda = "kevin://beranda";
    public const string Belajar = "kevin://belajar";
    public const string Pengaturan = "kevin://pengaturan";
    public const string Riwayat = "kevin://riwayat";
    public const string Bookmark = "kevin://bookmark";

    /// <summary>
    /// Versi rilis, mis. "0.1.0", dari &lt;Version&gt; di Directory.Build.props
    /// (yang juga dipakai paket .deb). Tampil di kaki setiap halaman.
    /// </summary>
    public static string Versi { get; } = typeof(HalamanBawaan).Assembly.GetName().Version?.ToString(3) ?? "";

    readonly record struct Butir(string Nama, string Judul, string JudulInggris, string? Kelas = null);

    // Kepala: halaman utama, fitur khusus Kevin Browser (Belajar), dan tempat
    // untuk fitur berikutnya (catur, asisten, …). Sampai fiturnya ada, tempat
    // itu menunjuk ke halaman Rencana.
    static readonly Butir[] MenuKepala =
    [
        new("beranda", "Beranda", "Home"),
        new("belajar", "Belajar", "Learn"),
        new("rencana", "Segera hadir", "Coming soon", "segera"),
        new("pengaturan", "Pengaturan", "Settings"),
    ];

    // Kaki: tentang browser ini. Semuanya halaman tetap dari berkas.
    static readonly Butir[] MenuKaki =
    [
        new("kisah", "Kisah", "Story"),
        new("kelebihan", "Kelebihan & Kekurangan", "Pros & Cons"),
        new("panduan", "Panduan", "Guide"),
        new("rencana", "Rencana", "Plans"),
        new("tentang", "Tentang Kami", "About Us"),
    ];

    /// <summary>
    /// Isi dan tipe MIME untuk sebuah alamat <c>kevin://…</c>. Tanpa
    /// <paramref name="layanan"/>, Pengaturan dan Riwayat dianggap tidak ada
    /// dan bahasanya Indonesia. <paramref name="isiPost"/>: isi formulir POST.
    /// </summary>
    public static async Task<(byte[] Isi, string Jenis)> Ambil(string uri, ILayanan? layanan = null, string? isiPost = null)
    {
        var t = layanan?.Preferensi.Teks ?? new Teks(Bahasa.Indonesia);
        var nama = NamaDari(uri);

        // Berkas .html hanya lewat nama halamannya: mentahnya kerangka atau
        // potongan isi.
        if (nama.Contains('.'))
            return !nama.EndsWith(".html", StringComparison.Ordinal) && Baca(nama) is { } berkas
                ? (berkas, JenisBerkas(nama))
                : Halaman(t, null, t["Tidak ditemukan", "Not found"], TidakAda(t, uri));

        switch (nama)
        {
            case "pengaturan" when layanan is not null:
                var pengaturan = await HalamanPengaturan.Buat(new Kueri(uri), layanan);
                t = layanan.Preferensi.Teks;   // bahasanya mungkin baru saja diganti
                return Halaman(t, "pengaturan", t["Pengaturan", "Settings"], pengaturan);
            case "riwayat" when layanan is not null:
                return Halaman(t, "pengaturan", t["Riwayat", "History"], HalamanRiwayat.Buat(new Kueri(uri), layanan));
            case "bookmark" when layanan is not null:
                return Halaman(t, "beranda", t["Bookmark", "Bookmarks"], HalamanBookmark.Buat(new Kueri(uri), layanan));
            case "belajar" when layanan?.Belajar is { } belajar:
                var halaman = await belajar.Buat(uri, isiPost, t);
                return Halaman(t, "belajar", halaman.Judul, halaman.Isi, "belajar");
        }

        var butir = MenuKaki.Append(MenuKepala[0]).FirstOrDefault(m => m.Nama == nama);
        if (butir.Nama is null || IsiHalaman(t, nama) is not { } isi)
            return Halaman(t, null, t["Tidak ditemukan", "Not found"], TidakAda(t, uri));
        if (nama == "beranda" && layanan is not null)
            isi = isi.Replace("<!--bookmark-->", HalamanBookmark.Pintasan(layanan));
        return Halaman(t, nama, t[butir.Judul, butir.JudulInggris], isi, nama);
    }

    // Versi Inggris kalau ada; kalau belum, versi Indonesia (tes memastikan
    // semuanya ada).
    static string? IsiHalaman(Teks t, string nama) =>
        ((t.Inggris ? Baca($"en/{nama}.html") : null) ?? Baca(nama + ".html")) is { } isi
            ? Encoding.UTF8.GetString(isi)
            : null;

    // "kevin://Tentang/?x#y" → "tentang".
    static string NamaDari(string uri)
    {
        var sisa = uri.StartsWith("kevin:", StringComparison.OrdinalIgnoreCase) ? uri[6..] : uri;
        sisa = sisa.TrimStart('/');
        var akhir = sisa.IndexOfAny(['/', '?', '#']);
        return (akhir < 0 ? sisa : sisa[..akhir]).ToLowerInvariant();
    }

    // menuAktif: butir menu yang disorot, di kepala dan di kaki; Riwayat
    // menyorot Pengaturan.
    static (byte[], string) Halaman(Teks t, string? menuAktif, string judul, string isi, string kelas = "")
    {
        var html = Encoding.UTF8.GetString(Baca("kerangka.html")!)
            .Replace("{{bahasa}}", t.Kode)
            .Replace("{{judul}}", kelas == "beranda" ? "Kevin Browser" : $"{WebUtility.HtmlEncode(judul)} · Kevin Browser")
            .Replace("{{label-menu}}", t["Menu", "Menu"])
            .Replace("{{menu}}", Tautan(t, MenuKepala, menuAktif))
            .Replace("{{label-kaki}}", t["Tentang Kevin Browser", "About Kevin Browser"])
            .Replace("{{menu-kaki}}", Tautan(t, MenuKaki, menuAktif))
            .Replace("{{kaki}}", t[$"Kevin Browser versi {Versi} · 2026 · dibuat untuk Kevin dan laptop bekasnya",
                $"Kevin Browser version {Versi} · 2026 · made for Kevin and his second-hand laptop"])
            .Replace("{{kelas}}", kelas)
            .Replace("{{isi}}", isi);
        return (Encoding.UTF8.GetBytes(html), "text/html");
    }

    static string Tautan(Teks t, Butir[] menu, string? menuAktif)
    {
        var hasil = new StringBuilder();
        foreach (var m in menu)
            hasil.Append($"<a href=\"kevin://{m.Nama}\"")
                .Append(m.Kelas is null ? "" : $" class=\"{m.Kelas}\"")
                .Append(m.Nama == menuAktif ? " aria-current=\"page\"" : "")
                .Append('>')
                .Append(WebUtility.HtmlEncode(t[m.Judul, m.JudulInggris]))
                .Append("</a>");
        return hasil.ToString();
    }

    static string TidakAda(Teks t, string uri) =>
        $"""
        <h1>{t["Halaman tidak ditemukan", "Page not found"]}</h1>
        <p>{t["Alamat", "The address"]} <code>{WebUtility.HtmlEncode(uri)}</code> {t["tidak ada di Kevin Browser.", "does not exist in Kevin Browser."]}</p>
        <p><a href="{Beranda}">{t["Kembali ke beranda", "Back to the home page"]}</a></p>
        """;

    static byte[]? Baca(string berkas)
    {
        using var aliran = typeof(HalamanBawaan).Assembly.GetManifestResourceStream("Halaman/" + berkas);
        if (aliran is null)
            return null;
        using var salinan = new MemoryStream();
        aliran.CopyTo(salinan);
        return salinan.ToArray();
    }

    static string JenisBerkas(string nama) => Path.GetExtension(nama) switch
    {
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".js" => "text/javascript",
        _ => "application/octet-stream",
    };
}
