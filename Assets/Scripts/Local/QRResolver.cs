using ArInventory.Local;
using System.Linq;
using UnityEngine;

/// <summary>
/// Единое место для резолва отсканированного QR в (заголовок, url).
/// - Если QR сам по себе URL — открываем его, заголовок берём из БД (object_name) или host.
/// - Если QR — код изделия (не URL), ищем запись в qr_codes.
/// - Если запись не найдена — помечаем как "не найден", не открываем google.com.
/// </summary>
public static class QRResolver
{
    public struct Result
    {
        public string title;
        public string url;
        public bool foundInDb;
    }

    public static Result Resolve(string qr)
    {
        var r = new Result { title = "QR-код", url = null, foundInDb = false };

        if (string.IsNullOrEmpty(qr))
        {
            r.title = "QR не отсканирован";
            return r;
        }

        bool isUrl = qr.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
                     qr.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);

        string host = "";
        if (isUrl)
        {
            try { host = new System.Uri(qr).Host; } catch { }
        }

        // Ищем в БД: в поле code может лежать как URL, так и код изделия
        LocalQrCode rec = null;
        if (LocalDatabase.Instance != null && LocalDatabase.Instance.IsReady &&
            LocalDatabase.Instance.Connection != null)
        {
            try
            {
                rec = LocalDatabase.Instance.Connection.Table<LocalQrCode>()
                        .Where(q => q.is_active && q.code == qr)
                        .FirstOrDefault();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[QRResolver] Ошибка запроса БД: " + e.Message);
            }
        }

        if (rec != null)
        {
            r.foundInDb = true;
            r.title = string.IsNullOrEmpty(rec.object_name) ? (isUrl ? host : qr) : rec.object_name;
        }
        else
        {
            r.title = isUrl ? (string.IsNullOrEmpty(host) ? "Веб-страница" : host) : qr;
        }

        // URL: открываем сам QR (он URL).
        // Код: если есть запись в БД — URL берётся... но его нет в qr_codes. Значит это код изделия без ссылки.
        if (isUrl)
        {
            r.url = qr;
        }
        else
        {
            r.url = null; // коды изделий без URL — только показываем карточку с названием
        }

        return r;
    }
}