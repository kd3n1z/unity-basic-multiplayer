using UnityEngine;

namespace UBM {
    public static class Logger {
        public static void Log(string text) {
            Debug.Log("[UBM-INFO] " + text);
        }

        public static void LogWarning(string text) {
            Debug.LogWarning("[UBM-WARN] " + text);
        }

        public static void LogError(string text) {
            Debug.LogError("[UBM-ERR] " + text);
        }
    }
}