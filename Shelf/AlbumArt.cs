using System;
using System.IO;
using System.Text;

namespace Shelf
{
    /// <summary>Cover art embedded in audio files: ID3v2 APIC (MP3) and the MP4 "covr" atom (M4A / M4B).</summary>
    public static class AlbumArt
    {
        public static byte[] Read(string path)
        {
            try
            {
                using (var s = Fs.OpenRead(path))
                {
                    var head = new byte[12];
                    if (s.Read(head, 0, 12) < 12) return null;
                    s.Seek(0, SeekOrigin.Begin);
                    if (head[0] == 'I' && head[1] == 'D' && head[2] == '3') return Id3(s);
                    if (head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p') return Mp4(s);
                }
            }
            catch { }
            return null;
        }

        static int Synchsafe(byte[] b, int o) => (b[o] & 0x7f) << 21 | (b[o + 1] & 0x7f) << 14 | (b[o + 2] & 0x7f) << 7 | (b[o + 3] & 0x7f);
        static int BigEndian(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

        static byte[] Id3(Stream s)
        {
            var h = new byte[10];
            s.Read(h, 0, 10);
            int version = h[3];
            int size = Synchsafe(h, 6);
            if (size <= 0 || size > 32 * 1024 * 1024 || version < 3) return null;
            var tag = new byte[size];
            int got = 0, n;
            while (got < size && (n = s.Read(tag, got, size - got)) > 0) got += n;
            int pos = 0;
            if ((h[5] & 0x40) != 0) pos += version == 4 ? Synchsafe(tag, 0) : BigEndian(tag, 0) + 4; // extended header
            while (pos + 10 <= got)
            {
                var id = Encoding.ASCII.GetString(tag, pos, 4);
                if (id[0] == 0) break;
                int fsize = version == 4 ? Synchsafe(tag, pos + 4) : BigEndian(tag, pos + 4);
                int body = pos + 10;
                if (fsize <= 0 || body + fsize > got) break;
                if (id == "APIC")
                {
                    int p = body;
                    byte enc = tag[p++];
                    while (p < body + fsize && tag[p] != 0) p++; // MIME type
                    p++;
                    p++; // picture type
                    if (enc == 1 || enc == 2) { while (p + 1 < body + fsize && !(tag[p] == 0 && tag[p + 1] == 0)) p += 2; p += 2; }
                    else { while (p < body + fsize && tag[p] != 0) p++; p++; }
                    int len = body + fsize - p;
                    if (len <= 0) return null;
                    var img = new byte[len];
                    Array.Copy(tag, p, img, 0, len);
                    return img;
                }
                pos = body + fsize;
            }
            return null;
        }

        /// <summary>moov → udta → meta → ilst → covr → data.</summary>
        static byte[] Mp4(Stream s)
        {
            return FindCovr(s, 0, s.Length, 0);
        }

        static byte[] FindCovr(Stream s, long start, long end, int depth)
        {
            var h = new byte[8];
            long pos = start;
            while (pos + 8 <= end && depth < 8)
            {
                s.Seek(pos, SeekOrigin.Begin);
                if (s.Read(h, 0, 8) < 8) return null;
                long size = (uint)BigEndian(h, 0);
                var type = Encoding.ASCII.GetString(h, 4, 4);
                long headerLen = 8;
                if (size == 1)
                {
                    var big = new byte[8]; s.Read(big, 0, 8);
                    size = (long)((ulong)(uint)BigEndian(big, 0) << 32 | (uint)BigEndian(big, 4));
                    headerLen = 16;
                }
                else if (size == 0) size = end - pos;
                if (size < headerLen) return null;
                switch (type)
                {
                    case "moov": case "udta": case "ilst":
                        { var r = FindCovr(s, pos + headerLen, pos + size, depth + 1); if (r != null) return r; break; }
                    case "meta":
                        { var r = FindCovr(s, pos + headerLen + 4, pos + size, depth + 1); if (r != null) return r; break; } // version/flags
                    case "covr":
                        {
                            // child "data" atom: size, "data", 4 bytes type, 4 bytes locale, image
                            s.Seek(pos + headerLen, SeekOrigin.Begin);
                            var d = new byte[16];
                            if (s.Read(d, 0, 16) < 16) return null;
                            long dsize = (uint)BigEndian(d, 0);
                            if (Encoding.ASCII.GetString(d, 4, 4) != "data" || dsize <= 16 || dsize > 32 * 1024 * 1024) return null;
                            var img = new byte[dsize - 16];
                            int got = 0, n;
                            while (got < img.Length && (n = s.Read(img, got, img.Length - got)) > 0) got += n;
                            return img;
                        }
                }
                pos += size;
            }
            return null;
        }
    }
}
