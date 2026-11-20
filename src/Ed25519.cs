// Ed25519 signatures (RFC 8032, pure Ed25519 without context) using BigInteger field arithmetic.
// Not constant-time: BigInteger operations and table lookups may leak timing information.
using System;
using System.Numerics;
using System.Security.Cryptography;

namespace HallowKeys
{
    public static class Ed25519
    {
        private struct Point
        {
            public BigInteger X;
            public BigInteger Y;
            public BigInteger Z;
            public BigInteger T;

            public Point(BigInteger x, BigInteger y, BigInteger z, BigInteger t)
            {
                X = x; Y = y; Z = z; T = t;
            }
        }

        private static readonly BigInteger P;
        private static readonly BigInteger L;
        private static readonly BigInteger D;
        private static readonly BigInteger D2;
        private static readonly BigInteger SqrtM1;
        private static readonly BigInteger PMinus2;
        private static readonly BigInteger PMinus5Div8;
        private static readonly Point Identity;
        private static readonly Point BasePoint;
        // BaseTable[i, j] = j * 16^i * B, for i in 0..63 and j in 0..15.
        private static readonly Point[,] BaseTable;

        static Ed25519()
        {
            P = BigInteger.Pow(2, 255) - 19;
            L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
            PMinus2 = P - 2;
            PMinus5Div8 = (P - 5) / 8;
            D = Mod(-121665 * Inv(121666));
            D2 = Mod(2 * D);
            SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
            Identity = new Point(BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero);

            BigInteger by = Mod(4 * Inv(5));
            BigInteger bx;
            if (!RecoverX(by, 0, out bx))
                throw new InvalidOperationException("Ed25519 base point setup failed.");
            BasePoint = new Point(bx, by, BigInteger.One, Mod(bx * by));

            BaseTable = new Point[64, 16];
            Point row = BasePoint;
            for (int i = 0; i < 64; i++)
            {
                BaseTable[i, 0] = Identity;
                BaseTable[i, 1] = row;
                for (int j = 2; j < 16; j++)
                    BaseTable[i, j] = Add(BaseTable[i, j - 1], row);
                row = Double(Double(Double(Double(row))));
            }
        }

        public static byte[] PublicKey(byte[] seed32)
        {
            if (seed32 == null) throw new ArgumentNullException("seed32");
            if (seed32.Length != 32) throw new ArgumentException("Seed must be 32 bytes.", "seed32");

            byte[] h = Sha512(seed32);
            byte[] scalarBytes = new byte[32];
            try
            {
                Buffer.BlockCopy(h, 0, scalarBytes, 0, 32);
                Clamp(scalarBytes);
                return Encode(ScalarMultBase(scalarBytes));
            }
            finally
            {
                Array.Clear(h, 0, h.Length);
                Array.Clear(scalarBytes, 0, scalarBytes.Length);
            }
        }

        public static byte[] Sign(byte[] seed32, byte[] message)
        {
            if (seed32 == null) throw new ArgumentNullException("seed32");
            if (seed32.Length != 32) throw new ArgumentException("Seed must be 32 bytes.", "seed32");
            if (message == null) throw new ArgumentNullException("message");

            byte[] h = Sha512(seed32);
            byte[] scalarBytes = new byte[32];
            byte[] rHash = null;
            byte[] rBytes = null;
            byte[] sBytes = null;
            try
            {
                Buffer.BlockCopy(h, 0, scalarBytes, 0, 32);
                Clamp(scalarBytes);
                BigInteger a = FromLittleEndian(scalarBytes, 0, 32);
                byte[] publicKey = Encode(ScalarMultBase(scalarBytes));

                // r = SHA512(prefix || M) mod L
                using (SHA512 sha = SHA512.Create())
                {
                    sha.TransformBlock(h, 32, 32, null, 0);
                    sha.TransformFinalBlock(message, 0, message.Length);
                    rHash = sha.Hash;
                }
                BigInteger r = FromLittleEndian(rHash, 0, 64) % L;
                rBytes = ToLittleEndian32(r);
                byte[] R = Encode(ScalarMultBase(rBytes));

                BigInteger k = HashToScalar(R, publicKey, message);
                BigInteger s = (r + k * a) % L;
                sBytes = ToLittleEndian32(s);

                byte[] signature = new byte[64];
                Buffer.BlockCopy(R, 0, signature, 0, 32);
                Buffer.BlockCopy(sBytes, 0, signature, 32, 32);
                return signature;
            }
            finally
            {
                Array.Clear(h, 0, h.Length);
                Array.Clear(scalarBytes, 0, scalarBytes.Length);
                if (rHash != null) Array.Clear(rHash, 0, rHash.Length);
                if (rBytes != null) Array.Clear(rBytes, 0, rBytes.Length);
                if (sBytes != null) Array.Clear(sBytes, 0, sBytes.Length);
            }
        }

