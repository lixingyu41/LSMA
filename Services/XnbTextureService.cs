using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace LSMA.Services;

public sealed class XnbTextureService
{
    private const byte LzxCompressedFlag = 0x80;
    private const byte Lz4CompressedFlag = 0x40;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public Task ExportPngAsync(string xnbPath, string pngPath, string gameDirectory)
        => Task.Run(() => ExportPng(xnbPath, pngPath, gameDirectory, null));

    public Task ExportPngRegionAsync(
        string xnbPath,
        string pngPath,
        string gameDirectory,
        int x,
        int y,
        int width,
        int height)
        => Task.Run(() => ExportPng(xnbPath, pngPath, gameDirectory, new TextureRegion(x, y, width, height)));

    private static void ExportPng(string xnbPath, string pngPath, string gameDirectory, TextureRegion? region)
    {
        using var input = File.OpenRead(xnbPath);
        using var header = new BinaryReader(input, Encoding.UTF8, true);
        if (Encoding.ASCII.GetString(header.ReadBytes(3)) != "XNB")
        {
            throw new InvalidDataException("素材文件不是有效的 XNB 文件。");
        }

        header.ReadByte();
        var version = header.ReadByte();
        var flags = header.ReadByte();
        var declaredSize = header.ReadInt32();
        if (version != 5 || declaredSize != input.Length)
        {
            throw new InvalidDataException("素材文件使用了不支持的 XNB 格式。");
        }

        Stream payload = input;
        if ((flags & LzxCompressedFlag) != 0)
        {
            var decompressedSize = header.ReadInt32();
            payload = OpenLzxPayload(input, decompressedSize, checked((int)(input.Length - input.Position)), gameDirectory);
        }
        else if ((flags & Lz4CompressedFlag) != 0)
        {
            throw new InvalidDataException("素材文件使用了不支持的 LZ4 XNB 压缩格式。");
        }

        try
        {
            var texture = ReadTexture(payload);
            if (region is not null)
            {
                texture = Crop(texture, region);
            }

            WritePng(pngPath, texture);
        }
        finally
        {
            if (!ReferenceEquals(payload, input))
            {
                payload.Dispose();
            }
        }
    }

    private static Stream OpenLzxPayload(Stream input, int decompressedSize, int compressedSize, string gameDirectory)
    {
        var runtimePath = Path.Combine(gameDirectory, "MonoGame.Framework.dll");
        if (!File.Exists(runtimePath))
        {
            throw new FileNotFoundException("游戏安装缺少 MonoGame.Framework.dll，无法读取本地素材。", runtimePath);
        }

        var assembly = Assembly.LoadFrom(runtimePath);
        var decoderType = assembly.GetType("MonoGame.Framework.Utilities.LzxDecoderStream")
            ?? throw new InvalidOperationException("游戏运行时不包含 XNB 解码组件。");
        var constructor = decoderType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [typeof(Stream), typeof(int), typeof(int)],
            null) ?? throw new InvalidOperationException("游戏运行时的 XNB 解码组件不兼容。");

        return constructor.Invoke([input, decompressedSize, compressedSize]) as Stream
            ?? throw new InvalidOperationException("无法创建 XNB 解码流。");
    }

    private static TextureData ReadTexture(Stream payload)
    {
        using var reader = new BinaryReader(payload, Encoding.UTF8, true);
        var readerCount = reader.Read7BitEncodedInt();
        var isTexture = false;
        for (var index = 0; index < readerCount; index++)
        {
            isTexture |= reader.ReadString().Contains("Texture2DReader", StringComparison.Ordinal);
            reader.ReadInt32();
        }

        reader.Read7BitEncodedInt();
        var rootReader = reader.Read7BitEncodedInt();
        if (!isTexture || rootReader == 0)
        {
            throw new InvalidDataException("素材 XNB 不包含可读取的纹理。");
        }

        var surfaceFormat = reader.ReadInt32();
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var mipCount = reader.ReadInt32();
        var byteCount = reader.ReadInt32();
        if (surfaceFormat != 0 || width <= 0 || height <= 0 || mipCount <= 0)
        {
            throw new InvalidDataException("素材 XNB 的纹理格式不受支持。");
        }

        var expectedLength = checked(width * height * 4);
        if (byteCount != expectedLength)
        {
            throw new InvalidDataException("素材 XNB 的像素长度无效。");
        }

        var pixels = reader.ReadBytes(byteCount);
        if (pixels.Length != byteCount)
        {
            throw new EndOfStreamException("素材 XNB 的像素数据不完整。");
        }

        return new TextureData(width, height, pixels);
    }

    private static TextureData Crop(TextureData texture, TextureRegion region)
    {
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || region.X + region.Width > texture.Width
            || region.Y + region.Height > texture.Height)
        {
            throw new InvalidDataException("素材图标裁切区域超出纹理范围。");
        }

        var pixels = new byte[checked(region.Width * region.Height * 4)];
        for (var row = 0; row < region.Height; row++)
        {
            Buffer.BlockCopy(
                texture.Pixels,
                ((region.Y + row) * texture.Width + region.X) * 4,
                pixels,
                row * region.Width * 4,
                region.Width * 4);
        }

        return new TextureData(region.Width, region.Height, pixels);
    }

    private static void WritePng(string path, TextureData texture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path);
        output.Write(PngSignature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), texture.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), texture.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        for (var row = 0; row < texture.Height; row++)
        {
            raw.WriteByte(0);
            raw.Write(texture.Pixels, row * texture.Width * 4, texture.Width * 4);
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = UpdateCrc(0xFFFFFFFF, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFF;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }

    private sealed record TextureData(int Width, int Height, byte[] Pixels);
    private sealed record TextureRegion(int X, int Y, int Width, int Height);
}
