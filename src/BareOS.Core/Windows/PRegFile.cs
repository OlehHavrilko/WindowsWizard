namespace BareOS.Core.Windows;

/// <summary>Чтение и запись формата PReg (Registry.pol локальной групповой политики). Чистые функции над байтами:
/// без файлового или процессного ввода-вывода, поэтому проверяются без Windows. Формат:
/// сигнатура "PReg" + версия (uint32 LE) + записи [key;value;type;size;data].</summary>
public static class PRegFile
{
    private static readonly byte[] Signature = { (byte)'P', (byte)'R', (byte)'e', (byte)'g' };
    private const uint Version = 1;

    public sealed record Entry(string Key, string Value, uint Type, byte[] Data);

    public static IReadOnlyList<Entry> Parse(byte[] bytes)
    {
        var entries = new List<Entry>();
        if (bytes.Length < 8) return entries;
        if (bytes[0] != Signature[0] || bytes[1] != Signature[1] || bytes[2] != Signature[2] || bytes[3] != Signature[3])
            throw new FormatException("Registry.pol: нет сигнатуры PReg");
        int pos = 8;
        while (pos < bytes.Length)
        {
            Expect(bytes, ref pos, '[');
            string key = ReadWideString(bytes, ref pos); Expect(bytes, ref pos, ';');
            string value = ReadWideString(bytes, ref pos); Expect(bytes, ref pos, ';');
            uint type = ReadUInt32(bytes, ref pos); Expect(bytes, ref pos, ';');
            uint size = ReadUInt32(bytes, ref pos); Expect(bytes, ref pos, ';');
            byte[] data = bytes.AsSpan(pos, checked((int)size)).ToArray(); pos += (int)size;
            Expect(bytes, ref pos, ']');
            entries.Add(new Entry(key, value, type, data));
        }
        return entries;
    }

    public static byte[] Serialize(IReadOnlyList<Entry> entries)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature); WriteUInt32(ms, Version);
        foreach (var e in entries)
        {
            ms.WriteByte((byte)'[');
            WriteWideString(ms, e.Key); ms.WriteByte((byte)';');
            WriteWideString(ms, e.Value); ms.WriteByte((byte)';');
            WriteUInt32(ms, e.Type); ms.WriteByte((byte)';');
            WriteUInt32(ms, (uint)e.Data.Length); ms.WriteByte((byte)';');
            ms.Write(e.Data);
            ms.WriteByte((byte)']');
        }
        return ms.ToArray();
    }

    private static void Expect(byte[] bytes, ref int pos, char c)
    {
        if (pos >= bytes.Length || bytes[pos] != (byte)c)
            throw new FormatException($"Registry.pol: ожидался '{c}' на позиции {pos}");
        pos++;
    }

    private static string ReadWideString(byte[] bytes, ref int pos)
    {
        int start = pos;
        while (pos + 1 < bytes.Length && !(bytes[pos] == 0 && bytes[pos + 1] == 0)) pos += 2;
        var s = System.Text.Encoding.Unicode.GetString(bytes, start, pos - start);
        pos += 2;
        return s;
    }

    private static void WriteWideString(MemoryStream ms, string s)
    {
        ms.Write(System.Text.Encoding.Unicode.GetBytes(s));
        ms.WriteByte(0); ms.WriteByte(0);
    }

    private static uint ReadUInt32(byte[] bytes, ref int pos)
    {
        uint v = BitConverter.ToUInt32(bytes, pos);
        pos += 4;
        return v;
    }

    private static void WriteUInt32(MemoryStream ms, uint v) => ms.Write(BitConverter.GetBytes(v));
}
