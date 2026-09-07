using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using WzComparerR2.WzLib;
using WzComparerR2.WzLib.Utilities;

namespace WzComparerR2.Headless.Media
{
    public static class CrossPlatformPngWriter
    {
        private static readonly byte[] PngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static void Save(Wz_Png png, int page, string path)
        {
            DecodedPngPixels decoded = DecodeToBgra32(png, page);
            using (var output = File.Create(path))
            {
                output.Write(PngSignature, 0, PngSignature.Length);
                WriteIhdr(output, decoded.Width, decoded.Height);
                WriteIdat(output, decoded);
                WriteChunk(output, "IEND", Array.Empty<byte>());
            }
        }

        internal static DecodedPngPixels DecodeToBgra32(Wz_Png png, int page)
        {
            ValidatePage(png, page);

            int dataSizePerPage = png.GetRawDataSizePerPage();
            byte[] rawData = new byte[dataSizePerPage];
            int actualBytes = png.GetRawData(page * dataSizePerPage, rawData);
            if (actualBytes != dataSizePerPage)
            {
                throw new InvalidOperationException("Not enough bytes have been read. (actual:" + actualBytes + ", expected:" + dataSizePerPage + ")");
            }

            switch (png.Format)
            {
                case Wz_TextureFormat.ARGB4444 when png.ActualScale == 1:
                    return DecodeBgra4444(png, rawData);

                case Wz_TextureFormat.ARGB8888 when png.ActualScale == 1:
                    return new DecodedPngPixels(png.Width, png.Height, rawData);

                case Wz_TextureFormat.ARGB1555 when png.ActualScale == 1:
                    return DecodeArgb1555(png, rawData);

                case Wz_TextureFormat.RGB565 when png.ActualScale == 1:
                    return DecodeRgb565(png.Width, png.Height, rawData);

                case Wz_TextureFormat.RGB565 when png.ActualScale == 16:
                    return DecodeScaledRgb565(png, rawData);

                case Wz_TextureFormat.DXT3 when png.ActualScale == 1:
                    return DecodeDxt3(png, rawData);

                case Wz_TextureFormat.DXT5 when png.ActualScale == 1:
                    return DecodeDxt5(png, rawData);

                case Wz_TextureFormat.A8 when png.ActualScale == 1:
                    return DecodeA8(png, rawData);

                case Wz_TextureFormat.RGBA1010102 when png.ActualScale == 1:
                    return DecodeRgba1010102(png, rawData);

                case Wz_TextureFormat.BC7 when png.ActualScale == 1:
                    return DecodeBc7(png, rawData);

                default:
                    throw new NotSupportedException("Cross-platform PNG export does not support texture format " + png.Format + " with scale=" + png.ActualScale + ".");
            }
        }

        private static void ValidatePage(Wz_Png png, int page)
        {
            if (png.Pages > 0)
            {
                if (page < 0 || page >= png.Pages)
                {
                    throw new ArgumentOutOfRangeException(nameof(page));
                }
                return;
            }

            if (page != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(page));
            }
        }

        private static DecodedPngPixels DecodeBgra4444(Wz_Png png, byte[] rawData)
        {
            byte[] bgra = new byte[png.Width * png.Height * 4];
            ImageCodec.BGRA4444ToBGRA32(rawData, bgra);
            return new DecodedPngPixels(png.Width, png.Height, bgra);
        }

