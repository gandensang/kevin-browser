namespace KevinBrowser.Linux;

/// <summary>
/// Bagian WebKit yang dipakai bersama semua tab: satu WebContext (proses
/// web), satu NetworkSession (proses jaringan: cookie, cache disk), satu
/// Settings. Semua pengaturan hemat-RAM yang berlaku global ada di sini.
/// </summary>
static class Mesin
{
    static WebKit.WebContext konteks = null!;
    static WebKit.Settings setelanWeb = null!;
    // Dipegang selama browser hidup: WebKit memakai pointernya di setiap
    // navigasi (lihat KebijakanSitus).
    static WebKit.WebsitePolicies? tanpaPutarOtomatis, denganPutarOtomatis;

    public static WebKit.NetworkSession Sesi { get; private set; } = null!;
    public static Riwayat Riwayat { get; private set; } = null!;
    public static DaftarBookmark Bookmark { get; private set; } = null!;
    public static TabTerbuka TabTerbuka { get; private set; } = null!;
    public static Preferensi Preferensi { get; private set; } = null!;
    public static ILayanan Layanan { get; private set; } = null!;

    /// <summary>
    /// KEVIN_BROWSER_PUTAR_OTOMATIS=1: video boleh berputar sendiri di semua
    /// situs, seperti bawaan WebKit (untuk membandingkan).
    /// </summary>
    static readonly bool CegahPutarOtomatis = Environment.GetEnvironmentVariable("KEVIN_BROWSER_PUTAR_OTOMATIS") != "1";

