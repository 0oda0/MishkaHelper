using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MishkaHelper.Core;

/// <summary>
/// Sec-MS-GEC v2 token for the Edge read-aloud service: "&lt;CS64 16 hex&gt;-&lt;SHA-256 64 hex&gt;".
/// Ported 1:1 from edge-tts (src/edge_tts/drm.py, "2-" protocol, 2026). The odd constants are Microsoft's — don't "fix" them.
/// </summary>
public static class EdgeGec
{
    static readonly uint[] Initial = { 0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19 };
    static readonly uint[] State0 = { 0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A, 0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2 };
    static readonly uint[] Schedule = { 0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA, 0xA622EE8C, 0xD1DD9491, 0xC0DD25A0, 0xF9E70AB6, 0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33, 0xD258B060, 0x72766564 };
    static readonly uint[] K =
    {
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5, 0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA, 0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85, 0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3, 0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
    };
    const uint Adjust = 0xC9DC06A7, Sigma0Adjust = 0x059C0B80, Sigma1Adjust = 0x000B92C0;
    static readonly (uint Coef, uint Off)[] Cs64 =
    {
        (0xE82E91B1, 0xB32700D0), (0x44DE24C6, 0x88DFF51D), (0x3BEB65BC, 0x29514038), (0xC89C5933, 0xB328CBDF), (0x8D1EC211, 0xD405B4D5),
        (0x314EF5D5, 0x26E55F3C), (0xE92491B1, 0x607801C6), (0xF0A5B9D5, 0x82B2C1FB), (0x06064CF3, 0xFA51A3DA), (0xDA79FA60, 0x207CC464),
        (0xBED20560, 0x3C3C74F3), (0x89F9C9F8, 0xD2D14B61), (0x35FA38C6, 0xBB04A2C1), (0xCB49E3C6, 0xC5626167),
    };

    static uint Rol16(uint v) => BitOperations.RotateLeft(v, 16);
    static uint S0(uint v) => BitOperations.RotateRight(v, 7) ^ BitOperations.RotateRight(v, 18) ^ (v >> 3);
    static uint S1(uint v) => BitOperations.RotateRight(v, 17) ^ BitOperations.RotateRight(v, 19) ^ (v >> 10);

    static void Expand(uint[] w, int from)
    {
        for (int i = from; i < 64; i++) w[i] = unchecked(S1(w[i - 2]) + w[i - 7] + S0(w[i - 15]) + w[i - 16]);
    }