        private static DecodedPngPixels DecodeArgb1555(Wz_Png png, byte[] rawData)
        {
            byte[] bgra = new byte[png.Width * png.Height * 4];
            for (int src = 0, dst = 0; src < rawData.Length; src += 2, dst += 4)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(rawData.AsSpan(src, 2));
                byte b = Expand5(value & 0x001f);
                byte g = Expand5((value >> 5) & 0x001f);
                byte r = Expand5((value >> 10) & 0x001f);
                byte a = (value & 0x8000) == 0 ? (byte)0 : (byte)255;
                bgra[dst] = b;
                bgra[dst + 1] = g;
                bgra[dst + 2] = r;
                bgra[dst + 3] = a;
            }
            return new DecodedPngPixels(png.Width, png.Height, bgra);
        }

        private static DecodedPngPixels DecodeRgb565(int width, int height, byte[] rawData)
        {
            byte[] bgra = new byte[width * height * 4];
            for (int src = 0, dst = 0; src < rawData.Length; src += 2, dst += 4)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(rawData.AsSpan(src, 2));
                bgra[dst] = Expand5(value & 0x001f);
                bgra[dst + 1] = Expand6((value >> 5) & 0x003f);
                bgra[dst + 2] = Expand5((value >> 11) & 0x001f);
                bgra[dst + 3] = 255;
            }
            return new DecodedPngPixels(width, height, bgra);
        }

        private static DecodedPngPixels DecodeScaledRgb565(Wz_Png png, byte[] rawData)
        {
            int rawWidth = png.Width / png.ActualScale;
            int rawHeight = png.Height / png.ActualScale;
            byte[] scaled = new byte[png.Width * png.Height * 2];
            ImageCodec.ScalePixels(rawData, 2, rawWidth, rawWidth * 2, rawHeight, png.ActualScale, png.ActualScale, scaled, png.Width * 2);
            return DecodeRgb565(png.Width, png.Height, scaled);
        }

        private static DecodedPngPixels DecodeDxt3(Wz_Png png, byte[] rawData)
        {
            byte[] bgra = new byte[png.Width * png.Height * 4];
            ImageCodec.DXT3ToBGRA32(rawData, bgra, png.Width, png.Width * 4, png.Height);
            return new DecodedPngPixels(png.Width, png.Height, bgra);
        }

        private static DecodedPngPixels DecodeDxt5(Wz_Png png, byte[] rawData)
        {
            byte[] bgra = new byte[png.Width * png.Height * 4];
            ImageCodec.DXT5ToBGRA32(rawData, bgra, png.Width, png.Width * 4, png.Height);
            return new DecodedPngPixels(png.Width, png.Height, bgra);
        }

        private static DecodedPngPixels DecodeA8(Wz_Png png, byte[] rawData)
        {
            byte[] bgra = new byte[png.Width * png.Height * 4];
            for (int i = 0, dst = 0; i < rawData.Length; i++, dst += 4)
            {
                bgra[dst] = 255;
                bgra[dst + 1] = 255;
                bgra[dst + 2] = 255;
                bgra[dst + 3] = rawData[i];
            }
            return new DecodedPngPixels(png.Width, png.Height, bgra);
        }

        private static DecodedPngPixels DecodeRgba1010102(Wz_Png png, byte[] rawData)
        {
            byte[] bgra = new byte[png.Width * png.Height * 4];
            ImageCodec.R10G10B10A2ToBGRA32(rawData, bgra);
            return new DecodedPngPixels(png.Width, png.Height, bgra);
        }

        private static DecodedPngPixels DecodeBc7(Wz_Png png, byte[] rawData)
        {
            int width = png.Width & ~3;
            int height = png.Height & ~3;
            byte[] rgba = new byte[width * height * 4];
            ImageCodec.BC7ToRGBA32(rawData, png.Width * 4, rgba, width, width * 4, height);
            ImageCodec.RGBA32ToBGRA32(rgba, rgba);
            return new DecodedPngPixels(width, height, rgba);
        }

        private static byte Expand5(int value)
        {
            return (byte)((value << 3) | (value >> 2));
        }

        private static byte Expand6(int value)
        {
            return (byte)((value << 2) | (value >> 4));
        }

        private static void WriteIhdr(Stream output, int width, int height)
        {
            byte[] ihdr = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
            BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            WriteChunk(output, "IHDR", ihdr);
        }

        private static void WriteIdat(Stream output, DecodedPngPixels decoded)
        {
            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
                {
                    WriteScanlines(zlib, decoded);
                }

                WriteChunk(output, "IDAT", compressed.ToArray());
            }
        }

        private static void WriteScanlines(Stream output, DecodedPngPixels decoded)
        {
            int sourceStride = decoded.Width * 4;
            byte[] row = new byte[1 + sourceStride];
            for (int y = 0; y < decoded.Height; y++)
            {
                row[0] = 0;
                int src = y * sourceStride;
                for (int x = 0, dst = 1; x < decoded.Width; x++, dst += 4, src += 4)
                {
                    row[dst] = decoded.Bgra[src + 2];
                    row[dst + 1] = decoded.Bgra[src + 1];
                    row[dst + 2] = decoded.Bgra[src];
                    row[dst + 3] = decoded.Bgra[src + 3];
                }
                output.Write(row, 0, row.Length);
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            Span<byte> lengthBytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
            output.Write(lengthBytes);
            output.Write(typeBytes, 0, typeBytes.Length);
            output.Write(data, 0, data.Length);

            uint crc = Crc32.Compute(typeBytes, data);
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
            output.Write(crcBytes);
        }

        internal sealed class DecodedPngPixels
        {
            public DecodedPngPixels(int width, int height, byte[] bgra)
            {
                this.Width = width;
                this.Height = height;
                this.Bgra = bgra;
            }

            public int Width { get; private set; }
            public int Height { get; private set; }
            public byte[] Bgra { get; private set; }
        }

        private static class Crc32
        {
            private static readonly uint[] Table = CreateTable();

            public static uint Compute(byte[] type, byte[] data)
            {
                uint crc = 0xffffffffu;
                crc = Update(crc, type);
                crc = Update(crc, data);
                return crc ^ 0xffffffffu;
            }

            private static uint Update(uint crc, byte[] data)
            {
                foreach (byte value in data)
                {
                    crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
                }
                return crc;
            }

            private static uint[] CreateTable()
            {
                var table = new uint[256];
                for (uint i = 0; i < table.Length; i++)
                {
                    uint crc = i;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
                    }
                    table[i] = crc;
                }
                return table;
            }
        }
    }
}
