using System.Text;

namespace UBM {
    public static class Base64Helper {
        public static string Encode(string text) {
            return System.Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
        }

        public static string Decode(string base64EncodedData) {
            return Encoding.Unicode.GetString(System.Convert.FromBase64String(base64EncodedData));
        }
    }
}