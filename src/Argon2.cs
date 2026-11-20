// Hallow Keys 2.0 - Argon2id (RFC 9106), BLAKE2b (RFC 7693) and HKDF-SHA256 (RFC 5869).
// Windows has no Argon2, so it's written out here, following the RFC and the reference implementation line by line, and checked
// against the RFC 9106 test vectors (Argon2d, Argon2i and Argon2id) and the reference implementation's own test vectors.
// HKDF uses Windows' HMAC-SHA256. The lanes of each Argon2 slice run on their own threads (as the RFC allows).
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace HallowKeys
{
    public static class Blake2b
    {
        static readonly ulong[] IV = { 0x6a09e667f3bcc908UL, 0xbb67ae8584caa73bUL, 0x3c6ef372fe94f82bUL, 0xa54ff53a5f1d36f1UL, 0x510e527fade682d1UL, 0x9b05688c2b3e6c1fUL, 0x1f83d9abfb41bd6bUL, 0x5be0cd19137e2179UL };
        static readonly byte[,] Sigma = {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
            { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 }, { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
            { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 }, { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
            { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 }, { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
            { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 }, { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 } };

        // an incremental BLAKE2b (no key) with an output of 1..64 bytes
        public class Hasher
        {
            readonly ulong[] h = new ulong[8]; readonly byte[] buf = new byte[128]; int bufLen; ulong t0, t1; readonly int outLen;
            public Hasher(int outLen)
            {
                if (outLen < 1 || outLen > 64) throw new ArgumentOutOfRangeException("outLen");
                this.outLen = outLen; Array.Copy(IV, h, 8); h[0] ^= 0x01010000UL ^ (ulong)outLen;
            }
            public Hasher Update(byte[] data) { return Update(data, 0, data.Length); }
            public Hasher Update(byte[] data, int off, int len)
            {
                while (len > 0)
                {
                    if (bufLen == 128) { Inc(128); Compress(buf, 0, false); bufLen = 0; }
                    int n = Math.Min(128 - bufLen, len);
                    Buffer.BlockCopy(data, off, buf, bufLen, n); bufLen += n; off += n; len -= n;
                }
                return this;
            }
            public Hasher Update(uint le32) { return Update(BitConverter.GetBytes(le32)); }
            void Inc(int n) { t0 += (ulong)n; if (t0 < (ulong)n) t1++; }
            public byte[] Final()
            {
                Inc(bufLen); for (int i = bufLen; i < 128; i++) buf[i] = 0;
                Compress(buf, 0, true);
                var full = new byte[64]; for (int i = 0; i < 8; i++) Buffer.BlockCopy(BitConverter.GetBytes(h[i]), 0, full, i * 8, 8);
                var o = new byte[outLen]; Buffer.BlockCopy(full, 0, o, 0, outLen); Array.Clear(full, 0, 64); Array.Clear(buf, 0, 128);
                return o;
            }
            void Compress(byte[] block, int off, bool last)
            {
                var m = new ulong[16]; for (int i = 0; i < 16; i++) m[i] = BitConverter.ToUInt64(block, off + i * 8);
                var v = new ulong[16];
                for (int i = 0; i < 8; i++) { v[i] = h[i]; v[i + 8] = IV[i]; }
                v[12] ^= t0; v[13] ^= t1; if (last) v[14] = ~v[14];
                for (int r = 0; r < 12; r++)
                {
                    int s = r % 10;
                    G(v, 0, 4, 8, 12, m[Sigma[s, 0]], m[Sigma[s, 1]]); G(v, 1, 5, 9, 13, m[Sigma[s, 2]], m[Sigma[s, 3]]);
                    G(v, 2, 6, 10, 14, m[Sigma[s, 4]], m[Sigma[s, 5]]); G(v, 3, 7, 11, 15, m[Sigma[s, 6]], m[Sigma[s, 7]]);
                    G(v, 0, 5, 10, 15, m[Sigma[s, 8]], m[Sigma[s, 9]]); G(v, 1, 6, 11, 12, m[Sigma[s, 10]], m[Sigma[s, 11]]);
                    G(v, 2, 7, 8, 13, m[Sigma[s, 12]], m[Sigma[s, 13]]); G(v, 3, 4, 9, 14, m[Sigma[s, 14]], m[Sigma[s, 15]]);
                }
                for (int i = 0; i < 8; i++) h[i] ^= v[i] ^ v[i + 8];
                Array.Clear(m, 0, 16); Array.Clear(v, 0, 16);
            }
            static void G(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
            {
                v[a] = v[a] + v[b] + x; v[d] = Rotr(v[d] ^ v[a], 32);
                v[c] = v[c] + v[d]; v[b] = Rotr(v[b] ^ v[c], 24);
                v[a] = v[a] + v[b] + y; v[d] = Rotr(v[d] ^ v[a], 16);
                v[c] = v[c] + v[d]; v[b] = Rotr(v[b] ^ v[c], 63);
            }
        }
        static ulong Rotr(ulong x, int n) { return (x >> n) | (x << (64 - n)); }
        public static byte[] Hash(byte[] data, int outLen) { return new Hasher(outLen).Update(data).Final(); }
    }

    public static class Argon2
    {
        public const int TypeD = 0, TypeI = 1, TypeId = 2;
        const int BlockWords = 128, AddressesInBlock = 128, SyncPoints = 4;

        // Argon2id with no secret and no associated data - what Hallow Keys uses
        public static byte[] Id(byte[] password, byte[] salt, int memoryKiB, int iterations, int parallelism, int tagLength)
        {
            return Hash(TypeId, password, salt, null, null, memoryKiB, iterations, parallelism, tagLength);
        }

        // the full function (RFC 9106 section 3), version 0x13
        public static byte[] Hash(int type, byte[] password, byte[] salt, byte[] secret, byte[] ad, int memoryKiB, int iterations, int parallelism, int tagLength)
        {
            password = password ?? new byte[0]; salt = salt ?? new byte[0]; secret = secret ?? new byte[0]; ad = ad ?? new byte[0];
            if (parallelism < 1 || parallelism > 0xFFFFFF) throw new ArgumentOutOfRangeException("parallelism");
            if (iterations < 1) throw new ArgumentOutOfRangeException("iterations");
            if (tagLength < 4) throw new ArgumentOutOfRangeException("tagLength");
            if (memoryKiB < 8 * parallelism) throw new ArgumentOutOfRangeException("memoryKiB");
            if (salt.Length < 8) throw new ArgumentException("salt");
            // H0
            var h0h = new Blake2b.Hasher(64);
            h0h.Update((uint)parallelism).Update((uint)tagLength).Update((uint)memoryKiB).Update((uint)iterations).Update(0x13u).Update((uint)type);
            h0h.Update((uint)password.Length).Update(password).Update((uint)salt.Length).Update(salt).Update((uint)secret.Length).Update(secret).Update((uint)ad.Length).Update(ad);
            var h0 = h0h.Final();
            int segment = memoryKiB / (parallelism * SyncPoints); int blocks = segment * parallelism * SyncPoints; int laneLen = segment * SyncPoints;
            var mem = new ulong[(long)blocks * BlockWords];
            try
            {
                // the first two blocks of each lane
                var seed = new byte[72]; Buffer.BlockCopy(h0, 0, seed, 0, 64);
                for (int l = 0; l < parallelism; l++)
                    for (int c = 0; c < 2; c++)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes((uint)c), 0, seed, 64, 4); Buffer.BlockCopy(BitConverter.GetBytes((uint)l), 0, seed, 68, 4);
                        var b = HPrime(seed, 1024); long o = ((long)l * laneLen + c) * BlockWords;
                        for (int i = 0; i < BlockWords; i++) mem[o + i] = BitConverter.ToUInt64(b, i * 8);
                        Array.Clear(b, 0, b.Length);
                    }
                Array.Clear(seed, 0, seed.Length);
                for (int pass = 0; pass < iterations; pass++)
                    for (int slice = 0; slice < SyncPoints; slice++)
                    {
                        int ps = pass, sl = slice;
                        if (parallelism == 1) FillSegment(mem, type, ps, 0, sl, parallelism, laneLen, segment, blocks, iterations);
                        else Parallel.For(0, parallelism, lane => FillSegment(mem, type, ps, lane, sl, parallelism, laneLen, segment, blocks, iterations));
                    }
                // final block: XOR of each lane's last block
                var fin = new ulong[BlockWords];
                for (int l = 0; l < parallelism; l++) { long o = ((long)l * laneLen + laneLen - 1) * BlockWords; for (int i = 0; i < BlockWords; i++) fin[i] ^= mem[o + i]; }
                var fb = new byte[1024]; for (int i = 0; i < BlockWords; i++) Buffer.BlockCopy(BitConverter.GetBytes(fin[i]), 0, fb, i * 8, 8);
                var tag = HPrime(fb, tagLength); Array.Clear(fb, 0, fb.Length); Array.Clear(fin, 0, fin.Length);
                return tag;
            }
            finally { Array.Clear(mem, 0, mem.Length); Array.Clear(h0, 0, h0.Length); }
        }

        // H' - the variable-length hash (RFC 9106 section 3.3)
        static byte[] HPrime(byte[] x, int t)
        {
            var lenPrefix = BitConverter.GetBytes((uint)t);
            if (t <= 64) return new Blake2b.Hasher(t).Update(lenPrefix).Update(x).Final();
            var o = new byte[t]; int r = (t + 31) / 32 - 2;
            var v = new Blake2b.Hasher(64).Update(lenPrefix).Update(x).Final();
            Buffer.BlockCopy(v, 0, o, 0, 32); int pos = 32;
            for (int i = 2; i <= r; i++) { v = Blake2b.Hash(v, 64); Buffer.BlockCopy(v, 0, o, pos, 32); pos += 32; }
            var last = Blake2b.Hash(v, t - 32 * r); Buffer.BlockCopy(last, 0, o, pos, last.Length);
            Array.Clear(v, 0, v.Length);
            return o;
        }

        static void FillSegment(ulong[] mem, int type, int pass, int lane, int slice, int lanes, int laneLen, int segment, int blocks, int passes)
        {
            bool dataIndependent = type == TypeI || (type == TypeId && pass == 0 && slice < SyncPoints / 2);
            ulong[] address = null, input = null, zero = null;
            if (dataIndependent)
            {
                address = new ulong[BlockWords]; input = new ulong[BlockWords]; zero = new ulong[BlockWords];
                input[0] = (ulong)pass; input[1] = (ulong)lane; input[2] = (ulong)slice; input[3] = (ulong)blocks; input[4] = (ulong)passes; input[5] = (ulong)type;
            }
            int start = 0;
            if (pass == 0 && slice == 0) { start = 2; if (dataIndependent) NextAddresses(address, input, zero); }
            long curr = (long)lane * laneLen + slice * segment + start;
            long prev = curr % laneLen == 0 ? curr + laneLen - 1 : curr - 1;
            var tmpR = new ulong[BlockWords]; var tmpT = new ulong[BlockWords];
            for (int i = start; i < segment; i++, curr++, prev++)
            {
                if (curr % laneLen == 1) prev = curr - 1;
                ulong rnd;
                if (dataIndependent) { if (i % AddressesInBlock == 0) NextAddresses(address, input, zero); rnd = address[i % AddressesInBlock]; }
                else rnd = mem[prev * BlockWords];
                long refLane = (long)((rnd >> 32) % (ulong)lanes);
                if (pass == 0 && slice == 0) refLane = lane;
                long refIndex = IndexAlpha(pass, slice, i, (uint)(rnd & 0xFFFFFFFF), refLane == lane, laneLen, segment);
                long refBlock = laneLen * refLane + refIndex;
                FillBlock(mem, prev * BlockWords, mem, refBlock * BlockWords, mem, curr * BlockWords, pass != 0, tmpR, tmpT);
            }
        }
        static long IndexAlpha(int pass, int slice, int index, uint pseudoRand, bool sameLane, int laneLen, int segment)
        {
            long area;
            if (pass == 0)
            {
                if (slice == 0) area = index - 1;
                else if (sameLane) area = (long)slice * segment + index - 1;
                else area = (long)slice * segment + (index == 0 ? -1 : 0);
            }
            else
            {
                if (sameLane) area = laneLen - segment + index - 1;
                else area = laneLen - segment + (index == 0 ? -1 : 0);
            }
            ulong rel = pseudoRand; rel = (rel * rel) >> 32; rel = (ulong)area - 1 - (((ulong)area * rel) >> 32);
            long startPos = 0;
            if (pass != 0) startPos = slice == SyncPoints - 1 ? 0 : (long)(slice + 1) * segment;
            return (long)(((ulong)startPos + rel) % (ulong)laneLen);
        }
        static void NextAddresses(ulong[] address, ulong[] input, ulong[] zero)
        {
            input[6]++;
            var r = new ulong[BlockWords]; var t = new ulong[BlockWords];
            FillBlock(zero, 0, input, 0, address, 0, false, r, t);
            FillBlock(zero, 0, address, 0, address, 0, false, r, t);
        }
        // the compression function G (RFC 9106 section 3.5); withXor = version 1.3's XOR into the block being overwritten
        static void FillBlock(ulong[] xa, long xo, ulong[] ya, long yo, ulong[] na, long no, bool withXor, ulong[] r, ulong[] t)
        {
            for (int i = 0; i < BlockWords; i++) { r[i] = xa[xo + i] ^ ya[yo + i]; t[i] = r[i]; }
            if (withXor) for (int i = 0; i < BlockWords; i++) t[i] ^= na[no + i];
            for (int i = 0; i < 8; i++)
                Round(r, 16 * i, 16 * i + 1, 16 * i + 2, 16 * i + 3, 16 * i + 4, 16 * i + 5, 16 * i + 6, 16 * i + 7, 16 * i + 8, 16 * i + 9, 16 * i + 10, 16 * i + 11, 16 * i + 12, 16 * i + 13, 16 * i + 14, 16 * i + 15);
            for (int i = 0; i < 8; i++)
                Round(r, 2 * i, 2 * i + 1, 2 * i + 16, 2 * i + 17, 2 * i + 32, 2 * i + 33, 2 * i + 48, 2 * i + 49, 2 * i + 64, 2 * i + 65, 2 * i + 80, 2 * i + 81, 2 * i + 96, 2 * i + 97, 2 * i + 112, 2 * i + 113);
            for (int i = 0; i < BlockWords; i++) na[no + i] = t[i] ^ r[i];
        }
        static void Round(ulong[] v, int v0, int v1, int v2, int v3, int v4, int v5, int v6, int v7, int v8, int v9, int v10, int v11, int v12, int v13, int v14, int v15)
        {
            GB(v, v0, v4, v8, v12); GB(v, v1, v5, v9, v13); GB(v, v2, v6, v10, v14); GB(v, v3, v7, v11, v15);
            GB(v, v0, v5, v10, v15); GB(v, v1, v6, v11, v12); GB(v, v2, v7, v8, v13); GB(v, v3, v4, v9, v14);
        }
        static void GB(ulong[] v, int a, int b, int c, int d)
        {
            ulong va = v[a], vb = v[b], vc = v[c], vd = v[d];
            va = va + vb + 2 * (va & 0xFFFFFFFFUL) * (vb & 0xFFFFFFFFUL); vd = Rotr(vd ^ va, 32);
            vc = vc + vd + 2 * (vc & 0xFFFFFFFFUL) * (vd & 0xFFFFFFFFUL); vb = Rotr(vb ^ vc, 24);
            va = va + vb + 2 * (va & 0xFFFFFFFFUL) * (vb & 0xFFFFFFFFUL); vd = Rotr(vd ^ va, 16);
            vc = vc + vd + 2 * (vc & 0xFFFFFFFFUL) * (vd & 0xFFFFFFFFUL); vb = Rotr(vb ^ vc, 63);
            v[a] = va; v[b] = vb; v[c] = vc; v[d] = vd;
        }
        static ulong Rotr(ulong x, int n) { return (x >> n) | (x << (64 - n)); }
    }

    // HKDF with HMAC-SHA256 (RFC 5869)
    public static class Hkdf
    {
        public static byte[] Extract(byte[] salt, byte[] ikm) { using (var h = new HMACSHA256(salt == null || salt.Length == 0 ? new byte[32] : salt)) return h.ComputeHash(ikm); }
        public static byte[] Expand(byte[] prk, byte[] info, int length)
        {
            if (length < 1 || length > 255 * 32) throw new ArgumentOutOfRangeException("length");
            info = info ?? new byte[0];
            var o = new byte[length]; var t = new byte[0]; int pos = 0;
            using (var h = new HMACSHA256(prk))
                for (byte i = 1; pos < length; i++)
                {
                    var input = new byte[t.Length + info.Length + 1];
                    Buffer.BlockCopy(t, 0, input, 0, t.Length); Buffer.BlockCopy(info, 0, input, t.Length, info.Length); input[input.Length - 1] = i;
                    t = h.ComputeHash(input); int n = Math.Min(32, length - pos); Buffer.BlockCopy(t, 0, o, pos, n); pos += n;
                }
            return o;
        }
        public static byte[] Derive(byte[] ikm, byte[] salt, byte[] info, int length) { var prk = Extract(salt, ikm); try { return Expand(prk, info, length); } finally { Array.Clear(prk, 0, prk.Length); } }
    }
}
