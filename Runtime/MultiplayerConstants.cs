using System.Text;

namespace UBM {
    public static class MultiplayerConstants {
        public static readonly Encoding TcpEncoding = Encoding.ASCII;

        #region Server

        public const string Heartbeat = "heartbeat";
        public const string PingResponse = "pong";
        public const string AuthStatus = "authstatus";
        public const string AuthStatusRequired = "required";
        public const string AuthStatusError = "error";
        public const string AuthStatusSuccess = "success";

        #endregion

        #region Client

        public const string Ping = "ping";
        public const string Auth = "auth";

        #endregion

        #region Uni-class =)

        public const string Message = "msg";

        #endregion
    }
}