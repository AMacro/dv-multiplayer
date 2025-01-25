using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class PacketCompression
{
    public static byte[] Compress(byte[] data)
    {
        using (var outputStream = new MemoryStream())
        {
            using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }
    }

    public static byte[] Decompress(byte[] compressedData)
    {
        using (var inputStream = new MemoryStream(compressedData))
        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        using (var outputStream = new MemoryStream())
        {
            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }
    }
}
