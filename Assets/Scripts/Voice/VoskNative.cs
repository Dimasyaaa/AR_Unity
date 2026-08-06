using System;
using System.Runtime.InteropServices;
using System.Text;

public static class VoskNative
{
    [DllImport("vosk")] private static extern IntPtr vosk_model_new(string path);
    [DllImport("vosk")] private static extern void vosk_model_free(IntPtr model);
    [DllImport("vosk")] private static extern IntPtr vosk_recognizer_new(IntPtr model, float sampleRate);
    [DllImport("vosk")] private static extern void vosk_recognizer_free(IntPtr rec);
    [DllImport("vosk")] private static extern int vosk_recognizer_accept_waveform_s(IntPtr rec, short[] data, int length);
    [DllImport("vosk")] private static extern IntPtr vosk_recognizer_result(IntPtr rec);
    [DllImport("vosk")] private static extern IntPtr vosk_recognizer_partial_result(IntPtr rec);

    public static IntPtr ModelNew(string path) => vosk_model_new(path);
    public static void ModelFree(IntPtr m) => vosk_model_free(m);
    public static IntPtr RecognizerNew(IntPtr m, float rate) => vosk_recognizer_new(m, rate);
    public static void RecognizerFree(IntPtr r) => vosk_recognizer_free(r);
    public static int AcceptWaveform(IntPtr r, short[] d, int len) => vosk_recognizer_accept_waveform_s(r, d, len);
    public static string Result(IntPtr r) => PtrUTF8(vosk_recognizer_result(r));
    public static string PartialResult(IntPtr r) => PtrUTF8(vosk_recognizer_partial_result(r));

    private static string PtrUTF8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "";
        int len = 0;
        while (Marshal.ReadByte(ptr, len) != 0) len++;
        byte[] buf = new byte[len];
        Marshal.Copy(ptr, buf, 0, len);
        return Encoding.UTF8.GetString(buf);
    }
}