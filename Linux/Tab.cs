namespace KevinBrowser.Linux;

/// <summary>
/// Satu tab. WebView-nya bisa ditidurkan (<see cref="Tidurkan"/>) untuk
/// menghemat RAM: alamat, judul, zoom, dan riwayat mundur/maju disimpan,
/// lalu dipulihkan oleh <see cref="Bangunkan"/> saat tab dilihat lagi.
/// </summary>
sealed class Tab : ITab
{
    // Penanda ketikan: sekali pemakai mengetik di halaman, tab ini tidak
    // ditidurkan sampai pindah halaman — isian formulir yang belum terkirim
    // hilang kalau WebView-nya dihancurkan. Berjalan di dunia JS terpisah,
    // jadi halaman tidak bisa melihat atau memanggilnya.
    const string DuniaJs = "kevin-browser";
    static readonly WebKit.UserScript SkripKetikan = WebKit.UserScript.NewForWorld(
        """
        addEventListener('input', function tandai(e) {
          if (!e.isTrusted) return;
          removeEventListener('input', tandai, true);
          webkit.messageHandlers.ketikan.postMessage(0);
        }, true);
        """,
        WebKit.UserContentInjectedFrames.AllFrames,
        WebKit.UserScriptInjectionTime.Start,
        DuniaJs, null, null);

    // Parameter pelacak di alamat halaman yang dibuka dari dalam halaman lain
    // (klik, pengalihan), sebelum skrip halaman itu bisa membacanya.
    static readonly WebKit.UserScript SkripPembersih = WebKit.UserScript.NewForWorld(
        PembersihAlamat.Skrip,
        WebKit.UserContentInjectedFrames.TopFrame,
        WebKit.UserScriptInjectionTime.Start,
        DuniaJs, null, null);

    static readonly WebKit.UserScript SkripYouTubeRingan = WebKit.UserScript.NewForWorld(
        YouTubeRingan.Skrip,
        WebKit.UserContentInjectedFrames.TopFrame,
        WebKit.UserScriptInjectionTime.Start,
        DuniaJs, YouTubeRingan.Situs, YouTubeRingan.Kecuali);

    // Dua skrip di bawah berjalan di dunia JS halaman: harus menimpa milik halaman.
    static readonly WebKit.UserScript SkripPutarDiLatar = WebKit.UserScript.New(
        YouTubeRingan.SkripPutarDiLatar,
        WebKit.UserContentInjectedFrames.TopFrame,
        WebKit.UserScriptInjectionTime.Start,
        YouTubeRingan.SitusMobile, null);

    static readonly WebKit.UserScript SkripH264 = WebKit.UserScript.New(
        PenguraiVideo.SkripYouTube,
        WebKit.UserContentInjectedFrames.AllFrames,
        WebKit.UserScriptInjectionTime.Start,
        PenguraiVideo.SitusYouTube, null);

    readonly Jendela jendela;
    readonly Gtk.Label labelJudul;
    readonly Gtk.Image ikonPerangkat;
    readonly Gtk.Button tombolTutup;
    readonly Gtk.GestureClick klikTengah;
    WebKit.UserContentManager? konten;
    WebKit.WebViewSessionState? riwayat;
    string? uriTerakhirDicatat;
    bool adaKetikan;
    double zoom = 1;

    public Gtk.Box Wadah { get; } = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
    public Gtk.Box Label { get; } = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
    public WebKit.WebView? Web { get; private set; }
    public string Uri { get; private set; }
    public string Judul { get; private set; } = "";
    public long TerakhirAktif { get; set; } = Environment.TickCount64;

    public bool Hidup => Web is not null;
    public bool Dilindungi => adaKetikan || Web?.IsPlayingAudio == true || MemakaiPerangkat || TidurTab.SelaluHidup(Uri);

    // Kamera, mikrofon, atau layar sedang dipakai (juga saat dibisukan):
    // tab yang ditidurkan memutus rekaman pesan suara.
    bool MemakaiPerangkat => Web is { } web
        && (web.GetCameraCaptureState() != WebKit.MediaCaptureState.None
            || web.GetMicrophoneCaptureState() != WebKit.MediaCaptureState.None
            || web.GetDisplayCaptureState() != WebKit.MediaCaptureState.None);