    public static void Siapkan(Setelan setelan)
    {
        using var tekanan = WebKit.MemoryPressureSettings.New();
        tekanan.SetMemoryLimit(setelan.BatasMemoriMB);
        // Untuk mengukur: seberapa sering WebKit memeriksa RAM proses dan,
        // di atas ambang, membuang memori (bawaan 30 detik).
        if (int.TryParse(Environment.GetEnvironmentVariable("KEVIN_BROWSER_TEKANAN_DETIK"), out var detik) && detik > 0)
            tekanan.SetPollInterval(detik);

        // Proses jaringan. Harus sebelum NetworkSession pertama dibuat.
        WebKit.NetworkSession.SetMemoryPressureSettings(tekanan);

        // Model cache WebKit berlaku global, dan kapasitas cache proses dihitung
        // SEKALI saat WebContext dibuat — jadi harus diset sebelum konteks di
        // bawah dibuat. Kalau sesudahnya, cache proses memakai bawaan
        // WebBrowser: proses situs yang ditinggalkan disimpan untuk dipakai
        // ulang (menurut kode WebKit: 4 proses per GB RAM, masing-masing 5
        // menit). Terukur, satu tab example.com → wikipedia → iana → detik:
        // tersisa 6 proses web tanpa urutan ini, 3 dengan urutan ini.
        // DocumentBrowser: cache memori lebih kecil, cache disk tetap ada
        // (DocumentViewer mematikan cache disk juga → semua diunduh ulang).
        WebKit.WebContext.GetDefault().SetCacheModel(WebKit.CacheModel.DocumentBrowser);

        // Proses web. Hanya bisa lewat properti konstruksi WebContext.
        var nilai = new GObject.Value(WebKit.MemoryPressureSettings.GetGType());
        nilai.SetBoxed(tekanan.Handle.DangerousGetHandle());
        using (var arg = new GObject.ConstructArgument("memory-pressure-settings", nilai))
            konteks = WebKit.WebContext.NewWithProperties([arg]);

        // Halaman bawaan kevin://. Lokal: halaman web biasa tidak bisa membuka
        // atau menyematkannya; yang bisa hanya pemakai (kotak alamat, tombol
        // beranda) dan halaman kevin:// sendiri.
        konteks.RegisterUriScheme(HalamanBawaan.Skema, SajikanHalaman);
        konteks.GetSecurityManager().RegisterUriSchemeAsLocal(HalamanBawaan.Skema);

        // Notifikasi WhatsApp Web (IzinNotifikasi). Dipancarkan setiap proses
        // web baru akan dibuat; izinnya terbawa ke proses itu. Notifikasinya
        // ditampilkan WebKit sendiri lewat D-Bus (org.freedesktop.Notifications).
        Sinyal.Sambung(konteks, "initialize-notification-permissions", () =>
            Asli.SetelIzinNotifikasi(konteks.Handle.DangerousGetHandle(), IzinNotifikasi.Asal));

        if (CegahPutarOtomatis)
        {
            tanpaPutarOtomatis = KebijakanPutar(WebKit.AutoplayPolicy.Deny);
            denganPutarOtomatis = KebijakanPutar(WebKit.AutoplayPolicy.AllowWithoutSound);
        }

        var folderData = Path.Combine(GLib.Functions.GetUserDataDir(), Profil);
        var folderCache = Path.Combine(GLib.Functions.GetUserCacheDir(), Profil);
        Sesi = WebKit.NetworkSession.New(folderData, folderCache);

        // Tanpa ini cookie hanya ada di memori: login hilang setiap browser
        // ditutup. NetworkSession.New tidak menyimpannya sendiri (terbukti:
        // folder data tidak pernah berisi berkas cookie).
        Sesi.GetCookieManager().SetPersistentStorage(
            Path.Combine(folderData, "cookies.sqlite"), WebKit.CookiePersistentStorage.Sqlite);

        Riwayat = new Riwayat(Path.Combine(folderData, "riwayat.tsv"));
        Riwayat.Rapikan(DateTimeOffset.UtcNow);
        Bookmark = new DaftarBookmark(Path.Combine(folderData, "bookmark.tsv"));
        TabTerbuka = new TabTerbuka(Path.Combine(folderData, "tab.tsv"));
        Preferensi = new Preferensi(BerkasPreferensi(folderData));
        Layanan = new LayananLinux(Riwayat, Bookmark, Preferensi, folderData, folderCache);

        setelanWeb = WebKit.Settings.New();
        // Halaman yang ditinggalkan tidak disimpan utuh di memori untuk tombol
        // mundur; mundur memuat ulang (dari cache disk).
        setelanWeb.SetEnablePageCache(false);
        setelanWeb.SetEnableDnsPrefetching(false);

        // Lihat tabel ukur di Lingkungan.
        setelanWeb.SetHardwareAccelerationPolicy(Lingkungan.Gambar == ModeGambar.Cpu
            ? WebKit.HardwareAccelerationPolicy.Never
            : WebKit.HardwareAccelerationPolicy.Always);

#if DEBUG
        // Uji izin kamera/mikrofon tanpa menyalakan perangkat sungguhan:
        // WebKit memakai kamera dan mikrofon tiruan.
        if (Environment.GetEnvironmentVariable("KEVIN_BROWSER_UJI_PERANGKAT_TIRUAN") == "1")
        {
            var fitur = WebKit.Settings.GetAllFeatures();
            for (uint i = 0; i < fitur.GetLength(); i++)
                if (fitur.Get(i).GetIdentifier() == "MockCaptureDevices")
                    setelanWeb.SetFeatureEnabled(fitur.Get(i), true);
        }
        if (Environment.GetEnvironmentVariable("KEVIN_BROWSER_UJI_UA") is { Length: > 0 } ua)
            setelanWeb.SetUserAgent(ua);
#endif

        // Terakhir: menjalankan loop GLib sebentar sampai filter termuat, jadi
        // semua yang di atas harus sudah siap kalau ada sinyal yang masuk.
        Penyaring.Mulai(folderData);
    }

    /// <summary>
    /// WebView baru. WebView untuk sinyal create (window.open, target=_blank)
    /// wajib dibuat dengan <paramref name="terkait"/> = pembukanya
    /// (properti related-view), supaya window.opener tersambung.
    /// </summary>
    public static WebKit.WebView BuatWebView(WebKit.UserContentManager konten, WebKit.WebView? terkait)
    {
        List<GObject.ConstructArgument> arg = terkait is null
            ?
            [
                Objek("web-context", konteks),
                Objek("network-session", Sesi),
                Objek("settings", setelanWeb),
                Objek("user-content-manager", konten),
            ]
            :
            [
                Objek("related-view", terkait),
                Objek("settings", setelanWeb),
                Objek("user-content-manager", konten),
            ];
        // Kebijakan bawaan WebView: untuk muatan yang tidak lewat
        // KebijakanSitus (decide-policy).
        if (tanpaPutarOtomatis is not null)
            arg.Add(Objek("website-policies", tanpaPutarOtomatis));

        var web = WebKit.WebView.NewWithProperties([.. arg]);
        foreach (var a in arg)
            a.Dispose();
        return web;
    }

