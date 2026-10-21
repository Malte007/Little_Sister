using System.Buffers.Binary;

namespace LittleSister.Infrastructure.Recording;

/// <summary>Minimaler Leser für die oberste Box-Ebene einer MP4-Datei.</summary>
public static class Mp4Boxes
{
    /// <summary>
    /// Größe des Initialisierungsteils (alles vor dem ersten "moof") einer fragmentierten MP4-Datei.
    /// null, wenn die Datei nicht lesbar oder nicht fragmentiert ist.
    /// </summary>
    public static int? FindInitSize(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return FindInitSize(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static int? FindInitSize(Stream stream)
    {
        Span<byte> header = stackalloc byte[16];
        long offset = 0;
        // Der Kopf ist wenige KB groß; mehr als 64 Boxen deuten auf eine fremde Datei hin
        for (var i = 0; i < 64; i++)
        {
            stream.Position = offset;
            if (stream.ReadAtLeast(header[..8], 8, throwOnEndOfStream: false) < 8)
                return null;

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = System.Text.Encoding.ASCII.GetString(header[4..8]);
            if (type == "moof")
                return offset <= int.MaxValue ? (int)offset : null;

            if (size == 1)
            {
                // 64-Bit-Größe direkt nach dem Typ
                if (stream.ReadAtLeast(header[8..16], 8, throwOnEndOfStream: false) < 8)
                    return null;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
            }
            if (size < 8)
                return null; // 0 = "bis Dateiende", danach kann kein moof mehr kommen
            offset += size;
        }
        return null;
    }
}
