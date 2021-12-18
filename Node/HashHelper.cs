using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Node
{
    internal class HashHelper
    {
        internal static string GetHash(object obj)
        {
            string json = JsonSerializer.Serialize(obj);
            var data = GetHashAsBytes(json);
            return BitConverter.ToString(data);
        }
        internal static string GetHash(string s)
        {
            var data = GetHashAsBytes(s);
            return BitConverter.ToString(data);
        }
        public static byte[] GetHashAsBytes(object obj)
        {
            string json = JsonSerializer.Serialize(obj);
            var data = GetHashAsBytes(json);
            return data;
        }
        public static byte[] GetHashAsBytes(string input)
        {
            byte[] data = Encoding.Default.GetBytes(input);
            var hash = GetHashAsBytes(data);
            return hash;
        }
        public static byte[] GetHashAsBytes(byte[] input)
        {
            MD5 md5Hasher = MD5.Create();
            byte[] data = md5Hasher.ComputeHash(input);
            return data;
        }


        internal static byte[] MakeSignature(object objToSign, string privateKey)
        {
            var rsaEncrypt = new RSACryptoServiceProvider();
            rsaEncrypt.FromXmlString(privateKey);
            var data = GetHashAsBytes(objToSign);

            byte[] signature = rsaEncrypt.SignData(data, SHA256.Create());
            return signature;
        }

        internal static bool CheckSignature(object objToSign, byte[] Signature, string publicKey)
        {
            var data = GetHashAsBytes(objToSign);

            var rsaDecrypt = new RSACryptoServiceProvider();
            rsaDecrypt.FromXmlString(publicKey);
            bool verified = rsaDecrypt.VerifyData(data, SHA256.Create(), Signature);

            return verified;

        }
    }
}