    /// <summary>
    /// Kebijakan situs (WebKitWebsitePolicies) untuk navigasi ke
    /// <paramref name="uri"/>, dipakai di decide-policy; 0 = biarkan bawaan
    /// WebKit (KEVIN_BROWSER_PUTAR_OTOMATIS=1). Kebijakan ini menempel ke
    /// dokumen yang dimuat. Yang menentukan video di halaman, termasuk di
    /// dalam iframe, adalah kebijakan dokumen bingkai utama
    /// (Document::videoPlaybackRequiresUserGesture membaca topDocument).
    /// Jadi kebijakan yang ikut terpasang ke navigasi iframe tidak
    /// berpengaruh, dan iframe YouTube di situs berita tetap tidak berputar
    /// sendiri.
    /// </summary>
    public static IntPtr KebijakanSitus(string uri) =>
        (PutarOtomatis.Boleh(uri) ? denganPutarOtomatis : tanpaPutarOtomatis)?.Handle.DangerousGetHandle() ?? IntPtr.Zero;

    // Deny: video tanpa suara pun menunggu klik. AllowWithoutSound: bawaan
    // WebKit, video tanpa suara boleh berputar sendiri, video bersuara
    // menunggu klik.
    static WebKit.WebsitePolicies KebijakanPutar(WebKit.AutoplayPolicy aturan)
    {
        var nilai = new GObject.Value(new GObject.Type(Asli.webkit_autoplay_policy_get_type()));
        nilai.SetEnum((int)aturan);
        using var arg = new GObject.ConstructArgument("autoplay", nilai);
        return WebKit.WebsitePolicies.NewWithProperties([arg]);
    }

    // async: menghapus data WebKit (halaman Pengaturan) baru selesai lewat
    // callback. Permintaan harus selalu diselesaikan; kalau tidak, tabnya
    // menunggu selamanya.
    static async void SajikanHalaman(WebKit.URISchemeRequest permintaan)
    {
        var uri = permintaan.GetUri();
        byte[] isi;
        string jenis;
        try
        {
            (isi, jenis) = await HalamanBawaan.Ambil(uri, Layanan);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[kevin-browser] gagal membuat {uri}: {e}");
            var pesan = Preferensi.Teks["Halaman ini gagal dibuat.", "This page could not be created."];
            (isi, jenis) = (System.Text.Encoding.UTF8.GetBytes($"<meta charset=\"utf-8\"><p>{pesan}</p>"), "text/html");
        }

        using var data = GLib.Bytes.New(isi);
        using var aliran = Gio.MemoryInputStream.NewFromBytes(data);
        permintaan.Finish(aliran, isi.Length, jenis);
        permintaan.Dispose();
    }

    static GObject.ConstructArgument Objek(string nama, GObject.Object objek) =>
        new(nama, new GObject.Value(objek));

    /// <summary>Nama folder profil, di folder data dan folder cache pemakai.</summary>
    const string Profil = "kevin-browser" + (Debug ? "-debug" : "");

    static string BerkasPreferensi(string folderData) => Path.Combine(folderData, "preferensi.tsv");

    /// <summary>
    /// Bahasa pilihan pemakai sebelum GTK dan WebKit dimuat (pesan galat di
    /// Program). Folder data dihitung tanpa GLib: .NET memakai aturan XDG yang
    /// sama ($XDG_DATA_HOME, kalau tidak ada ~/.local/share).
    /// </summary>
    public static Bahasa BahasaAwal() => new Preferensi(BerkasPreferensi(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Profil))).Bahasa;

#if DEBUG
    const bool Debug = true;
#else
    const bool Debug = false;
#endif
}
