namespace KevinBrowser.Linux;

/// <summary>
/// Satu-satunya jendela: toolbar (mundur, maju, muat ulang, kotak alamat,
/// tab baru) di atas Gtk.Notebook berisi tab. Baris tab disembunyikan
/// selama hanya ada satu tab.
/// </summary>
sealed class Jendela
{
    // Skema yang ditangani WebKit sendiri. Selebihnya (mailto:, zoommtg:,
    // whatsapp:, …) diserahkan ke aplikasi lain lewat xdg-open.
    static readonly string[] SkemaWeb =
        ["http", "https", "file", "about", "data", "blob", "javascript", "webkit-pdfjs-viewer", HalamanBawaan.Skema, HalamanBawaan.SkemaMesin];

    readonly Gtk.Application app;
    readonly Setelan setelan;
    readonly Gtk.ApplicationWindow win;
    readonly Gtk.Box toolbar;
    readonly Gtk.Button mundur, maju, muatUlang, beranda, tabBaru, bintang;
    readonly Gtk.Button cariSebelum, cariBerikut, tutupCari;
    readonly Gtk.Entry alamat;
    readonly Gtk.Label status;
    readonly Gtk.Notebook buku;
    readonly Gtk.Box barisCari;
    readonly Gtk.SearchEntry kotakCari;
    readonly Gtk.Label hasilCari;
    readonly List<Tab> tabs = [];
    readonly TabTertutup tertutup = new();
    Tab? aktif;
    Tab? latarTerakhir;   // tab latar yang terakhir dibuka dari tab aktif
    bool alamatDiketik;   // fokus di kotak alamat: jangan timpa ketikan pemakai
    bool layarPenuh;
    uint pengingatStatus;