    /// <summary>Tab baru yang belum diisi alamat — belum punya proses web sama sekali.</summary>
    public bool Kosong => Web is null && Uri.Length == 0;

    public string JudulTampil =>
        Judul.Length > 0 ? Judul : Uri.Length > 0 ? Uri : Mesin.Preferensi.Teks["Tab baru", "New tab"];

    public Tab(Jendela jendela, string uri)
    {
        this.jendela = jendela;
        Uri = uri;

        labelJudul = Gtk.Label.New(JudulTampil);
        labelJudul.SetEllipsize(Pango.EllipsizeMode.End);
        labelJudul.SetWidthChars(12);
        labelJudul.SetMaxWidthChars(24);
        labelJudul.SetXalign(0);

        // Izin kamera/mikrofon diberikan tanpa bertanya (IzinMedia), jadi
        // pemakai harus bisa melihat kapan perangkatnya menyala.
        ikonPerangkat = Gtk.Image.New();
        ikonPerangkat.SetVisible(false);

        tombolTutup = Gtk.Button.NewFromIconName("window-close-symbolic");
        tombolTutup.SetHasFrame(false);
        tombolTutup.SetTooltipText(Mesin.Preferensi.Teks["Tutup tab (Ctrl+W)", "Close tab (Ctrl+W)"]);
        Sinyal.Sambung(tombolTutup, "clicked", () => jendela.TutupTab(this));

        // Klik tengah di judul tab = tutup, seperti peramban lain.
        klikTengah = Gtk.GestureClick.New();
        klikTengah.SetButton(2);
        Sinyal.Sambung(klikTengah, "pressed", (int _, double _, double _) => jendela.TutupTab(this));
        Label.AddController(klikTengah);

        Label.Append(ikonPerangkat);
        Label.Append(labelJudul);
        Label.Append(tombolTutup);
    }

    /// <summary>
    /// Tab yang dibuka lagi (tab dari sesi sebelumnya, Ctrl+Shift+T): tidur,
    /// tanpa proses web, sampai dilihat. Riwayat mundur/majunya ikut kembali.
    /// </summary>
    public Tab(Jendela jendela, TabTersimpan simpanan) : this(jendela, simpanan.Uri)
    {
        Judul = simpanan.Judul;
        if (simpanan.Riwayat is { } data)
        {
            using var bytes = GLib.Bytes.New(data);
            var dibaca = WebKit.WebViewSessionState.New(bytes);
            // Data rusak: WebKit mengembalikan NULL, tetapi GirCore tetap
            // membungkusnya, dan memulihkannya memicu "assertion 'state'
            // failed" (terjadi). Tab dibuka di alamatnya saja.
            if (dibaca.Handle.DangerousGetHandle() == IntPtr.Zero)
            {
                dibaca.Dispose();
                Catat.Tulis($"riwayat tab tidak terbaca: {Uri}");
            }
            else
                riwayat = dibaca;
        }
        labelJudul.AddCssClass("dim-label");
        PerbaruiLabel();
    }

    /// <summary>
    /// Alamat, judul, dan riwayat mundur/maju tab ini, untuk dibuka lagi
    /// nanti; null untuk tab kosong.
    /// </summary>
    public TabTersimpan? Simpan()
    {
        if (Kosong)
            return null;
        using var sesiHidup = Web?.GetSessionState();
        using var bytes = (sesiHidup ?? riwayat)?.Serialize();
        var data = bytes is null ? null : Asli.IsiBytes(bytes.Handle.DangerousGetHandle()).ToArray();
        return new TabTersimpan(Uri, Judul, data);
    }

    /// <summary>Bahasa tampilan diganti: teks label dan tombol tab ini.</summary>
    public void TerapkanBahasa()
    {
        tombolTutup.SetTooltipText(Mesin.Preferensi.Teks["Tutup tab (Ctrl+W)", "Close tab (Ctrl+W)"]);
        PerbaruiLabel();
        PerbaruiIkonPerangkat();
    }

    /// <summary>Memuat alamat di tab ini, membuat WebView kalau belum ada.</summary>
    public void Muat(string uri)
    {
        Uri = uri;
        LepasRiwayat();
        (Web ?? Pasang(null)).LoadUri(uri);
    }

