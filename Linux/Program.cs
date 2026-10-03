using System.Runtime.InteropServices;
using KevinBrowser;
using KevinBrowser.Linux;

// Laptop lain bisa belum punya GTK4/WebKitGTK 6.0. GirCore baru memuatnya
// saat dipakai, jadi tanpa pemeriksaan ini galatnya tertelan di handler
// startup: aplikasi hidup terus tanpa jendela, dan karena instans tunggal,
// membukanya lagi pun tidak memunculkan apa-apa (terlihat saat disimulasikan).
if (Lingkungan.PaketHilang() is { } paket)
{
    var t = new Teks(Mesin.BahasaAwal());
    var pesan = t[$"Pustaka {paket} belum terpasang. Pasang dengan:\n  sudo apt install {paket}",
        $"The {paket} library is not installed. Install it with:\n  sudo apt install {paket}"];
    Console.Error.WriteLine("kevin-browser: " + pesan);
    Lingkungan.Beritahu(t["Kevin Browser tidak bisa dibuka", "Kevin Browser cannot start"], pesan);
    return 1;
}

// GirCore memasang pemuat pustaka native (GTK, WebKit, GLib, …).
WebKit.Module.Initialize();

// Proses anak untuk memperbarui pemblokir iklan (lihat Penyaring.cs):
// tanpa GTK, tanpa jendela, selesai lalu keluar.
if (args is [Penyaring.ArgPerbarui, var folderPenyaring])
    return Penyaring.Perbarui(folderPenyaring);

Lingkungan.Siapkan();
var setelan = Setelan.Hitung(Lingkungan.RamTotalMB());

#if DEBUG
// Build Debug tidak mendaftar sebagai instans tunggal. Kalau mendaftar,
// `dotnet run` hanya mengoper alamatnya ke kevin-browser terpasang yang
// sedang terbuka, lalu langsung keluar.
const Gio.ApplicationFlags ModeInstans = Gio.ApplicationFlags.NonUnique;
#else
const Gio.ApplicationFlags ModeInstans = Gio.ApplicationFlags.FlagsNone;
#endif

// Instans tunggal: membuka kevin-browser lagi (atau xdg-open sebuah alamat)
// menambah tab di jendela yang sudah ada, bukan menyalakan WebKit kedua.
var app = Gtk.Application.New("lokal.kevin.Browser", Gio.ApplicationFlags.HandlesOpen | ModeInstans);
Jendela? jendela = null;

Sinyal.Sambung(app, "startup", () =>
{
    Mesin.Siapkan(setelan);
    Pemulung.Mulai();
});

// Saat browser baru dibuka (bukan alamat yang dioper ke jendela yang sudah
// ada), tab dari sesi sebelumnya ikut dibuka lagi, tidur, di kanan tab baru.
Sinyal.Sambung(app, "activate", () =>
{
    var baru = jendela is null;
    jendela ??= new Jendela(app, setelan);
    jendela.BukaTab(Alamat.Beranda);
    if (baru)
        jendela.PulihkanTab();
    jendela.Tampilkan();
});

Sinyal.Sambung(app, "open", (IntPtr berkas, int jumlah, IntPtr _) =>
{
    var baru = jendela is null;
    jendela ??= new Jendela(app, setelan);
    // Yang pertama langsung dilihat; sisanya tab latar (berjejer sesuai
    // urutan) yang baru dimuat saat dilihat, bukan menyalakan satu proses web
    // per alamat sekaligus.
    for (var i = 0; i < jumlah; i++)
    {
        var uri = Asli.g_file_get_uri(Marshal.ReadIntPtr(berkas, i * IntPtr.Size));
        jendela.BukaTab(Asli.Teks(uri), latar: i > 0);
        Asli.g_free(uri);
    }
    if (baru)
        jendela.PulihkanTab();
    jendela.Tampilkan();
});

// GApplication menganggap elemen pertama argv sebagai nama program.
var argumen = args.Select(a => Alamat.DariBarisPerintah(a, p => File.Exists(p) || Directory.Exists(p)));
return app.RunWithSynchronizationContext([Environment.ProcessPath ?? "kevin-browser", .. argumen]);
