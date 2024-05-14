using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UBM.Exceptions;
using UnityEngine;

namespace UBM {
    public abstract class Server : MonoBehaviour {
        private static Server _instance;

        [Header("General")]
        [SerializeField] private float heartbeatInterval = 5;

        [Header("Security")]
        [SerializeField] private bool ignoreMessageHandlerExceptions = true;
        [SerializeField] private bool kickAfterWrongPassword = true;

        [Header("Performance")]
        [Tooltip("Set to -1 to disable")]
        [SerializeField] private int targetFrameRate = 60;

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private string[] debugCommandLineArgs;
#endif

        private void Awake() {
            if (_instance != null) {
                Logger.LogWarning("Server is already instanced. This should not happen.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private ushort _port;
        private float _lifetime = float.MaxValue;
        private string _password = "";

        private void Start() {
            // process start arguments

#if UNITY_EDITOR
            string[] args = debugCommandLineArgs;
#else
            string[] args = System.Environment.GetCommandLineArgs();
#endif

            for (int i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--server-port":
                        ushort.TryParse(args[++i], out _port);
                        break;
                    case "--server-lifetime":
                        float.TryParse(args[++i], out _lifetime);
                        break;
                    case "--server-password":
                        _password = args[++i];
                        break;
                }
            }

            if (_port <= 0 || _lifetime <= 0) {
                Logger.LogError("Either port or lifetime is <= than 0");
                Quit();
                return;
            }

            if (_password == "") {
                Logger.LogWarning("Password not specified");
            }

            ProcessArguments(args);

            if (!CheckStartArguments()) {
                Logger.LogError("CheckStartArguments() returned false, quiting...");
                Quit();
                return;
            }

            Logger.Log("Setting target frame rate to " + targetFrameRate);

            Application.targetFrameRate = targetFrameRate;

            OnServerReady();
        }

        // TODO: write summary
        protected virtual void ProcessArguments(string[] args) { }
        protected virtual bool CheckStartArguments() => true;
        protected abstract void OnServerReady();

        private bool _started;

        protected void StartServer() {
            if (_started) {
                return;
            }

            _started = true;

            StartCoroutine(ServerRoutine());
        }

        private uint _nextClientId;

        private bool _running = true;

        private IEnumerator ServerRoutine() {
            TcpListener server = new TcpListener(IPAddress.Any, _port);

            // catch global server exceptions
            try {
                Logger.Log("Starting tcp listener (port " + _port + ")...");
                server.Start();

                Logger.Log("Waiting for clients...");
                while (_running) {
                    if ((_lifetime -= Time.unscaledDeltaTime) <= 0) {
                        Logger.Log("Lifetime expired");
                        yield break;
                    }

                    if (server.Pending()) {
                        StartCoroutine(ClientRoutine(_nextClientId++, server.AcceptTcpClient()));
                    }

                    yield return null;
                }
            }
            finally {
                Logger.Log("Stopping tcp listener...");
                server.Stop();

                Logger.Log("Closing client streams...");
                foreach (KeyValuePair<uint, ClientInstance> client in _clients) {
                    Logger.Log("Closing client " + client.Key + "...");

                    // I guess we don't need to call OnClientClosed if we're going to quit anyway

                    // catch close exceptions
                    try {
                        client.Value.Stream.Close();
                        client.Value.TcpClient.Close();
                    }
                    catch (Exception e) {
                        Logger.LogWarning("Error closing client " + client.Key + ": " + e);
                    }
                }

                Quit();
            }
        }

        public void Shutdown() {
            Logger.Log("Shutdown requested");

            _running = false;
        }

        private readonly Dictionary<uint, ClientInstance> _clients = new Dictionary<uint, ClientInstance>();

        private IEnumerator ClientRoutine(uint clientId, TcpClient tcpClient) {
            NetworkStream stream = null;

            // catch global client exception
            try {
                Logger.Log(IPAddress.Parse(((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString()) + " connected; id: " + clientId);

                stream = tcpClient.GetStream();

                ClientInstance clientInstance = new ClientInstance(tcpClient, stream);

                _clients.Add(clientId, clientInstance);

                if (_password == "") {
                    Authenticate(clientId, clientInstance);
                }
                else {
                    Logger.Log("Waiting for " + clientId + " to authenticate...");
                    SendCommandToClient(clientId,
                        new MultiplayerCommand(MultiplayerConstants.AuthStatus, new[] { MultiplayerConstants.AuthStatusRequired }));
                }

                float timeUntilHeartbeat = heartbeatInterval;

                byte[] buffer = new byte[1024];
                StringBuilder messageBuilder = new StringBuilder();

                while (true) {
                    // catch reading exceptions
                    try {
                        if (clientInstance.ShouldBeKicked) {
                            Logger.Log("Client " + clientId + " should be kicked...");
                            break;
                        }

                        if ((timeUntilHeartbeat -= Time.unscaledDeltaTime) <= 0) {
                            Logger.Log("Sending heartbeat to " + clientId + "...");

                            timeUntilHeartbeat = heartbeatInterval;

                            SendCommandToClient(clientId, new MultiplayerCommand(MultiplayerConstants.Heartbeat));
                        }

                        if (stream.DataAvailable) {
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            string messageChunk = MultiplayerConstants.TcpEncoding.GetString(buffer, 0, bytesRead);
                            messageBuilder.Append(messageChunk);

                            if (messageChunk.EndsWith("\n")) {
                                string[] commands = messageBuilder.ToString().TrimEnd().Split('\n');

                                foreach (string command in commands) {
                                    // catch message handler exceptions
                                    try {
                                        MultiplayerCommand multiplayerCommand = MultiplayerCommand.Parse(command);

                                        if (clientInstance.Authenticated) {
                                            switch (multiplayerCommand.Command) {
                                                case MultiplayerConstants.Ping:
                                                    SendCommandToClient(clientId, new MultiplayerCommand(MultiplayerConstants.PingResponse));
                                                    break;
                                                case MultiplayerConstants.Message:
                                                    OnMessageReceived(clientId, multiplayerCommand.Args[0]);
                                                    break;
                                                default:
                                                    throw new InvalidCommandException();
                                            }
                                        }
                                        else if (multiplayerCommand.Command != MultiplayerConstants.Auth) {
                                            throw new ClientNotAuthenticatedException();
                                        }
                                        else {
                                            if (multiplayerCommand.Args[0] == _password) {
                                                Authenticate(clientId, clientInstance);
                                            }
                                            else {
                                                Logger.Log("Client " + clientId + " entered wrong password");
                                                SendCommandToClient(clientId,
                                                    new MultiplayerCommand(MultiplayerConstants.AuthStatus,
                                                        new[] { MultiplayerConstants.AuthStatusError, "wrong password" }));
                                                if (kickAfterWrongPassword) {
                                                    Kick(clientId);
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception e) {
                                        string message = "Command handler threw " + e;

                                        if (ignoreMessageHandlerExceptions) {
                                            Logger.LogWarning(message);
                                        }
                                        else {
                                            Logger.LogError(message);
                                            throw;
                                        }
                                    }
                                }

                                messageBuilder.Clear();
                            }
                        }
                    }
                    catch {
                        break;
                    }

                    yield return null;
                }
            }
            finally {
                Logger.Log("Client " + clientId + " disconnected, closing streams...");

                // catch closing exceptions
                try {
                    stream?.Close();
                    tcpClient.Close();
                }
                catch (Exception e) {
                    Logger.LogWarning("Error closing client " + clientId + ": " + e);
                }

                _clients.Remove(clientId);
                OnClientClosed(clientId);
            }
        }

        private void Authenticate(uint clientId, ClientInstance clientInstance) {
            Logger.Log("Client " + clientId + " authenticated successfully");
            clientInstance.Authenticated = true;
            SendCommandToClient(clientId,
                new MultiplayerCommand(MultiplayerConstants.AuthStatus, new[] { MultiplayerConstants.AuthStatusSuccess, clientId.ToString() }));
            OnClientAuthenticated(clientId);
        }

        protected void Kick(uint clientId) {
            _clients[clientId].ShouldBeKicked = true;
        }

        private void SendCommandToClient(uint clientId, MultiplayerCommand command) {
            byte[] data = MultiplayerConstants.TcpEncoding.GetBytes(command + "\n");
            _clients[clientId].Stream.Write(data, 0, data.Length);
        }

        protected void SendMessageToClient(uint clientId, string message) {
            // catch data send exceptions
            try {
                SendCommandToClient(clientId, new MultiplayerCommand(MultiplayerConstants.Message, new[] { message }));
            }
            catch (Exception e) {
                Logger.LogWarning("Error sending message to " + clientId + ": " + e);
            }
        }

        protected void SendMessageToEveryone(string message) {
            foreach (uint clientId in _clients.Keys) {
                SendMessageToClient(clientId, message);
            }
        }

        protected abstract void OnClientClosed(uint clientId);
        protected abstract void OnClientAuthenticated(uint clientId);
        protected abstract void OnMessageReceived(uint clientId, string message);

        private static bool _quited;

        private static void Quit() {
            if (_quited) {
                return;
            }

            _quited = true;

            Logger.Log("Quitting...");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }


        private class ClientInstance {
            public readonly TcpClient TcpClient;
            public readonly NetworkStream Stream;
            public bool ShouldBeKicked;
            public bool Authenticated;

            public ClientInstance(TcpClient tcpClient, NetworkStream stream) {
                TcpClient = tcpClient;
                Stream = stream;
            }
        }
    }
}