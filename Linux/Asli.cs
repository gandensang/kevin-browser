using System.Runtime.InteropServices;

namespace KevinBrowser.Linux;

/// <summary>
/// Fungsi C yang dipanggil langsung dari handler sinyal, pada pointer yang
/// hanya berlaku selama sinyalnya berjalan. Lewat GirCore, struktur boxed
/// seperti NavigationAction disalin dan salinannya baru dibebaskan
/// finalizer di thread lain — objek WebKit tidak boleh disentuh di sana.
/// </summary>
static partial class Asli
{
    const string LibWebKit = "libwebkitgtk-6.0.so.4";
    const string LibGio = "libgio-2.0.so.0";
    const string LibGLib = "libglib-2.0.so.0";

    [LibraryImport(LibWebKit)]
    public static partial void webkit_policy_decision_ignore(IntPtr keputusan);

    [LibraryImport(LibWebKit)]
    public static partial IntPtr webkit_navigation_policy_decision_get_navigation_action(IntPtr keputusan);

    [LibraryImport(LibWebKit)]
    public static partial int webkit_navigation_action_get_navigation_type(IntPtr aksi);

    [LibraryImport(LibWebKit)]
    public static partial uint webkit_navigation_action_get_mouse_button(IntPtr aksi);

    [LibraryImport(LibWebKit)]
    public static partial uint webkit_navigation_action_get_modifiers(IntPtr aksi);

