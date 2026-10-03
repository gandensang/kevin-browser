using System.Runtime.InteropServices;

namespace KevinBrowser.Linux;

/// <summary>
/// Menyambung sinyal GObject langsung lewat g_signal_connect_data. Semua
/// sinyal di proyek ini lewat sini — JANGAN pakai event GirCore (web.OnLoadChanged,
/// tombol.OnClicked, dst).
/// </summary>
/// <remarks>
/// Event GirCore (0.8.1, sama di 0.9-preview) menyalin tiap parameter sinyal,
/// termasuk pengirimnya, ke GValue terkelola yang hanya dibebaskan oleh
/// finalizer .NET. Terukur: WebView dengan satu handler notify menahan 9
/// referensi setelah memuat satu halaman, tidak pernah dihancurkan, dan
/// proses web-nya tidak pernah berhenti — tab yang ditidurkan tidak menghemat
/// apa pun. Kalau GC akhirnya berjalan, widget GTK difinalisasi di thread
/// finalizer (GC.WaitForPendingFinalizers di thread utama malah menggantung).
///
/// Di sini parameter dibaca langsung dari C tanpa disalin, dan delegate-nya
/// dibebaskan GLib sendiri saat objeknya dihancurkan. Tiap bentuk handler C
/// butuh trampolinnya sendiri: daftar parameter harus persis sama dengan
/// tanda tangan sinyalnya, kalau tidak "data" terbaca dari register yang salah.
/// </remarks>
static unsafe partial class Sinyal
{
    /// <summary>void (*)(GObject*, gpointer) — clicked, activate, close, ready-to-show, …</summary>
    public static void Sambung(GObject.Object objek, string nama, Action kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&V, kerja);

    /// <summary>
    /// Seperti di atas, untuk objek yang hanya diterima sebagai pointer di
    /// argumen sinyal lain (mis. WebKitNotification dari show-notification).
    /// </summary>
    public static void Sambung(IntPtr objek, string nama, Action kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&V, kerja);

    /// <summary>gboolean (*)(GObject*, gpointer) — enter-fullscreen, leave-fullscreen.</summary>
    public static void Sambung(GObject.Object objek, string nama, Func<bool> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&B, kerja);

    /// <summary>void (*)(GObject*, gpointer arg, gpointer) — notify::…, script-message-received, …</summary>
    public static void Sambung(GObject.Object objek, string nama, Action<IntPtr> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&VP, kerja);