    public Jendela(Gtk.Application app, Setelan setelan)
    {
        this.app = app;
        this.setelan = setelan;

        win = Gtk.ApplicationWindow.New(app);
        win.SetTitle("kevin-browser");
        win.SetDefaultSize(1100, 720);

        // Teks tombol dan kotak (tooltip, placeholder) dipasang TerapkanBahasa.
        mundur = Tombol("go-previous-symbolic", () => aktif?.Web?.GoBack());
        maju = Tombol("go-next-symbolic", () => aktif?.Web?.GoForward());
        muatUlang = Tombol("view-refresh-symbolic", MuatUlangAtauBerhenti);
        beranda = Tombol("go-home-symbolic", () => Buka(Alamat.Beranda));
        tabBaru = Tombol("tab-new-symbolic", () => BukaTab(""));

        alamat = Gtk.Entry.New();
        alamat.SetHexpand(true);
        alamat.SetInputPurpose(Gtk.InputPurpose.Url);
        Sinyal.Sambung(alamat, "activate", () => Buka(alamat.GetText()));

        var fokus = Gtk.EventControllerFocus.New();
        Sinyal.Sambung(fokus, "enter", () => { alamatDiketik = true; });
        Sinyal.Sambung(fokus, "leave", () =>
        {
            alamatDiketik = false;
            PerbaruiToolbar();
        });
        alamat.AddController(fokus);

        // Esc di kotak alamat: batalkan ketikan, kembali ke halaman.
        var tombolEsc = Gtk.EventControllerKey.New();
        tombolEsc.SetPropagationPhase(Gtk.PropagationPhase.Capture);
        Sinyal.Sambung(tombolEsc, "key-pressed", (uint tombol, uint _, uint _) =>
        {
            if (tombol != Gdk.Constants.KEY_Escape)
                return false;
            KembaliKeHalaman();
            return true;
        });
        alamat.AddController(tombolEsc);

        bintang = Tombol("non-starred-symbolic", AlihkanBookmark);

        status = Gtk.Label.New("");
        status.SetVisible(false);
        status.SetEllipsize(Pango.EllipsizeMode.Middle);
        status.SetMaxWidthChars(32);

        toolbar = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
        toolbar.SetMarginStart(4);
        toolbar.SetMarginEnd(4);
        toolbar.SetMarginTop(4);
        toolbar.SetMarginBottom(4);
        foreach (var w in new Gtk.Widget[] { mundur, maju, muatUlang, beranda, alamat, bintang, status, tabBaru })
            toolbar.Append(w);

        buku = Gtk.Notebook.New();
        buku.SetScrollable(true);
        buku.SetShowBorder(false);
        buku.SetShowTabs(false);
        buku.SetVexpand(true);
        Sinyal.Sambung(buku, "switch-page", (IntPtr halaman, uint _) => Berpindah(halaman));

        // Cari di halaman (Ctrl+F): bilah di bawah, tersembunyi sampai dipakai.
        kotakCari = Gtk.SearchEntry.New();
        kotakCari.SetWidthChars(28);
        Sinyal.Sambung(kotakCari, "search-changed", Cari);
        Sinyal.Sambung(kotakCari, "activate", () => CariLagi(maju: true));
        Sinyal.Sambung(kotakCari, "stop-search", TutupCari);
        var shiftEnter = Gtk.EventControllerKey.New();
        shiftEnter.SetPropagationPhase(Gtk.PropagationPhase.Capture);
        Sinyal.Sambung(shiftEnter, "key-pressed", (uint tombol, uint _, uint modifier) =>
        {
            if (tombol is not (Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter)
                || (modifier & (uint)Gdk.ModifierType.ShiftMask) == 0)
                return false;
            CariLagi(maju: false);
            return true;
        });
        kotakCari.AddController(shiftEnter);
        hasilCari = Gtk.Label.New("");
        hasilCari.SetHexpand(true);
        hasilCari.SetXalign(0);
        barisCari = Gtk.Box.New(Gtk.Orientation.Horizontal, 4);
        barisCari.SetMarginStart(4);
        barisCari.SetMarginEnd(4);
        barisCari.SetMarginTop(4);
        barisCari.SetMarginBottom(4);
        cariSebelum = Tombol("go-up-symbolic", () => CariLagi(maju: false));
        cariBerikut = Tombol("go-down-symbolic", () => CariLagi(maju: true));
        tutupCari = Tombol("window-close-symbolic", TutupCari);
        foreach (var w in new Gtk.Widget[] { kotakCari, cariSebelum, cariBerikut, hasilCari, tutupCari })
            barisCari.Append(w);
        barisCari.SetVisible(false);

        var isi = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        isi.Append(toolbar);
        isi.Append(buku);
        isi.Append(barisCari);
        win.SetChild(isi);

        PasangPintasan();
        Sinyal.Sambung(Mesin.Sesi, "download-started", (IntPtr unduhan) => PantauUnduhan(unduhan));
        // Tab latar yang lama tidak dilihat ditidurkan walau jumlah tab hidup
        // masih di bawah batas, atau semuanya kalau RAM laptop menipis. Daftar
        // tab ikut disimpan, jadi kalau browser mati mendadak paling banyak
        // perubahan 30 detik terakhir yang hilang.
        GLib.Functions.TimeoutAddSeconds(GLib.Constants.PRIORITY_LOW, 30, () =>
        {
            TidurkanTabLatar();
            SimpanTab();
            return true;
        });
        Sinyal.Sambung(win, "close-request", () =>
        {
            SimpanTab();
            return false;
        });
        Penyaring.Berubah += () =>
        {
            foreach (var tab in tabs)
                tab.PasangUlangPenyaring();
        };
        // Bahasa diganti di kevin://pengaturan: dipancarkan di thread utama,
        // saat halaman itu dibuat.
        Mesin.Preferensi.Berubah += TerapkanBahasa;
        TerapkanBahasa();
    }

    static Teks T => Mesin.Preferensi.Teks;

    // Teks yang tidak berubah-ubah sendiri. Yang bergantung pada keadaan
    // (muat ulang/berhenti, bintang) diatur PerbaruiToolbar.
    void TerapkanBahasa()
    {
        var t = T;
        mundur.SetTooltipText(t["Mundur (Alt+Kiri)", "Back (Alt+Left)"]);
        maju.SetTooltipText(t["Maju (Alt+Kanan)", "Forward (Alt+Right)"]);
        beranda.SetTooltipText(t["Beranda (Alt+Home)", "Home (Alt+Home)"]);
        tabBaru.SetTooltipText(t["Tab baru (Ctrl+T)", "New tab (Ctrl+T)"]);
        alamat.SetPlaceholderText(t["Cari di Google atau ketik alamat", "Search Google or type an address"]);
        kotakCari.SetPlaceholderText(t["Cari di halaman", "Find on page"]);
        cariSebelum.SetTooltipText(t["Hasil sebelumnya (Shift+Enter)", "Previous match (Shift+Enter)"]);
        cariBerikut.SetTooltipText(t["Hasil berikutnya (Enter)", "Next match (Enter)"]);
        tutupCari.SetTooltipText(t["Tutup (Esc)", "Close (Esc)"]);
        foreach (var tab in tabs)
            tab.TerapkanBahasa();
        PerbaruiToolbar();
    }