    public void Bangunkan()
    {
        if (Web is not null || Uri.Length == 0)
            return;

        Catat.Tulis($"bangun: {Uri}");
        var web = Pasang(null);
        if (riwayat is not null)
        {
            web.RestoreSessionState(riwayat);
            LepasRiwayat();
            if (web.GetBackForwardList().GetCurrentItem() is { } item)
            {
                web.GoToBackForwardListItem(item);
                return;
            }
        }
        web.LoadUri(Uri);
    }

    /// <summary>
    /// Menghancurkan WebView; proses web-nya ikut berhenti kalau tidak
    /// dipakai tab lain.
    /// </summary>
    public void Tidurkan()
    {
        if (Web is not { } web)
            return;

        Catat.Tulis($"tidur: {Uri}");
        LepasRiwayat();
        riwayat = web.GetSessionState();
        LepasWebView();
    }

    public void Tutup()
    {
        Catat.Tulis($"tutup: {Uri}");
        LepasWebView();
        LepasRiwayat();
        klikTengah.Dispose();
        ikonPerangkat.Dispose();
        tombolTutup.Dispose();
        labelJudul.Dispose();
        Label.Dispose();
        Wadah.Dispose();
    }

    /// <summary>Daftar pemblokir diperbarui: berlaku untuk halaman berikutnya yang dimuat tab ini.</summary>
    public void PasangUlangPenyaring()
    {
        if (konten is null)
            return;
        Asli.webkit_user_content_manager_remove_all_filters(konten.Handle.DangerousGetHandle());
        Penyaring.PasangKe(konten);
    }

    /// <summary>Tab untuk window.open / target=_blank: WebView-nya harus terkait pembukanya.</summary>
    public void PasangTerkait(WebKit.WebView pembuka) => Pasang(pembuka);

    public void Zoom(double langkah)
    {
        zoom = langkah == 0 ? 1 : Math.Clamp(zoom + langkah, 0.3, 3);
        Web?.SetZoomLevel(zoom);
    }

    WebKit.WebView Pasang(WebKit.WebView? terkait)
    {
        konten = WebKit.UserContentManager.New();
        konten.AddScript(SkripKetikan);
        if (Penyaring.Nyala)
            konten.AddScript(SkripPembersih);
        konten.AddScript(SkripYouTubeRingan);
        konten.AddScript(SkripPutarDiLatar);
        if (PenguraiVideo.PaksaH264)
            konten.AddScript(SkripH264);
        konten.RegisterScriptMessageHandler("ketikan", DuniaJs);
        Penyaring.PasangKe(konten);
        Sinyal.Sambung(konten, "script-message-received::ketikan", (IntPtr _) =>
        {
            adaKetikan = true;
            Catat.Tulis($"ada ketikan, dilindungi: {Uri}");
        });

        var web = Mesin.BuatWebView(konten, terkait);
        web.SetVexpand(true);
        web.SetZoomLevel(zoom);

        Sinyal.Sambung(web, "notify::title", (IntPtr _) =>
        {
            Judul = web.GetTitle() ?? "";
            PerbaruiLabel();
            jendela.TabBerubah(this);
            if (!web.IsLoading)
                CatatKunjungan(web);
        });
        Sinyal.Sambung(web, "notify::uri", (IntPtr _) =>
        {
            Uri = web.GetUri() ?? Uri;
            PerbaruiLabel();
            jendela.TabBerubah(this);
        });
        Sinyal.Sambung(web, "notify::estimated-load-progress", (IntPtr _) => jendela.TabBerubah(this));
        Sinyal.Sambung(web, "notify::is-loading", (IntPtr _) => jendela.TabBerubah(this));
        Sinyal.Sambung(web, "load-changed", (int peristiwa) =>
        {
            // Halaman baru: ketikan di halaman lama sudah hilang bersamanya.
            if (peristiwa == (int)WebKit.LoadEvent.Committed)
                adaKetikan = false;
            if (peristiwa == (int)WebKit.LoadEvent.Finished)
                CatatKunjungan(web);
            jendela.TabBerubah(this);
        });
        Sinyal.Sambung(web, "decide-policy", (IntPtr keputusan, int jenis) =>
            jendela.PutuskanNavigasi(keputusan, (WebKit.PolicyDecisionType)jenis));
        // Kembalian create: WebView baru, yang sudah dipegang tab-nya sendiri.
        Sinyal.Sambung(web, "create", (IntPtr _) =>
            jendela.BukaTabTerkait(this).Web!.Handle.DangerousGetHandle());
        Sinyal.Sambung(web, "ready-to-show", () => jendela.TampilkanTab(this));
        Sinyal.Sambung(web, "close", () => jendela.TutupTab(this));
        Sinyal.Sambung(web, "enter-fullscreen", () =>
        {
            jendela.LayarPenuh(true);
            return false;
        });
        Sinyal.Sambung(web, "leave-fullscreen", () =>
        {
            jendela.LayarPenuh(false);
            return false;
        });
        Sinyal.Sambung(web, "web-process-terminated", (int alasan) =>
            ProsesMati((WebKit.WebProcessTerminationReason)alasan));
        Sinyal.Sambung(web, "permission-request", (IntPtr permintaan) => PutuskanIzin(web, permintaan));
        // Notifikasi (hanya situs IzinNotifikasi yang diizinkan) ditampilkan
        // WebKit sendiri. Kalau diklik, jendela dan tab ini ditampilkan;
        // halamannya juga menerima klik itu.
        Sinyal.Sambung(web, "show-notification", (IntPtr notifikasi) =>
        {
            Catat.Tulis($"notifikasi: {Uri}");
            Sinyal.Sambung(notifikasi, "clicked", () =>
            {
                Catat.Tulis($"notifikasi diklik: {Uri}");
                if (Web is null)
                    return;   // tab sudah ditutup
                jendela.TampilkanTab(this);
                jendela.Tampilkan();
            });
            return false;
        });
        var cari = web.GetFindController();
        // found-text tidak dipakai untuk jumlah: setelah SearchNext jumlahnya 1
        // (terjadi), jadi jumlah total dihitung terpisah (CountMatches).
        Sinyal.Sambung(cari, "counted-matches", (int jumlah) => jendela.HasilCari(this, jumlah));
        Sinyal.Sambung(cari, "failed-to-find-text", () => jendela.HasilCari(this, 0));
        foreach (var perangkat in new[] { "camera", "microphone", "display" })
            Sinyal.Sambung(web, $"notify::{perangkat}-capture-state", (IntPtr _) => PerbaruiIkonPerangkat());

#if DEBUG
        UjiJs(web);
#endif
        labelJudul.RemoveCssClass("dim-label");
        Wadah.Append(web);
        Web = web;
        return web;
    }