    /// <summary>void (*)(GObject*, gint/enum, gpointer) — load-changed, web-process-terminated.</summary>
    public static void Sambung(GObject.Object objek, string nama, Action<int> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr, void>)&VI, kerja);

    /// <summary>gboolean (*)(GObject*, gpointer arg, gpointer) — decide-destination.</summary>
    public static void Sambung(GObject.Object objek, string nama, Func<IntPtr, bool> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)&BP, kerja);

    /// <summary>gpointer (*)(GObject*, gpointer arg, gpointer) — create.</summary>
    public static void Sambung(GObject.Object objek, string nama, Func<IntPtr, IntPtr> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)&PP, kerja);

    /// <summary>gboolean (*)(GObject*, gpointer arg, gint/enum, gpointer) — decide-policy.</summary>
    public static void Sambung(GObject.Object objek, string nama, Func<IntPtr, int, bool> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int>)&BPI, kerja);

    /// <summary>void (*)(GObject*, gpointer arg, guint, gpointer) — switch-page.</summary>
    public static void Sambung(GObject.Object objek, string nama, Action<IntPtr, uint> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, uint, IntPtr, void>)&VPU, kerja);

    /// <summary>void (*)(GObject*, gpointer, gint, const gchar*, gpointer) — open (GApplication).</summary>
    public static void Sambung(GObject.Object objek, string nama, Action<IntPtr, int, IntPtr> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, IntPtr, void>)&VPIP, kerja);

    /// <summary>void (*)(GObject*, gint, gdouble, gdouble, gpointer) — pressed (GtkGestureClick).</summary>
    public static void Sambung(GObject.Object objek, string nama, Action<int, double, double> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, int, double, double, IntPtr, void>)&VIDD, kerja);

    /// <summary>gboolean (*)(GObject*, guint, guint, GdkModifierType, gpointer) — key-pressed.</summary>
    public static void Sambung(GObject.Object objek, string nama, Func<uint, uint, uint, bool> kerja) =>
        Pasang(objek, nama, (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, uint, IntPtr, int>)&BUUU, kerja);

    static void Pasang(GObject.Object objek, string nama, IntPtr trampolin, Delegate kerja) =>
        Pasang(objek.Handle.DangerousGetHandle(), nama, trampolin, kerja, objek.GetType().Name);

    static void Pasang(IntPtr objek, string nama, IntPtr trampolin, Delegate kerja, string jenis = "objek itu")
    {
        var data = GCHandle.ToIntPtr(GCHandle.Alloc(kerja));
        var lepas = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&Lepas;
        if (g_signal_connect_data(objek, nama, trampolin, data, lepas, 0) == 0)
        {
            GCHandle.FromIntPtr(data).Free();
            throw new ArgumentException($"Sinyal '{nama}' tidak ada pada {jenis}");
        }
    }

    static T Kerja<T>(IntPtr data) where T : Delegate => (T)GCHandle.FromIntPtr(data).Target!;

    // Pengecualian tidak boleh lolos ke kode C — prosesnya langsung mati.
    static void Galat(Exception e) =>
        Console.Error.WriteLine($"[kevin-browser] galat di handler sinyal: {e}");

    // GClosureNotify: dipanggil GLib saat handler diputus atau objeknya dihancurkan.
    [UnmanagedCallersOnly]
    static void Lepas(IntPtr data, IntPtr closure) => GCHandle.FromIntPtr(data).Free();

    [UnmanagedCallersOnly]
    static void V(IntPtr diri, IntPtr data)
    {
        try { Kerja<Action>(data)(); }
        catch (Exception e) { Galat(e); }
    }

    [UnmanagedCallersOnly]
    static int B(IntPtr diri, IntPtr data)
    {
        try { return Kerja<Func<bool>>(data)() ? 1 : 0; }
        catch (Exception e) { Galat(e); return 0; }
    }

    [UnmanagedCallersOnly]
    static void VP(IntPtr diri, IntPtr a, IntPtr data)
    {
        try { Kerja<Action<IntPtr>>(data)(a); }
        catch (Exception e) { Galat(e); }
    }

    [UnmanagedCallersOnly]
    static void VI(IntPtr diri, int a, IntPtr data)
    {
        try { Kerja<Action<int>>(data)(a); }
        catch (Exception e) { Galat(e); }
    }

    [UnmanagedCallersOnly]
    static int BP(IntPtr diri, IntPtr a, IntPtr data)
    {
        try { return Kerja<Func<IntPtr, bool>>(data)(a) ? 1 : 0; }
        catch (Exception e) { Galat(e); return 0; }
    }

    [UnmanagedCallersOnly]
    static IntPtr PP(IntPtr diri, IntPtr a, IntPtr data)
    {
        try { return Kerja<Func<IntPtr, IntPtr>>(data)(a); }
        catch (Exception e) { Galat(e); return IntPtr.Zero; }
    }

    [UnmanagedCallersOnly]
    static int BPI(IntPtr diri, IntPtr a, int b, IntPtr data)
    {
        try { return Kerja<Func<IntPtr, int, bool>>(data)(a, b) ? 1 : 0; }
        catch (Exception e) { Galat(e); return 0; }
    }

    [UnmanagedCallersOnly]
    static void VPU(IntPtr diri, IntPtr a, uint b, IntPtr data)
    {
        try { Kerja<Action<IntPtr, uint>>(data)(a, b); }
        catch (Exception e) { Galat(e); }
    }

    [UnmanagedCallersOnly]
    static void VPIP(IntPtr diri, IntPtr a, int b, IntPtr c, IntPtr data)
    {
        try { Kerja<Action<IntPtr, int, IntPtr>>(data)(a, b, c); }
        catch (Exception e) { Galat(e); }
    }

    [UnmanagedCallersOnly]
    static void VIDD(IntPtr diri, int a, double b, double c, IntPtr data)
    {
        try { Kerja<Action<int, double, double>>(data)(a, b, c); }
        catch (Exception e) { Galat(e); }
    }

    [UnmanagedCallersOnly]
    static int BUUU(IntPtr diri, uint a, uint b, uint c, IntPtr data)
    {
        try { return Kerja<Func<uint, uint, uint, bool>>(data)(a, b, c) ? 1 : 0; }
        catch (Exception e) { Galat(e); return 0; }
    }

    [LibraryImport("libgobject-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint g_signal_connect_data(
        IntPtr instance, string sinyal, IntPtr handler, IntPtr data, IntPtr lepas, int flags);
}
