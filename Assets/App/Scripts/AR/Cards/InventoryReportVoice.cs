using System;
using System.IO;
using System.Text;
using UnityEngine;

internal static class InventoryReportVoice
{
    internal const int Rate = 16000;
    internal const int MaxSeconds = 60;

    internal static byte[] Encode(AudioClip recording, int frames, out AudioClip preview)
    {
        preview = null;
        var input = new float[frames * recording.channels];
        if (!recording.GetData(input, 0))
            throw new InvalidOperationException("Не удалось прочитать запись микрофона.");

        int count = Mathf.Min(MaxSeconds * Rate, Mathf.FloorToInt(frames * (Rate / (float)recording.frequency)));
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            float position = i * (recording.frequency / (float)Rate);
            int first = Mathf.Min((int)position, frames - 1);
            int next = Mathf.Min(first + 1, frames - 1);
            float sum = 0;
            for (int channel = 0; channel < recording.channels; channel++)
                sum += Mathf.Lerp(input[first * recording.channels + channel],
                    input[next * recording.channels + channel], position - first);
            samples[i] = Mathf.Clamp(sum / recording.channels, -1f, 1f);
        }

        using var stream = new MemoryStream(44 + count * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(count * 2);
        foreach (float sample in samples)
            writer.Write((short)Mathf.RoundToInt(sample * short.MaxValue));

        preview = AudioClip.Create("Голосовое сообщение", count, 1, Rate, false);
        preview.SetData(samples, 0);
        return stream.ToArray();
    }
}