        public static bool Verify(byte[] publicKey32, byte[] message, byte[] signature64)
        {
            if (publicKey32 == null || message == null || signature64 == null) return false;
            if (publicKey32.Length != 32 || signature64.Length != 64) return false;
            try
            {
                Point a;
                if (!Decode(publicKey32, out a)) return false;

                BigInteger s = FromLittleEndian(signature64, 32, 32);
                if (s >= L) return false;

                byte[] R = new byte[32];
                Buffer.BlockCopy(signature64, 0, R, 0, 32);
                BigInteger k = HashToScalar(R, publicKey32, message);

                // Check encode([S]B - [k]A) == R (also rejects non-canonical R encodings).
                Point negA = new Point(Neg(a.X), a.Y, a.Z, Neg(a.T));
                Point check = Add(ScalarMultBase(ToLittleEndian32(s)), ScalarMult(ToLittleEndian32(k), negA));
                byte[] encoded = Encode(check);

                int diff = 0;
                for (int i = 0; i < 32; i++) diff |= encoded[i] ^ R[i];
                return diff == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- scalar helpers ----

        private static void Clamp(byte[] s)
        {
            s[0] &= 248;
            s[31] &= 127;
            s[31] |= 64;
        }

        private static BigInteger HashToScalar(byte[] r, byte[] publicKey, byte[] message)
        {
            using (SHA512 sha = SHA512.Create())
            {
                sha.TransformBlock(r, 0, 32, null, 0);
                sha.TransformBlock(publicKey, 0, 32, null, 0);
                sha.TransformFinalBlock(message, 0, message.Length);
                return FromLittleEndian(sha.Hash, 0, 64) % L;
            }
        }

        private static byte[] Sha512(byte[] data)
        {
            using (SHA512 sha = SHA512.Create())
                return sha.ComputeHash(data);
        }

        private static BigInteger FromLittleEndian(byte[] src, int offset, int count)
        {
            byte[] tmp = new byte[count + 1];
            Buffer.BlockCopy(src, offset, tmp, 0, count);
            BigInteger v = new BigInteger(tmp);
            Array.Clear(tmp, 0, tmp.Length);
            return v;
        }

        // Value must be in [0, 2^256).
        private static byte[] ToLittleEndian32(BigInteger v)
        {
            byte[] raw = v.ToByteArray();
            byte[] result = new byte[32];
            Buffer.BlockCopy(raw, 0, result, 0, Math.Min(raw.Length, 32));
            Array.Clear(raw, 0, raw.Length);
            return result;
        }

        // ---- field arithmetic mod p ----

        private static BigInteger Mod(BigInteger v)
        {
            BigInteger r = v % P;
            return r.Sign < 0 ? r + P : r;
        }

        private static BigInteger Neg(BigInteger v)
        {
            return v.IsZero ? v : P - v;
        }

        private static BigInteger Inv(BigInteger v)
        {
            return BigInteger.ModPow(Mod(v), PMinus2, P);
        }

        // ---- point arithmetic (extended twisted Edwards coordinates, a = -1) ----

        private static Point Add(Point p, Point q)
        {
            BigInteger a = ((p.Y - p.X) * (q.Y - q.X)) % P;
            BigInteger b = ((p.Y + p.X) * (q.Y + q.X)) % P;
            BigInteger c = (p.T * D2 % P) * q.T % P;
            BigInteger d = (2 * p.Z * q.Z) % P;
            BigInteger e = b - a;
            BigInteger f = d - c;
            BigInteger g = d + c;
            BigInteger h = b + a;
            return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
        }

        private static Point Double(Point p)
        {
            BigInteger a = p.X * p.X % P;
            BigInteger b = p.Y * p.Y % P;
            BigInteger c = 2 * (p.Z * p.Z % P);
            BigInteger h = a + b;
            BigInteger xy = p.X + p.Y;
            BigInteger e = h - (xy * xy % P);
            BigInteger g = a - b;
            BigInteger f = c + g;
            return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
        }

        // scalar is 32 little-endian bytes.
        private static Point ScalarMultBase(byte[] scalar)
        {
            Point acc = Identity;
            bool first = true;
            for (int i = 0; i < 64; i++)
            {
                int nibble = (scalar[i >> 1] >> ((i & 1) * 4)) & 15;
                if (nibble == 0) continue;
                if (first) { acc = BaseTable[i, nibble]; first = false; }
                else acc = Add(acc, BaseTable[i, nibble]);
            }
            return acc;
        }

        // Variable-base multiplication with a fixed 4-bit window; scalar is 32 little-endian bytes.
        private static Point ScalarMult(byte[] scalar, Point q)
        {
            Point[] table = new Point[16];
            table[0] = Identity;
            table[1] = q;
            for (int j = 2; j < 16; j++)
                table[j] = (j & 1) == 0 ? Double(table[j >> 1]) : Add(table[j - 1], q);

            Point acc = Identity;
            bool started = false;
            for (int i = 63; i >= 0; i--)
            {
                if (started)
                    acc = Double(Double(Double(Double(acc))));
                int nibble = (scalar[i >> 1] >> ((i & 1) * 4)) & 15;
                if (nibble != 0)
                {
                    acc = started ? Add(acc, table[nibble]) : table[nibble];
                    started = true;
                }
            }
            return acc;
        }

        private static byte[] Encode(Point p)
        {
            BigInteger zInv = Inv(p.Z);
            BigInteger x = p.X * zInv % P;
            BigInteger y = p.Y * zInv % P;
            byte[] result = ToLittleEndian32(y);
            if (!x.IsEven) result[31] |= 0x80;
            return result;
        }

        private static bool Decode(byte[] encoded, out Point point)
        {
            point = Identity;
            byte[] tmp = new byte[32];
            Buffer.BlockCopy(encoded, 0, tmp, 0, 32);
            int sign = (tmp[31] >> 7) & 1;
            tmp[31] &= 0x7F;
            BigInteger y = FromLittleEndian(tmp, 0, 32);
            if (y >= P) return false;
            BigInteger x;
            if (!RecoverX(y, sign, out x)) return false;
            point = new Point(x, y, BigInteger.One, x * y % P);
            return true;
        }

        // RFC 8032 section 5.1.3 x recovery.
        private static bool RecoverX(BigInteger y, int sign, out BigInteger x)
        {
            BigInteger yy = y * y % P;
            BigInteger u = Mod(yy - 1);
            BigInteger v = Mod(D * yy + 1);
            BigInteger v3 = v * v % P * v % P;
            BigInteger v7 = v3 * v3 % P * v % P;
            x = u * v3 % P * BigInteger.ModPow(u * v7 % P, PMinus5Div8, P) % P;
            BigInteger vxx = v * (x * x % P) % P;
            if (vxx != u)
            {
                if (vxx == Neg(u)) x = x * SqrtM1 % P;
                else return false;
            }
            if (x.IsZero && sign == 1) return false;
            if ((int)(x % 2) != sign) x = Neg(x);
            return true;
        }
    }
}
