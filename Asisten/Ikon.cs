namespace KevinBrowser.Asisten;

/// <summary>
/// Ikon garis kecil untuk halaman Belajar: SVG sebaris, tanpa berkas gambar
/// atau font ikon, masing-masing beberapa ratus bait. Digambar sendiri
/// (bentuk dasar di kotak 24×24) supaya tidak membawa lisensi pihak ketiga.
/// Warnanya ikut warna teks; ukurannya di kelas <c>ikon</c> (gaya.css).
/// </summary>
static class Ikon
{
    const string Awal = """<svg class="ikon" viewBox="0 0 24 24" aria-hidden="true">""";
    const string Akhir = "</svg>";

    public const string Cari = Awal + """<circle cx="11" cy="11" r="6.5"/><path d="M16 16l4.5 4.5"/>""" + Akhir;

    /// <summary>Gelembung obrolan.</summary>
    public const string Tanya = Awal + """<path d="M5 5h14a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-8l-5 4v-4H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z"/><path d="M8 9.5h8M8 12.5h5"/>""" + Akhir;

    /// <summary>Dokumen dengan tanda tambah: materi jadi catatan.</summary>
    public const string Serap = Awal + """<path d="M14 3H7a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V7z"/><path d="M14 3v4h4M9.5 14h5M12 11.5v5"/>""" + Akhir;

    /// <summary>Pensil.</summary>
    public const string Tulis = Awal + """<path d="M15.5 4.5l4 4L8 20H4v-4z"/><path d="M13 7l4 4"/>""" + Akhir;

    public const string Kembali = Awal + """<path d="M19 12H5M11 18l-6-6 6-6"/>""" + Akhir;

    /// <summary>Pesawat kertas.</summary>
    public const string Kirim = Awal + """<path d="M21 3L10 14"/><path d="M21 3l-7 18-4-7-7-4z"/>""" + Akhir;

    public const string Baru = Awal + """<path d="M12 5v14M5 12h14"/>""" + Akhir;

    public const string Kunci = Awal + """<circle cx="8" cy="15" r="4"/><path d="M11 12l9-9M16 7l3 3"/>""" + Akhir;

    public const string Folder = Awal + """<path d="M3 7a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z"/>""" + Akhir;
}