    // Satu entri per halaman: saat selesai dimuat, atau saat judul berubah
    // sesudah navigasi di dalam halaman (pindah video di YouTube tanpa memuat
    // ulang). Muat ulang dan bangun dari tidur tidak dicatat lagi.
    void CatatKunjungan(WebKit.WebView web)
    {
        var uri = web.GetUri();
        if (uri is null || uri == uriTerakhirDicatat)
            return;
        uriTerakhirDicatat = uri;
        Mesin.Riwayat.Catat(uri, web.GetTitle() ?? "", DateTimeOffset.Now);
    }

    void LepasWebView()
    {
        if (Web is not { } web)
            return;

        Uri = web.GetUri() ?? Uri;
        Wadah.Remove(web);
        web.Dispose();
        konten?.Dispose();
        Web = null;
        konten = null;
        adaKetikan = false;
        labelJudul.AddCssClass("dim-label");
        PerbaruiIkonPerangkat();
    }

    // Tanpa penangan ini WebKit juga menolak semuanya; di sini penolakannya
    // tertulis, dan pengecualiannya hanya kamera/mikrofon (IzinMedia) dan
    // notifikasi (IzinNotifikasi). Alamat yang dicek adalah bingkai utama:
    // iframe situs lain di dalam halaman WhatsApp ikut terizinkan, tetapi
    // WhatsApp di dalam iframe situs lain tidak.
    static bool PutuskanIzin(WebKit.WebView web, IntPtr permintaan)
    {
        var uri = web.GetUri();
        var (jenis, boleh) =
            Asli.IzinPerangkatMedia(permintaan) ? ("kamera/mikrofon", IzinMedia.Boleh(uri))
            : Asli.IzinNotifikasi(permintaan) ? ("notifikasi", IzinNotifikasi.Boleh(uri))
            : ("lain", false);
        if (boleh)
            Asli.webkit_permission_request_allow(permintaan);
        else
            Asli.webkit_permission_request_deny(permintaan);
        Catat.Tulis($"izin {jenis} {(boleh ? "diberikan" : "ditolak")}: {uri}");
        return true;
    }