    public void Tampilkan() => win.Present();

    public bool ApakahAktif(Tab tab) => tab == aktif;

    /// <summary>
    /// Tab baru di kanan tab aktif. Tab latar belum dimuat sama sekali (tanpa
    /// proses web) sampai dilihat; <paramref name="uri"/> kosong = tab kosong
    /// dengan kursor di kotak alamat.
    /// </summary>
    public Tab BukaTab(string uri, bool latar = false)
    {
        var tab = new Tab(this, TanpaPelacak(uri));
        Sisipkan(tab, latar);
        return tab;
    }

    /// <summary>Untuk window.open / target=_blank; tab-nya ditampilkan saat ready-to-show.</summary>
    public Tab BukaTabTerkait(Tab pembuka)
    {
        Catat.Tulis($"tab dari window.open/target=_blank, pembuka: {pembuka.Uri}");
        var tab = new Tab(this, "");
        tab.PasangTerkait(pembuka.Web!);
        Sisipkan(tab, latar: true);
        return tab;
    }

    public void TampilkanTab(Tab tab) => buku.SetCurrentPage(buku.PageNum(tab.Wadah));

    /// <summary>
    /// Tab dari sesi sebelumnya dibuka lagi, di kanan tab yang baru dibuka,
    /// dalam keadaan tidur: tanpa proses web sampai dilihat. Dipanggil sekali,
    /// saat jendela pertama dibuat.
    /// </summary>
    public void PulihkanTab()
    {
        List<TabTersimpan> simpanan;
        try
        {
            simpanan = Mesin.TabTerbuka.Baca();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[kevin-browser] daftar tab tidak terbaca: {e.Message}");
            return;
        }
        foreach (var s in simpanan)
            Sisipkan(new Tab(this, s), latar: true);
        if (simpanan.Count > 0)
            Catat.Tulis($"{simpanan.Count} tab dari sesi sebelumnya dibuka lagi (tidur)");
    }

    public void TutupTab(Tab tab)
    {
        var posisi = buku.PageNum(tab.Wadah);
        if (posisi < 0)
            return;   // sudah ditutup, mis. window.close() lalu Ctrl+W

        if (tab.Simpan() is { } simpanan)
            tertutup.Tambah(simpanan, posisi);
        tabs.Remove(tab);
        if (tab == aktif)
            aktif = null;
        buku.RemovePage(posisi);   // GTK pindah ke tab tetangga → Berpindah
        tab.Tutup();

        SimpanTab();
        if (tabs.Count == 0)
            win.Close();
        else
            AturBarisTab();
    }

    // Ctrl+Shift+T: tab yang terakhir ditutup, di tempatnya semula.
    void BukaLagiTab()
    {
        if (tertutup.Ambil() is not { } t)
            return;
        var (simpanan, posisi) = t;
        Catat.Tulis($"dibuka lagi: {simpanan.Uri}");
        Sisipkan(new Tab(this, simpanan), latar: false, Math.Min(posisi, buku.GetNPages()));
    }

    // Tab yang sedang terbuka, urut seperti di jendela, untuk PulihkanTab
    // berikutnya. Beranda tidak ikut: tab beranda baru selalu dibuka saat
    // browser dibuka.
    void SimpanTab()
    {
        var urut = tabs.OrderBy(t => buku.PageNum(t.Wadah))
            .Select(t => t.Simpan())
            .OfType<TabTersimpan>()
            .Where(t => t.Uri != Alamat.Beranda)
            .ToList();
        try
        {
            if (Mesin.TabTerbuka.Simpan(urut))
                Catat.Tulis($"daftar tab disimpan: {urut.Count} tab");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[kevin-browser] daftar tab gagal disimpan: {e.Message}");
        }
    }

    public void TabBerubah(Tab tab)
    {
        if (tab == aktif)
            PerbaruiToolbar();
    }

    public void LayarPenuh(bool penuh)
    {
        layarPenuh = penuh;
        toolbar.SetVisible(!penuh);
        AturBarisTab();
    }

