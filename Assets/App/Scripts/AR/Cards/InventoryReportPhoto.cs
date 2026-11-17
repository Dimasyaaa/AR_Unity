using System;
using System.IO;
using UnityEngine;

internal static class InventoryReportPhoto
{
    private const int MaxEdge = 1920;
    private const int MaxBytes = 4 * 1024 * 1024;
    internal const string LegacyStoragePermission = "android.permission.WRITE_EXTERNAL_STORAGE";

    internal static Texture2D Capture(out byte[] jpeg)
    {
        var frame = ScreenCapture.CaptureScreenshotAsTexture();
        if (frame == null) throw new InvalidOperationException("Не удалось получить кадр.");
        Texture2D scaled = null;
        try
        {
            float scale = Mathf.Min(1f, MaxEdge / (float)Mathf.Max(frame.width, frame.height));
            if (scale < 1f)
                scaled = Resize(frame, Mathf.RoundToInt(frame.width * scale), Mathf.RoundToInt(frame.height * scale));

            var image = scaled != null ? scaled : frame;
            jpeg = image.EncodeToJPG(88);
            if (jpeg.Length > MaxBytes) jpeg = image.EncodeToJPG(72);
            if (jpeg.Length == 0 || jpeg.Length > MaxBytes)
                throw new InvalidOperationException("Снимок превышает допустимый размер.");

            if (scaled != null) UnityEngine.Object.Destroy(frame);
            return image;
        }
        catch
        {
            UnityEngine.Object.Destroy(frame);
            if (scaled != null) UnityEngine.Object.Destroy(scaled);
            throw;
        }
    }

    private static Texture2D Resize(Texture2D source, int width, int height)
    {
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var resized = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            resized.Apply();
            return resized;
        }
        catch
        {
            UnityEngine.Object.Destroy(resized);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    internal static string SaveToGallery(byte[] jpeg)
    {
        var name = $"AR-Inventory_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
#if UNITY_ANDROID && !UNITY_EDITOR
        using var version = new AndroidJavaClass("android.os.Build$VERSION");
        using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
        if (version.GetStatic<int>("SDK_INT") >= 29)
        {
            SaveWithMediaStore(activity, name, jpeg);
        }
        else
        {
            using var environment = new AndroidJavaClass("android.os.Environment");
            using var pictures = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", "Pictures");
            string path = WritePhoto(pictures.Call<string>("getAbsolutePath"), name, jpeg);
            using var scanner = new AndroidJavaClass("android.media.MediaScannerConnection");
            scanner.CallStatic("scanFile", activity, new[] { path }, new[] { "image/jpeg" }, null);
        }
        return "Фото сохранено в галерее · AR Inventory";
#else
        string path = WritePhoto(Application.persistentDataPath, name, jpeg);
        return "Фото сохранено в папке AR Inventory. " + Path.GetFileName(path);
#endif
    }

    private static string WritePhoto(string directory, string name, byte[] jpeg)
    {
        string album = Path.Combine(directory, "AR Inventory");
        Directory.CreateDirectory(album);
        string path = Path.Combine(album, name);
        File.WriteAllBytes(path, jpeg);
        return path;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void SaveWithMediaStore(AndroidJavaObject activity, string name, byte[] jpeg)
    {
        using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
        using var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
        using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
        using var values = new AndroidJavaObject("android.content.ContentValues");
        values.Call("put", "_display_name", name);
        values.Call("put", "mime_type", "image/jpeg");
        values.Call("put", "relative_path", "Pictures/AR Inventory");
        values.Call("put", "is_pending", "1");
        using var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
        if (uri == null) throw new IOException("Галерея не создала запись для фото.");

        try
        {
            using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
            if (stream == null) throw new IOException("Галерея не открыла файл для записи.");
            var javaBytes = new sbyte[jpeg.Length];
            Buffer.BlockCopy(jpeg, 0, javaBytes, 0, jpeg.Length);
            try { stream.Call("write", javaBytes); }
            finally { stream.Call("close"); }

            values.Call("clear");
            values.Call("put", "is_pending", "0");
            if (resolver.Call<int>("update", uri, values, null, null) != 1)
                throw new IOException("Не удалось опубликовать фото в галерее.");
        }
        catch
        {
            resolver.Call<int>("delete", uri, null, null);
            throw;
        }
    }
#endif
}
