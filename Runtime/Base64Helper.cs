using System.Text;

namespace UBM {
    public static class Base64Helper {
        public static readonly Encoding MessageEncoding = Encoding.Unicode;

        public static string Encode(string text) {
            return System.Convert.ToBase64String(MessageEncoding.GetBytes(text));
        }

        public static string Decode(string base64EncodedData) {
            return MessageEncoding.GetString(System.Convert.FromBase64String(base64EncodedData));
        }
    }
}