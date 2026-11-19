using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Backend.Services;

/// <summary>
/// Чистая бизнес-логика по работе с медиа-вложениями отчётов инвентаризации.
/// Перенесена из InventoryApi.cs (методы ReadReportMedia и DecodeAttachment).
/// Никакого SQL здесь нет — только декодирование base64 и валидация форматов JPEG/WAV.
/// Все константы (лимиты размеров, сигнатуры файлов) сохранены 1-в-1 из оригинала.
/// </summary>
public static class ReportMediaService
{
    // Максимальный размер одной фотографии (4 МБ)
    private const int MaxReportPhotoBytes = 4 * 1024 * 1024;

    // Максимальный размер голосового сообщения: 44 байта заголовка WAV + 60 секунд * 16000 Гц * 2 байта (16-bit PCM)
    private const int MaxReportVoiceBytes = 44 + 60 * 16000 * 2;

    /// <summary>
    /// Извлекает и валидирует медиа из запроса синхронизации.
    /// Возвращает кортеж (список JPEG, WAV) или null, если медиа нет вообще.
    /// Бросает ArgumentException при нарушении формата/размера — эндпоинт превратит это в 400.
    /// </summary>
    public static (List<byte[]> Photos, byte[] Voice)? ExtractReportMedia(
        List<string>? photosBase64,
        string? singlePhotoBase64,
        string? voiceBase64)
    {
        // Поддержка двух форматов от разных версий клиента:
        // новые APK шлют список photosBase64, старые — одну строку photoBase64.
        var encodedPhotos = photosBase64 is { Count: > 0 }
            ? photosBase64
            : string.IsNullOrEmpty(singlePhotoBase64)
                ? new List<string>()
                : new List<string> { singlePhotoBase64 };

        // Не более 5 фотографий в одном отчёте
        if (encodedPhotos.Count > 5)
            throw new ArgumentException("В отчёте допускается не более 5 фотографий.");

        // Если нет ни фото, ни голоса — медиа-блок отсутствует (это не ошибка, просто чек-лист)
        if (encodedPhotos.Count == 0 && string.IsNullOrEmpty(voiceBase64))
            return null;

        // Декодируем и проверяем каждую фотографию
        var photos = encodedPhotos
            .Select(encoded => DecodeAttachment(encoded, MaxReportPhotoBytes, "фото"))
            .ToList();

        // Суммарный размер всех фото не должен превышать 12 МБ
        if (photos.Sum(photo => (long)photo.Length) > 12L * 1024 * 1024)
            throw new ArgumentException("Общий размер фотографий в отчёте не должен превышать 12 МБ.");

        // Проверяем сигнатуру JPEG: начинается с FF D8, заканчивается FF D9
        if (photos.Any(photo =>
            photo.Length < 4 ||
            photo[0] != 0xff || photo[1] != 0xd8 ||
            photo[^2] != 0xff || photo[^1] != 0xd9))
        {
            throw new ArgumentException("Проверка пропущена: ожидается JPEG для каждой фотографии.");
        }

        // Декодируем и проверяем голосовое сообщение
        byte[] voice = DecodeAttachment(voiceBase64, MaxReportVoiceBytes, "голосовое сообщение");

        // Полная валидация WAV-заголовка (RIFF/WAVE/fmt /data, 16 kHz, mono, 16-bit PCM)
        ValidateWavHeader(voice);

        return (photos, voice);
    }

    /// <summary>
    /// Декодирует base64-строку в массив байт с проверкой размера.
    /// Бросает ArgumentException, если строка пустая, невалидный base64 или превышает лимит.
    /// </summary>
    private static byte[] DecodeAttachment(string? encoded, int limit, string name)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > ((limit + 2) / 3) * 4)
            throw new ArgumentException($"Проверка пропущена: отсутствует или превышает допустимый размер вложение «{name}».");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new ArgumentException($"Проверка пропущена: повреждено вложение «{name}».");
        }

        if (bytes.Length == 0 || bytes.Length > limit)
            throw new ArgumentException($"Проверка пропущена: недопустимый размер вложения «{name}».");

        return bytes;
    }

    /// <summary>
    /// Проверяет, что байты соответствуют формату WAV PCM 16 kHz mono 16-bit.
    /// Все смещения и значения — 1-в-1 из оригинального InventoryApi.cs.
    /// </summary>
    private static void ValidateWavHeader(byte[] voice)
    {
        if (voice.Length < 46)
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");

        // RIFF....WAVEfmt 
        if (!voice.AsSpan(0, 4).SequenceEqual("RIFF"u8))
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (!voice.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8))
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");

        // Размер файла в заголовке RIFF должен совпадать с реальным
        if (BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(4)) != voice.Length - 8)
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");

        // fmt chunk: PCM=1, channels=1, sampleRate=16000, byteRate=32000, blockAlign=2, bitsPerSample=16
        if (BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(20)) != 1)    // wFormatTag = PCM
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(22)) != 1)    // nChannels = mono
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(24)) != 16000) // nSamplesPerSec
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(28)) != 32000) // nAvgBytesPerSec
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(32)) != 2)    // nBlockAlign
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(34)) != 16)   // wBitsPerSample
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");

        // data chunk
        if (!voice.AsSpan(36, 4).SequenceEqual("data"u8))
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        if (BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(40)) != voice.Length - 44)
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");

        // WAV должен иметь чётное количество байт (16-bit samples)
        if (voice.Length % 2 != 0)
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
    }
}