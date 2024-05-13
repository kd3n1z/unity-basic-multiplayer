using System;
using System.Text;

namespace UBM {
    public struct MultiplayerCommand {
        public string Command;
        public string[] Args;

        public MultiplayerCommand(string command) {
            Command = command;
            Args = Array.Empty<string>();
        }

        public MultiplayerCommand(string command, string[] args) {
            Command = command;
            Args = args;
        }

        public static MultiplayerCommand Parse(string input) {
            string[] parts = input.Split(' ');

            string[] args = new string[parts.Length - 1];

            for (int i = 1; i < parts.Length; i++) {
                args[i - 1] = Base64Helper.Decode(parts[i]);
            }

            return new MultiplayerCommand {
                Command = parts[0],
                Args = args
            };
        }

        public override string ToString() {
            StringBuilder result = new StringBuilder();

            result.Append(Command);

            foreach (string argument in Args) {
                result.Append(' ');
                result.Append(Base64Helper.Encode(argument));
            }

            return result.ToString();
        }

        public static class Constants {
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
}