namespace KevinBrowser;

/// <summary>
/// Angka yang menentukan pemakaian RAM. Bawaannya dihitung dari RAM total
/// mesin; env <c>KEVIN_BROWSER_TAB_HIDUP</c>, <c>KEVIN_BROWSER_BATAS_MB</c>,
/// <c>KEVIN_BROWSER_TIDUR_DETIK</c>, dan <c>KEVIN_BROWSER_RAM_MENIPIS_MB</c>
/// menimpanya (untuk mengukur tanpa build ulang).
/// </summary>
/// <param name="TabHidupMaks">
/// Jumlah tab yang WebView-nya (dan proses web-nya) boleh hidup bersamaan,
/// termasuk tab aktif. Sisanya ditidurkan — lihat <see cref="TidurTab"/>.
/// </param>
/// <param name="BatasMemoriMB">
/// Batas memori per proses WebKit. Lewat 33% dari angka ini WebKit mulai
/// membuang cache, lewat 50% lebih agresif (ambang bawaan WebKit). Proses
/// tidak pernah dibunuh karenanya.
/// </param>
/// <param name="TidurSetelah">
/// Tab latar yang tidak dilihat selama ini ditidurkan, walau jumlah tab
/// hidup masih di bawah <paramref name="TabHidupMaks"/>.
/// </param>
/// <param name="RamMenipisMB">
/// Kalau RAM yang masih tersedia di laptop (MemAvailable, termasuk yang
/// dipakai program lain) di bawah angka ini, semua tab latar yang tidak
/// dilindungi langsung ditidurkan.
/// </param>
public sealed record Setelan(int TabHidupMaks, uint BatasMemoriMB, TimeSpan TidurSetelah, long RamMenipisMB)
{
    public static readonly TimeSpan TidurSetelahBawaan = TimeSpan.FromMinutes(5);

    public static Setelan Hitung(long ramMB) => new(
        Env("KEVIN_BROWSER_TAB_HIDUP") ?? TabHidupBawaan(ramMB),
        (uint)(Env("KEVIN_BROWSER_BATAS_MB") ?? BatasMemoriBawaan(ramMB)),
        Env("KEVIN_BROWSER_TIDUR_DETIK") is { } detik ? TimeSpan.FromSeconds(detik) : TidurSetelahBawaan,
        Env("KEVIN_BROWSER_RAM_MENIPIS_MB") ?? RamMenipisBawaan(ramMB));

    // Laptop "2 GB" melapor ~1,9 GB, "4 GB" ~3,8 GB.
    public static int TabHidupBawaan(long ramMB) => ramMB switch
    {
        <= 2048 => 2,
        <= 4096 => 3,
        _ => 4,
    };

    // Bawaan WebKit memakai min(RAM, 3 GB) sebagai batas — terlalu longgar
    // untuk mesin kecil: di laptop 2 GB cache baru dibuang setelah satu
    // proses memakan ~650 MB.
    public static long BatasMemoriBawaan(long ramMB) => Math.Clamp(ramMB / 4, 256, 1024);

    // 15% RAM total: sedikit lebih awal daripada WebKit sendiri, yang mulai
    // memberi sinyal tekanan memori ke semua proses saat RAM tersisa 10%
    // (MemoryPressureMonitor). Sinyal itu hanya membuang cache; menidurkan
    // tab melepas seluruh proses halamannya.
    public static long RamMenipisBawaan(long ramMB) => ramMB * 15 / 100;

    static int? Env(string nama) =>
        int.TryParse(Environment.GetEnvironmentVariable(nama), out var n) && n > 0 ? n : null;
}
