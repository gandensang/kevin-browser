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

    /// <summary>Panah ke bawah ke baki: mengambil partai.</summary>
    public const string Unduh = Awal + """<path d="M12 4v11M7 10l5 5 5-5M5 20h14"/>""" + Akhir;

    /// <summary>Papan klip: menempel PGN.</summary>
    public const string Tempel = Awal + """<path d="M9 4h6v3H9z"/><path d="M9 5.5H6a1 1 0 0 0-1 1V20a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6.5a1 1 0 0 0-1-1h-3"/>""" + Akhir;

    public const string KeAwal = Awal + """<path d="M6 5v14M18 6l-7 6 7 6"/>""" + Akhir;

    public const string Mundur = Awal + """<path d="M15 6l-6 6 6 6"/>""" + Akhir;

    public const string Maju = Awal + """<path d="M9 6l6 6-6 6"/>""" + Akhir;

    public const string KeAkhir = Awal + """<path d="M18 5v14M6 6l7 6-7 6"/>""" + Akhir;

    /// <summary>Dua panah berlawanan: membalik papan.</summary>
    public const string Balik = Awal + """<path d="M7 4v14M3.5 14.5L7 18l3.5-3.5M17 20V6M13.5 9.5L17 6l3.5 3.5"/>""" + Akhir;

    /// <summary>Buku terbuka: catatan.</summary>
    public const string Buku = Awal + """<path d="M3 5h6a3 3 0 0 1 3 3v12a2 2 0 0 0-2-2H3z"/><path d="M21 5h-6a3 3 0 0 0-3 3v12a2 2 0 0 1 2-2h7z"/>""" + Akhir;

    /// <summary>Kotak dengan panah keluar: membuka situs lain.</summary>
    public const string Keluar = Awal + """<path d="M14 4h6v6M20 4l-9 9"/><path d="M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"/>""" + Akhir;
}