    [LibraryImport(LibWebKit)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool webkit_navigation_action_is_user_gesture(IntPtr aksi);

    [LibraryImport(LibWebKit)]
    public static partial IntPtr webkit_navigation_action_get_request(IntPtr aksi);

    [LibraryImport(LibWebKit)]
    public static partial IntPtr webkit_uri_request_get_uri(IntPtr permintaan);

    [LibraryImport(LibWebKit)]
    public static partial void webkit_permission_request_allow(IntPtr permintaan);

    [LibraryImport(LibWebKit)]
    public static partial void webkit_permission_request_deny(IntPtr permintaan);

    [LibraryImport(LibWebKit)]
    private static partial nuint webkit_user_media_permission_request_get_type();

    [LibraryImport(LibWebKit)]
    private static partial nuint webkit_device_info_permission_request_get_type();

    [LibraryImport("libgobject-2.0.so.0")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool g_type_check_instance_is_a(IntPtr objek, nuint jenis);

    /// <summary>
    /// Permintaan kamera, mikrofon, atau layar (getUserMedia/getDisplayMedia),
    /// termasuk izin melihat nama perangkat (enumerateDevices).
    /// </summary>
    public static bool IzinPerangkatMedia(IntPtr permintaan) =>
        g_type_check_instance_is_a(permintaan, webkit_user_media_permission_request_get_type())
        || g_type_check_instance_is_a(permintaan, webkit_device_info_permission_request_get_type());

    [LibraryImport(LibWebKit)]
    private static partial nuint webkit_notification_permission_request_get_type();

    /// <summary>Permintaan izin notifikasi desktop (Notification.requestPermission).</summary>
    public static bool IzinNotifikasi(IntPtr permintaan) =>
        g_type_check_instance_is_a(permintaan, webkit_notification_permission_request_get_type());

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr webkit_security_origin_new_for_uri(string uri);

    [LibraryImport(LibWebKit)]
    private static partial void webkit_security_origin_unref(IntPtr asal);

    [LibraryImport(LibWebKit)]
    private static partial void webkit_web_context_initialize_notification_permissions(IntPtr konteks, IntPtr diizinkan, IntPtr ditolak);

    [LibraryImport(LibGLib)]
    private static partial IntPtr g_list_append(IntPtr daftar, IntPtr data);

    [LibraryImport(LibGLib)]
    private static partial void g_list_free(IntPtr daftar);

    /// <summary>
    /// Izin notifikasi untuk proses web yang akan dibuat. GirCore meminta
    /// GLib.List berisi SecurityOrigin, yang tidak bisa diisi dari sisi .NET.
    /// </summary>
    public static void SetelIzinNotifikasi(IntPtr konteks, IEnumerable<string> asal)
    {
        var daftar = IntPtr.Zero;
        foreach (var a in asal)
            daftar = g_list_append(daftar, webkit_security_origin_new_for_uri(a));
        webkit_web_context_initialize_notification_permissions(konteks, daftar, IntPtr.Zero);
        // GList: { gpointer data; GList *next; GList *prev; }
        for (var l = daftar; l != IntPtr.Zero; l = Marshal.ReadIntPtr(l, IntPtr.Size))
            webkit_security_origin_unref(Marshal.ReadIntPtr(l));
        g_list_free(daftar);
    }

    [LibraryImport(LibWebKit)]
    public static partial void webkit_policy_decision_use_with_policies(IntPtr keputusan, IntPtr kebijakan);

    /// <summary>GType enum WebKitAutoplayPolicy; tidak ikut dibungkus GirCore.</summary>
    [LibraryImport(LibWebKit)]
    public static partial nuint webkit_autoplay_policy_get_type();

    [LibraryImport(LibWebKit)]
    private static unsafe partial void webkit_website_data_manager_clear(
        IntPtr pengelola, uint jenis, long rentangMikrodetik, IntPtr pembatal,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> selesai, IntPtr data);

    [LibraryImport(LibWebKit)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool webkit_website_data_manager_clear_finish(IntPtr pengelola, IntPtr hasil, out IntPtr galat);

    [LibraryImport(LibGLib)]
    private static partial void g_error_free(IntPtr galat);

    /// <summary>
    /// webkit_website_data_manager_clear sebagai Task. GirCore hanya membuat
    /// …_finish-nya; fungsi pemulainya tidak ikut dibungkus.
    /// <paramref name="rentang"/> null = semua waktu.
    /// </summary>
    public static unsafe Task HapusDataWebKit(IntPtr pengelola, uint jenis, TimeSpan? rentang)
    {
        var selesai = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        webkit_website_data_manager_clear(pengelola, jenis, rentang is { } r ? (long)r.TotalMicroseconds : 0,
            IntPtr.Zero, &SelesaiHapus, GCHandle.ToIntPtr(GCHandle.Alloc(selesai)));
        return selesai.Task;
    }

    [UnmanagedCallersOnly]
    static void SelesaiHapus(IntPtr pengelola, IntPtr hasil, IntPtr data)
    {
        var pegangan = GCHandle.FromIntPtr(data);
        var selesai = (TaskCompletionSource)pegangan.Target!;
        pegangan.Free();
        if (!webkit_website_data_manager_clear_finish(pengelola, hasil, out var galat) && galat != IntPtr.Zero)
        {
            // GError: { GQuark domain; gint code; gchar *message; }
            Console.Error.WriteLine("[kevin-browser] hapus data gagal: " + Teks(Marshal.ReadIntPtr(galat, 8)));
            g_error_free(galat);
        }
        selesai.SetResult();
    }

    // Pemblokir konten. GirCore lagi-lagi hanya membuat …_finish-nya.
    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial void webkit_user_content_filter_store_save_from_file(
        IntPtr gudang, string id, IntPtr berkas, IntPtr pembatal,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> selesai, IntPtr data);

    [LibraryImport(LibWebKit)]
    private static partial IntPtr webkit_user_content_filter_store_save_from_file_finish(IntPtr gudang, IntPtr hasil, out IntPtr galat);

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial void webkit_user_content_filter_store_load(
        IntPtr gudang, string id, IntPtr pembatal,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> selesai, IntPtr data);

    [LibraryImport(LibWebKit)]
    private static partial IntPtr webkit_user_content_filter_store_load_finish(IntPtr gudang, IntPtr hasil, out IntPtr galat);

    [LibraryImport(LibWebKit)]
    public static partial void webkit_user_content_manager_add_filter(IntPtr pengelola, IntPtr filter);

    [LibraryImport(LibWebKit)]
    public static partial void webkit_user_content_manager_remove_all_filters(IntPtr pengelola);

    [LibraryImport(LibWebKit)]
    public static partial void webkit_user_content_filter_unref(IntPtr filter);

    [LibraryImport(LibGio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr g_file_new_for_path(string path);

    [LibraryImport("libgobject-2.0.so.0")]
    private static partial void g_object_unref(IntPtr objek);

    [LibraryImport(LibGLib)]
    private static partial IntPtr g_bytes_get_data(IntPtr bytes, out nuint ukuran);

    /// <summary>Mengompilasi berkas JSON aturan; hasilnya filter (perlu di-unref) atau galat.</summary>
    public static unsafe Task<(IntPtr Filter, string? Galat)> SimpanFilter(IntPtr gudang, string id, string berkasJson)
    {
        var selesai = new TaskCompletionSource<(IntPtr, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var berkas = g_file_new_for_path(berkasJson);
        webkit_user_content_filter_store_save_from_file(gudang, id, berkas, IntPtr.Zero, &SelesaiSimpan, GCHandle.ToIntPtr(GCHandle.Alloc(selesai)));
        g_object_unref(berkas);
        return selesai.Task;
    }

    /// <summary>Memuat filter yang sudah dikompilasi; Filter = 0 kalau belum ada.</summary>
    public static unsafe Task<(IntPtr Filter, string? Galat)> MuatFilter(IntPtr gudang, string id)
    {
        var selesai = new TaskCompletionSource<(IntPtr, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        webkit_user_content_filter_store_load(gudang, id, IntPtr.Zero, &SelesaiMuat, GCHandle.ToIntPtr(GCHandle.Alloc(selesai)));
        return selesai.Task;
    }

    [UnmanagedCallersOnly]
    static void SelesaiSimpan(IntPtr gudang, IntPtr hasil, IntPtr data) =>
        Akhiri(data, webkit_user_content_filter_store_save_from_file_finish(gudang, hasil, out var galat), galat);

    [UnmanagedCallersOnly]
    static void SelesaiMuat(IntPtr gudang, IntPtr hasil, IntPtr data) =>
        Akhiri(data, webkit_user_content_filter_store_load_finish(gudang, hasil, out var galat), galat);

    static void Akhiri(IntPtr data, IntPtr filter, IntPtr galat)
    {
        var pegangan = GCHandle.FromIntPtr(data);
        var selesai = (TaskCompletionSource<(IntPtr, string?)>)pegangan.Target!;
        pegangan.Free();
        string? pesan = null;
        if (galat != IntPtr.Zero)
        {
            pesan = Teks(Marshal.ReadIntPtr(galat, 8));
            g_error_free(galat);
        }
        selesai.SetResult((filter, pesan));
    }

    /// <summary>Menulis isi GBytes ke berkas.</summary>
    public static void TulisBytes(IntPtr bytes, string berkas)
    {
        using var aliran = File.Create(berkas);
        aliran.Write(IsiBytes(bytes));
    }

    /// <summary>Isi GBytes (tanpa disalin; berlaku selama GBytes-nya hidup).</summary>
    public static unsafe ReadOnlySpan<byte> IsiBytes(IntPtr bytes)
    {
        var data = g_bytes_get_data(bytes, out var ukuran);
        return new ReadOnlySpan<byte>((void*)data, checked((int)ukuran));
    }

    [LibraryImport(LibGio)]
    public static partial IntPtr g_file_get_uri(IntPtr berkas);

    [LibraryImport(LibGLib)]
    public static partial void g_free(IntPtr p);

    /// <summary>const gchar* → string (tidak dibebaskan).</summary>
    public static string Teks(IntPtr p) => Marshal.PtrToStringUTF8(p) ?? "";
}