    /// <summary>
    /// Dari decide-policy tiap WebView; <paramref name="keputusan"/> hanya
    /// berlaku selama sinyal berjalan. <c>true</c> = sudah ditangani di sini.
    /// </summary>
    public bool PutuskanNavigasi(IntPtr keputusan, WebKit.PolicyDecisionType jenis)
    {
        if (jenis != WebKit.PolicyDecisionType.NavigationAction)
            return false;

        var aksi = Asli.webkit_navigation_policy_decision_get_navigation_action(keputusan);
        var uri = Asli.Teks(Asli.webkit_uri_request_get_uri(Asli.webkit_navigation_action_get_request(aksi)));
        var tombol = Asli.webkit_navigation_action_get_mouse_button(aksi);
        var ctrl = (Asli.webkit_navigation_action_get_modifiers(aksi) & (uint)Gdk.ModifierType.ControlMask) != 0;

        // Klik tengah / Ctrl+klik tautan → tab latar.
        if (Asli.webkit_navigation_action_get_navigation_type(aksi) == (int)WebKit.NavigationType.LinkClicked
            && (tombol == 2 || (tombol == 1 && ctrl)))
        {
            Asli.webkit_policy_decision_ignore(keputusan);
            Catat.Tulis($"tab latar (klik tengah/Ctrl+klik): {uri}");
            BukaTab(uri, latar: true);
            return true;
        }

        if (SkemaDikenal(uri))
        {
            // Video berputar sendiri atau tidak (PutarOtomatis), per halaman.
            var kebijakan = Mesin.KebijakanSitus(uri);
            if (kebijakan == IntPtr.Zero)
                return false;
            Asli.webkit_policy_decision_use_with_policies(keputusan, kebijakan);
            return true;
        }

        // Hanya kalau diklik pemakai: halaman tidak boleh membuka aplikasi
        // lain sendiri (iklan yang mengalihkan ke market:, dll).
        var diklik = Asli.webkit_navigation_action_is_user_gesture(aksi);
        Asli.webkit_policy_decision_ignore(keputusan);
        Catat.Tulis($"skema asing ({(diklik ? "diluncurkan" : "tanpa klik, diabaikan")}): {uri}");
        if (diklik)
            Luncurkan(uri);
        return true;
    }

    // Alamat dari luar halaman (kotak alamat, aplikasi lain, klik tengah):
    // parameter pelacak dibuang sebelum dimuat, jadi servernya tidak pernah
    // menerimanya. Yang dari dalam halaman diurus Tab.SkripPembersih.
    static string TanpaPelacak(string uri) =>
        Penyaring.Nyala && PembersihAlamat.Bersihkan(uri) is { } bersih ? bersih : uri;

    void Buka(string teks)
    {
        var uri = Alamat.Tafsirkan(teks);
        if (!SkemaDikenal(uri))
        {
            Luncurkan(uri);
            KembaliKeHalaman();
            return;
        }
        if (aktif is null)
            return;

        aktif.Muat(TanpaPelacak(uri));
        TidurkanTabLatar();   // tab kosong baru saja hidup
        aktif.Web?.GrabFocus();
    }

    // Di kanan tab aktif, kecuali kalau posisinya diberikan. Beberapa tab
    // latar berturut-turut (klik tengah, alamat dari baris perintah, tab dari
    // sesi sebelumnya) berjejer sesuai urutan dibuka; kalau masing-masing
    // langsung di kanan tab aktif, urutannya jadi terbalik.
    void Sisipkan(Tab tab, bool latar, int? posisi = null)
    {
        var acuan = latar && latarTerakhir is not null && tabs.Contains(latarTerakhir) ? latarTerakhir : aktif;
        posisi ??= acuan is null ? -1 : buku.PageNum(acuan.Wadah) + 1;
        tabs.Add(tab);
        buku.InsertPage(tab.Wadah, tab.Label, posisi.Value);   // halaman pertama langsung jadi aktif
        buku.SetTabReorderable(tab.Wadah, true);
        AturBarisTab();
        if (latar)
            latarTerakhir = tab;
        else
            buku.SetCurrentPage(buku.PageNum(tab.Wadah));
    }

