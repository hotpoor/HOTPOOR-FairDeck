using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FairDeck {
    [Serializable] public class WalletFile {
        public int version = 1;
        public string algorithm = "RSA3072-PKCS1-SHA256";
        public string kdf = "PBKDF2-HMAC-SHA256";
        public int iterations = 600000;
        public string address, modulus, exponent, salt, iv, ciphertext, mac;
    }

    public static class LocalWallet {
        public static string DirectoryPath {
            get {
                // Editor: Unity project/wallet. Windows build: beside the game executable.
                return Path.Combine(Path.GetDirectoryName(Application.dataPath), "wallet");
            }
        }
        static byte[] RandomBytes(int count) {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }
        static string B64(byte[] bytes) { return Convert.ToBase64String(bytes); }
        public static string Hash(byte[] bytes) {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string Address(string modulus, string exponent) {
            return "fd1_" + Hash(Encoding.ASCII.GetBytes("FairDeck-RSA3072-v1|" + modulus + "|" + exponent));
        }
        static byte[] Derive(string password, WalletFile wallet) {
            if (wallet.version != 1 || wallet.algorithm != "RSA3072-PKCS1-SHA256" ||
                wallet.kdf != "PBKDF2-HMAC-SHA256" || wallet.iterations != 600000)
                throw new CryptographicException("不支持的钱包格式");
            using (var kdf = new Rfc2898DeriveBytes(password, Convert.FromBase64String(wallet.salt), wallet.iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(64);
        }
        static byte[] Part(byte[] key, int offset) { var result = new byte[32]; Buffer.BlockCopy(key, offset, result, 0, 32); return result; }
        static byte[] AuthBytes(WalletFile w) {
            return Encoding.UTF8.GetBytes(w.version + "|" + w.algorithm + "|" + w.kdf + "|" + w.iterations + "|" +
                w.address + "|" + w.modulus + "|" + w.exponent + "|" + w.salt + "|" + w.iv + "|" + w.ciphertext);
        }
        static byte[] Mac(WalletFile w, byte[] keys) {
            var key = Part(keys, 32);
            try { using (var h = new HMACSHA256(key)) return h.ComputeHash(AuthBytes(w)); }
            finally { Array.Clear(key, 0, key.Length); }
        }
        static bool Equal(byte[] a, byte[] b) {
            if (a.Length != b.Length) return false;
            int diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0;
        }
        public static WalletFile Create(string directory, string password) {
            if (password == null || password.Length < 12 || password.Length > 256) throw new ArgumentException("密码需要 12–256 个字符");
            Directory.CreateDirectory(directory);
            using (var rsa = new RSACryptoServiceProvider(3072)) {
                rsa.PersistKeyInCsp = false;
                var p = rsa.ExportParameters(false);
                var wallet = new WalletFile { modulus = B64(p.Modulus), exponent = B64(p.Exponent), salt = B64(RandomBytes(32)), iv = B64(RandomBytes(16)) };
                wallet.address = Address(wallet.modulus, wallet.exponent);
                var keys = Derive(password, wallet);
                byte[] plaintext = Encoding.UTF8.GetBytes(rsa.ToXmlString(true));
                try {
                    using (var aes = Aes.Create()) {
                        aes.Key = Part(keys, 0); aes.IV = Convert.FromBase64String(wallet.iv);
                        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                        using (var enc = aes.CreateEncryptor()) wallet.ciphertext = B64(enc.TransformFinalBlock(plaintext, 0, plaintext.Length));
                    }
                    wallet.mac = B64(Mac(wallet, keys));
                    var target = Path.Combine(directory, wallet.address + ".json");
                    using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(wallet, true)); file.Write(bytes, 0, bytes.Length); file.Flush(true);
                    }
                    return wallet;
                } finally { Array.Clear(keys, 0, keys.Length); Array.Clear(plaintext, 0, plaintext.Length); }
            }
        }
        public static WalletFile Read(string file) {
            if (new FileInfo(file).Length > 32768) throw new IOException("钱包文件过大");
            var w = JsonUtility.FromJson<WalletFile>(File.ReadAllText(file));
            if (w == null || Address(w.modulus, w.exponent) != w.address) throw new CryptographicException("钱包地址不匹配");
            return w;
        }
        public static string Sign(WalletFile wallet, string password, string message) {
            var keys = Derive(password, wallet);
            byte[] plaintext = null;
            try {
                if (!Equal(Mac(wallet, keys), Convert.FromBase64String(wallet.mac))) throw new CryptographicException("密码错误或钱包已被修改");
                using (var aes = Aes.Create()) {
                    aes.Key = Part(keys, 0); aes.IV = Convert.FromBase64String(wallet.iv);
                    aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var dec = aes.CreateDecryptor()) { var raw = Convert.FromBase64String(wallet.ciphertext); plaintext = dec.TransformFinalBlock(raw, 0, raw.Length); }
                }
                using (var rsa = new RSACryptoServiceProvider()) {
                    rsa.PersistKeyInCsp = false; rsa.FromXmlString(Encoding.UTF8.GetString(plaintext));
                    var p = rsa.ExportParameters(false);
                    if (Address(B64(p.Modulus), B64(p.Exponent)) != wallet.address) throw new CryptographicException("密钥不匹配");
                    return B64(rsa.SignData(Encoding.UTF8.GetBytes(message), CryptoConfig.MapNameToOID("SHA256")));
                }
            } finally { Array.Clear(keys, 0, keys.Length); if (plaintext != null) Array.Clear(plaintext, 0, plaintext.Length); }
        }
    }
}