    static uint[] Rounds(uint[] state, uint[] w, int first)
    {
        uint a = state[0], b = state[1], c = state[2], d = state[3], e = state[4], f = state[5], g = state[6], h = state[7];
        for (int i = first; i < 64; i++)
        {
            uint k = first == 14 && i == 14 ? Adjust : K[i];
            uint t1 = unchecked(h + (BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25)) + (g ^ (e & (f ^ g))) + k + w[i]);
            uint t2 = unchecked((BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22)) + ((a & b) | (c & (a | b))));
            h = g; g = f; f = e; e = unchecked(d + t1); d = c; c = b; b = a; a = unchecked(t1 + t2);
        }
        return new[] { a, b, c, d, e, f, g, h };
    }

    static uint[] Init(byte[] data)
    {
        var w = new uint[64];
        w[14] = (uint)(data[0] << 16 | data[1] << 8 | data[2]);
        w[15] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(3, 4));
        for (int i = 16; i < 30; i++)
        {
            uint v = Schedule[i - 16];
            v = unchecked(v + (i == 16 ? S1(w[i - 2]) ^ Sigma1Adjust : S1(w[i - 2])));
            if (i >= 21) v = unchecked(v + w[i - 7]);
            if (i == 29) v = unchecked(v + (S0(w[i - 15]) ^ Sigma0Adjust));
            w[i] = v;
        }
        w[30] = unchecked(S1(w[28]) + w[23] + S0(w[15]) + w[14] + Adjust - K[14]);
        Expand(w, 31);
        var work = Rounds(State0, w, 14);
        return Initial.Select((x, i) => unchecked(x + work[i])).ToArray();
    }

    internal static byte[] Sha(byte[] data)
    {
        int length = 57 + data.Length;
        int pad = ((55 - length) % 64 + 64) % 64;
        var padded = new byte[data.Length - 7 + 1 + pad + 8];
        data.AsSpan(7).CopyTo(padded);
        padded[data.Length - 7] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(padded.Length - 8), (ulong)length * 8);
        var state = Init(data);
        for (int off = 0; off < padded.Length; off += 64)
        {
            var w = new uint[64];
            for (int i = 0; i < 16; i++) w[i] = BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(off + i * 4, 4));
            Expand(w, 16);
            var work = Rounds(state, w, 0);
            for (int i = 0; i < 8; i++) state[i] = unchecked(state[i] + work[i]);
        }
        var res = new byte[32];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(res.AsSpan(i * 4), state[i]);
        return res;
    }

    static (uint, uint) Cs(byte[] data, byte[] digest)
    {
        uint keyA = BinaryPrimitives.ReadUInt32LittleEndian(digest) | 1, keyB = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4)) | 1;
        uint fa = unchecked(keyA * 0x82893933), fb = unchecked(keyB * 0x82893933);
        var terms = Cs64.Select((c, i) => unchecked(c.Coef * (i % 2 == 0 ? fa : fb))).ToList();
        var offsets = Cs64.Select(c => c.Off).ToList();

        var bytes = new byte[1 + data.Length + ((-(1 + data.Length)) % 4 + 4) % 4];
        data.CopyTo(bytes, 1);
        var words = new List<uint>();
        for (int i = 0; i < bytes.Length; i += 4) words.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)));
        if (words.Count % 2 == 1) words.Add(0);
        for (int i = 0; i < words.Count; i++)
        {
            uint wd = words[i];
            uint term = unchecked(wd * (i % 2 == 0 ? keyA : keyB));
            uint off = unchecked(Rol16(wd) * (i % 2 == 0 ? 0xEF0569FBu : 0xC31713DBu));
            if (i == 0) { term = unchecked(term + 0xF2F90B1A * fa); off = unchecked(off + 0x0B1A0000); }
            terms.Add(term); offsets.Add(off);
        }

        uint swapAcc = 0, swapLast = 0, state = 0, acc = 0;
        unchecked
        {
            for (int i = 0; i < terms.Count; i += 2)
            {
                uint x = terms[i], y = terms[i + 1];
                uint t = Rol16(offsets[i] + x);
                uint q = t * 0x76F50F34;
                swapAcc += Rol16(q) * 0xC3EFEA97 + q;
                t = Rol16(offsets[i + 1] + y);
                q = t * 0x1881CE3C;
                swapLast = Rol16(q) * 0x35BD1EC9 + q;
                swapAcc += swapLast;

                state = state * keyA + x;
                state = Rol16(state) * 0xCF98B111;
                state = Rol16(state) * 0x87085B9F;
                state = Rol16(state) * 0x12CEB96D;
                state = Rol16(state) * 0x257E1D83;
                acc += state;
                t = state * keyB + y;
                t = Rol16(t) * 0xA27416F5;
                t = Rol16(t) * 0xD38396FF;
                state = Rol16(Rol16(t) * 0x7C932B89) * 0xBFA49F69;
                acc += state;
            }
        }
        return (swapLast ^ state, swapAcc ^ acc);
    }

    /// <summary>Token for a Unix time (seconds); the service accepts the current 5-minute window.</summary>
    public static string Generate(double unixSeconds, string trustedClientToken)
    {
        double ticks = unixSeconds + 11644473600;
        ticks -= ticks % 300;
        ticks *= 1e9 / 100;
        var data = Encoding.ASCII.GetBytes(Math.Round(ticks, MidpointRounding.ToEven).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + trustedClientToken);
        var digest = Sha(data);
        var (w0, w1) = Cs(data, digest);
        var cs = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(cs, w0);
        BinaryPrimitives.WriteUInt32LittleEndian(cs.AsSpan(4), w1);
        return Convert.ToHexString(cs) + "-" + Convert.ToHexString(digest);
    }
}
