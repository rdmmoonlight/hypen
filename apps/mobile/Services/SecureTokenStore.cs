using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

/// <summary>
/// Titik akses tunggal untuk menyimpan token sesi (Last.fm session key, Google Drive
/// access token, TeraBox cookie, dsb). WAJIB dipakai untuk data ini — jangan Preferences.
///
/// Preferences.Default disimpan sebagai SharedPreferences teks-biasa (readable jika
/// perangkat di-root atau lewat adb backup). SecureStorage.Default di Android memakai
/// Android Keystore untuk mengenkripsi nilai sebelum ditulis ke disk, jadi cocok untuk
/// credential production.
/// </summary>
public static class SecureTokenStore
{
    public static Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public static Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public static void Remove(string key) => SecureStorage.Default.Remove(key);
}