    void Berpindah(IntPtr halaman)
    {
        var baru = tabs.Find(t => t.Wadah.Handle.DangerousGetHandle() == halaman);
        if (baru is null || baru == aktif)
            return;

        var sekarang = Environment.TickCount64;
        if (aktif is not null)
        {
            aktif.TerakhirAktif = sekarang;
            aktif.Web?.GetFindController().SearchFinish();
        }
        baru.TerakhirAktif = sekarang;
        aktif = baru;
        latarTerakhir = null;

        baru.Bangunkan();
        TidurkanTabLatar();
        if (barisCari.GetVisible())
            Cari();
        alamatDiketik = false;
        PerbaruiToolbar();

        // Fokus setelah GTK selesai berpindah halaman: widget di halaman yang
        // belum tampil belum bisa menerima fokus.
        GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
        {
            if (baru == aktif)
            {
                if (baru.Web is { } web)
                    web.GrabFocus();
                else
                    alamat.GrabFocus();
            }
            return false;
        });
    }

    void TidurkanTabLatar()
    {
        if (aktif is null)
            return;
        var tersedia = Lingkungan.RamTersediaMB();
        var menipis = tersedia < setelan.RamMenipisMB;
        var tidur = TidurTab.PilihYangDitidurkan(tabs, aktif, setelan.TabHidupMaks, Environment.TickCount64, setelan.TidurSetelah, menipis);
        if (menipis && tidur.Count > 0)
            Catat.Tulis($"RAM menipis (tersedia {tersedia} MB, ambang {setelan.RamMenipisMB} MB): {tidur.Count} tab latar ditidurkan");
        foreach (var tab in tidur)
            tab.Tidurkan();
    }

    void PerbaruiToolbar()
    {
        var web = aktif?.Web;
        if (!alamatDiketik)
            alamat.SetText(aktif?.Uri ?? "");
        mundur.SetSensitive(web?.CanGoBack() == true);
        maju.SetSensitive(web?.CanGoForward() == true);

        var memuat = web?.IsLoading == true;
        muatUlang.SetIconName(memuat ? "process-stop-symbolic" : "view-refresh-symbolic");
        muatUlang.SetTooltipText(memuat ? T["Berhenti", "Stop"] : T["Muat ulang (F5)", "Reload (F5)"]);
        alamat.SetProgressFraction(memuat ? web!.EstimatedLoadProgress : 0);

        var uri = aktif?.Uri ?? "";
        var bisaDisimpan = Riwayat.LayakDicatat(uri);
        var tersimpan = bisaDisimpan && Mesin.Bookmark.Ada(uri);
        bintang.SetSensitive(bisaDisimpan);
        bintang.SetIconName(tersimpan ? "starred-symbolic" : "non-starred-symbolic");
        bintang.SetTooltipText(tersimpan ? T["Hapus dari bookmark", "Remove bookmark"] : T["Simpan ke bookmark (Ctrl+D)", "Bookmark this page (Ctrl+D)"]);
        win.SetTitle(aktif?.JudulTampil ?? "kevin-browser");
    }

    void AturBarisTab() => buku.SetShowTabs(tabs.Count > 1 && !layarPenuh);

    void KembaliKeHalaman()
    {
        alamatDiketik = false;
        PerbaruiToolbar();
        aktif?.Web?.GrabFocus();
    }

    void MuatUlangAtauBerhenti()
    {
        if (aktif?.Web is not { } web)
            return;
        if (web.IsLoading)
            web.StopLoading();
        else
            web.Reload();
    }

    void PasangPintasan()
    {
        Aksi("tab-baru", ["<Control>t"], () => BukaTab(""));
        Aksi("buka-lagi-tab", ["<Control><Shift>t"], BukaLagiTab);
        Aksi("tutup-tab", ["<Control>w", "<Control>F4"], () =>
        {
            if (aktif is not null)
                TutupTab(aktif);
        });
        Aksi("alamat", ["<Control>l", "<Alt>d", "F6"], () => alamat.GrabFocus());
        Aksi("muat-ulang", ["<Control>r", "F5"], () => aktif?.Web?.Reload());
        Aksi("muat-ulang-penuh", ["<Control><Shift>r", "<Shift>F5"], () => aktif?.Web?.ReloadBypassCache());
        Aksi("mundur", ["<Alt>Left"], () => aktif?.Web?.GoBack());
        Aksi("maju", ["<Alt>Right"], () => aktif?.Web?.GoForward());
        Aksi("beranda", ["<Alt>Home"], () => Buka(Alamat.Beranda));
        Aksi("riwayat", ["<Control>h"], () => BukaTab(HalamanBawaan.Riwayat));
        Aksi("cari", ["<Control>f"], BukaCari);
        Aksi("simpan-bookmark", ["<Control>d"], SimpanBookmark);
        Aksi("bookmark", ["<Control><Shift>o"], () => BukaTab(HalamanBawaan.Bookmark));
        Aksi("cari-berikut", ["<Control>g", "F3"], () => CariLagi(maju: true));
        Aksi("cari-sebelum", ["<Control><Shift>g", "<Shift>F3"], () => CariLagi(maju: false));
        Aksi("hapus-data", ["<Control><Shift>Delete"], () => BukaTab(HalamanBawaan.Pengaturan));
        Aksi("tab-berikut", ["<Control>Tab", "<Control>Page_Down"], () => GeserTab(+1));
        Aksi("tab-sebelum", ["<Control><Shift>Tab", "<Control><Shift>ISO_Left_Tab", "<Control>Page_Up"], () => GeserTab(-1));
        Aksi("perbesar", ["<Control>plus", "<Control>equal", "<Control>KP_Add"], () => aktif?.Zoom(+0.1));
        Aksi("perkecil", ["<Control>minus", "<Control>KP_Subtract"], () => aktif?.Zoom(-0.1));
        Aksi("zoom-asli", ["<Control>0", "<Control>KP_0"], () => aktif?.Zoom(0));
        Aksi("cetak", ["<Control>p"], () =>
        {
            if (aktif?.Web is { } web)
                WebKit.PrintOperation.New(web).RunDialog(win);
        });
        Aksi("layar-penuh", ["F11"], () =>
        {
            if (win.IsFullscreen())
                win.Unfullscreen();
            else
                win.Fullscreen();
        });
    }

    // Pintasan lewat aksi aplikasi: GTK memprosesnya di fase capture, jadi
    // tetap jalan walau fokus sedang di dalam halaman web.
    void Aksi(string nama, string[] tombol, Action kerja)
    {
        var aksi = Gio.SimpleAction.New(nama, null);
        Sinyal.Sambung(aksi, "activate", (IntPtr _) => kerja());
        win.AddAction(aksi);
        app.SetAccelsForAction("win." + nama, tombol);
    }

    void GeserTab(int arah)
    {
        var n = buku.GetNPages();
        if (n > 1)
            buku.SetCurrentPage((buku.GetCurrentPage() + arah + n) % n);
    }

    void PantauUnduhan(IntPtr ptr)
    {
        // Pembungkus sendiri, dilepas saat selesai; WebKit tetap memegang
        // unduhannya selama berjalan.
        var unduhan = WebKit.Download.NewFromPointer(ptr, false);
        var gagal = false;
        Sinyal.Sambung(unduhan, "decide-destination", (IntPtr usulan) =>
        {
            var folder = GLib.Functions.GetUserSpecialDir(GLib.UserDirectory.DirectoryDownload)
                ?? GLib.Functions.GetHomeDir();
            Directory.CreateDirectory(folder);
            unduhan.SetDestination(Path.Combine(folder, NamaBerkas.Unik(folder, Asli.Teks(usulan))));
            return true;
        });
        Sinyal.Sambung(unduhan, "failed", (IntPtr _) => { gagal = true; });
        // "finished" juga dipancarkan sesudah "failed".
        Sinyal.Sambung(unduhan, "finished", () =>
        {
            var tujuan = unduhan.GetDestination() ?? "";
            if (gagal)
                TampilkanStatus(T["Unduhan gagal", "Download failed"], null, sementara: true);
            else
                TampilkanStatus(T["Tersimpan: ", "Saved: "] + Path.GetFileName(tujuan), tujuan, sementara: true);
            unduhan.Dispose();
        });
        TampilkanStatus(T["Mengunduh…", "Downloading…"], null, sementara: false);
    }

    // ── Bookmark ────────────────────────────────────────────────────────

    // Ctrl+D hanya menyimpan, tidak pernah menghapus: menekannya dua kali
    // tidak boleh membuang bookmark tanpa sengaja.
    void SimpanBookmark()
    {
        if (aktif is not { } tab || !Riwayat.LayakDicatat(tab.Uri))
            return;
        TampilkanStatus(Mesin.Bookmark.Tambah(tab.Uri, tab.Judul, DateTimeOffset.Now)
            ? T["Disimpan ke bookmark", "Bookmarked"] : T["Sudah ada di bookmark", "Already bookmarked"], null, sementara: true);
        PerbaruiToolbar();
    }

    // Tombol bintang: simpan, atau hapus kalau sudah tersimpan.
    void AlihkanBookmark()
    {
        if (aktif is not { } tab || !Mesin.Bookmark.Ada(tab.Uri))
        {
            SimpanBookmark();
            return;
        }
        Mesin.Bookmark.Hapus(tab.Uri);
        TampilkanStatus(T["Dihapus dari bookmark", "Bookmark removed"], null, sementara: true);
        PerbaruiToolbar();
    }

    // ── Cari di halaman ─────────────────────────────────────────────────

    const uint HasilCariMaks = 1000;

    void BukaCari()
    {
        barisCari.SetVisible(true);
        kotakCari.GrabFocus();
        kotakCari.SelectRegion(0, -1);
        Cari();
    }

    // Saat mengetik (search-changed sudah menunggu sejenak) dan saat pindah tab.
    void Cari()
    {
        if (aktif?.Web is not { } web)
            return;
        var teks = kotakCari.GetText();
        if (teks.Length == 0)
        {
            web.GetFindController().SearchFinish();
            TulisHasilCari(null);
            return;
        }
        var opsi = (uint)(WebKit.FindOptions.CaseInsensitive | WebKit.FindOptions.WrapAround);
        var cari = web.GetFindController();
        // Menghitung dulu: CountMatches sesudah Search menghapus sorotan
        // kuning semua hasil (terjadi).
        cari.CountMatches(teks, opsi, HasilCariMaks);
        cari.Search(teks, opsi, HasilCariMaks);
    }

    void CariLagi(bool maju)
    {
        if (!barisCari.GetVisible())
        {
            BukaCari();
            return;
        }
        if (aktif?.Web is not { } web || kotakCari.GetText().Length == 0)
            return;
        if (maju)
            web.GetFindController().SearchNext();
        else
            web.GetFindController().SearchPrevious();
    }

    void TutupCari()
    {
        barisCari.SetVisible(false);
        aktif?.Web?.GetFindController().SearchFinish();
        aktif?.Web?.GrabFocus();
    }

    /// <summary>Dari sinyal counted-matches/failed-to-find-text milik tab itu.</summary>
    public void HasilCari(Tab tab, int jumlah)
    {
        if (tab == aktif && barisCari.GetVisible())
            TulisHasilCari(jumlah);
    }

    void TulisHasilCari(int? jumlah)
    {
        hasilCari.SetText(jumlah switch
        {
            null => "",
            0 => T["Tidak ditemukan", "Not found"],
            1 => T["1 hasil", "1 match"],
            >= (int)HasilCariMaks => T[$"{HasilCariMaks}+ hasil", $"{HasilCariMaks}+ matches"],
            _ => T[$"{jumlah} hasil", $"{jumlah} matches"],
        });
        if (jumlah == 0)
            kotakCari.AddCssClass("error");
        else
            kotakCari.RemoveCssClass("error");
    }

    void TampilkanStatus(string teks, string? tip, bool sementara)
    {
        status.SetText(teks);
        status.SetTooltipText(tip);
        status.SetVisible(true);
        if (pengingatStatus != 0)
        {
            GLib.Functions.SourceRemove(pengingatStatus);
            pengingatStatus = 0;
        }
        if (sementara)
            pengingatStatus = GLib.Functions.TimeoutAddSeconds(GLib.Constants.PRIORITY_DEFAULT, 8, () =>
            {
                status.SetVisible(false);
                pengingatStatus = 0;
                return false;
            });
    }

    static Gtk.Button Tombol(string ikon, Action kerja)
    {
        var tombol = Gtk.Button.NewFromIconName(ikon);
        Sinyal.Sambung(tombol, "clicked", kerja);
        return tombol;
    }

    static bool SkemaDikenal(string uri)
    {
        var titikDua = uri.IndexOf(':');
        return titikDua > 0 && SkemaWeb.Contains(uri[..titikDua].ToLowerInvariant());
    }

    static void Luncurkan(string uri)
    {
        try
        {
            Gio.AppInfoHelper.LaunchDefaultForUri(uri, null);
        }
        catch (GLib.GException)
        {
            // Tidak ada aplikasi untuk skema itu — diam saja, seperti peramban lain.
        }
    }
}
