namespace HypenMaui.Services;

/// <summary>
/// Permission runtime untuk membaca file audio lokal.
/// Android 13+ (API 33) pakai READ_MEDIA_AUDIO, versi di bawahnya pakai READ_EXTERNAL_STORAGE.
/// </summary>
public class MediaAudioPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? new[] { (global::Android.Manifest.Permission.ReadMediaAudio!, true) }
            : new[] { (global::Android.Manifest.Permission.ReadExternalStorage!, true) };
#endif
}