    void PerbaruiIkonPerangkat()
    {
        var (ikon, teks, inggris) =
            Web is not { } web ? (null, null, null)
            : web.GetCameraCaptureState() != WebKit.MediaCaptureState.None ? ("camera-web-symbolic", "Kamera menyala", "Camera on")
            : web.GetMicrophoneCaptureState() != WebKit.MediaCaptureState.None ? ("audio-input-microphone-symbolic", "Mikrofon menyala", "Microphone on")
            : web.GetDisplayCaptureState() != WebKit.MediaCaptureState.None ? ("video-display-symbolic", "Layar sedang dibagikan", "Screen is being shared")
            : (null, null, null);
        var nyala = ikon is not null;
        if (nyala)
            ikonPerangkat.SetFromIconName(ikon);
        ikonPerangkat.SetTooltipText(teks is null ? null : Mesin.Preferensi.Teks[teks, inggris!]);
        // Log tetap berbahasa Indonesia, apa pun bahasa tampilannya.
        if (ikonPerangkat.GetVisible() != nyala)
            Catat.Tulis($"{teks ?? "kamera/mikrofon mati"}: {Uri}");
        ikonPerangkat.SetVisible(nyala);
    }

    // Riwayat dibebaskan di thread utama; kalau dibiarkan ke GC, finalizer
    // .NET membebaskannya di thread lain.
    void LepasRiwayat()
    {
        riwayat?.Dispose();
        riwayat = null;
    }

    // Proses web mati (crash, atau dibunuh sistem karena RAM habis). Tab latar
    // cukup ditidurkan — dimuat ulang saat dilihat. Tab aktif tidak dimuat
    // ulang otomatis supaya halaman yang crash tidak berputar terus.
    void ProsesMati(WebKit.WebProcessTerminationReason alasan)
    {
        Catat.Tulis($"proses web mati ({alasan}): {Uri}");
        if (alasan == WebKit.WebProcessTerminationReason.TerminatedByApi || Web is not { } web)
            return;

        if (!jendela.ApakahAktif(this))
        {
            Tidurkan();
            return;
        }
        var t = Mesin.Preferensi.Teks;
        web.LoadAlternateHtml(
            $"""
            <meta charset="utf-8">
            <body style="font:16px sans-serif;margin:3em;color:#444">
            <h2>{t["Halaman ini berhenti", "This page stopped"]}</h2>
            <p>{t["Mungkin kehabisan memori. Tekan F5 untuk memuat ulang.", "It may have run out of memory. Press F5 to reload."]}</p>
            """,
            Uri, null);
    }

    void PerbaruiLabel()
    {
        labelJudul.SetText(JudulTampil);
        labelJudul.SetTooltipText(JudulTampil);
    }

#if DEBUG
    // Uji tanpa klik (build Debug saja): isi env KEVIN_BROWSER_UJI_JS dijalankan
    // sekali, di halaman pertama yang selesai dimuat. Skrip dari API TIDAK
    // dihitung gestur pemakai: popup diblokir dan klik sintetis tidak membawa
    // tombol tengah. Untuk itu perlu klik X11 sungguhan (scripts/klik.py).
    // KEVIN_BROWSER_UJI_JS_SETIAP=1: dijalankan lagi setiap halaman di tab itu
    // selesai dimuat. Perlu untuk YouTube, yang memuat ulang dirinya
    // (&themeRefresh=1) setelah pemuatan pertama, sehingga skrip sekali-jalan
    // ikut hilang. Jangan dipakai dengan skrip yang berpindah halaman.
    static bool ujiJsSudah;

    static void UjiJs(WebKit.WebView web)
    {
        if (ujiJsSudah || Environment.GetEnvironmentVariable("KEVIN_BROWSER_UJI_JS") is not { Length: > 0 } js)
            return;
        var setiap = Environment.GetEnvironmentVariable("KEVIN_BROWSER_UJI_JS_SETIAP") == "1";
        Sinyal.Sambung(web, "load-changed", (int peristiwa) =>
        {
            if (peristiwa != (int)WebKit.LoadEvent.Finished || (ujiJsSudah && !setiap))
                return;
            ujiJsSudah = true;
            web.EvaluateJavascriptAsync(js);
        });
    }
#endif
}
