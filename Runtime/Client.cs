using System;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using UBM.Exceptions;
using UnityEngine;

namespace UBM {
    public abstract class Client : MonoBehaviour {
        [Header("General")]
        [SerializeField] private float pingInterval = 5;

        [Header("Security")]
        [SerializeField] private bool ignoreMessageHandlerExceptions;

        protected double PingMilliseconds { get; private set; }

        private TcpClient _client;
        private NetworkStream _stream;

        protected bool Connect(string hostname, ushort port, string password = "") {
            Logger.Log("Connecting to " + hostname + ":" + port + "...");

            try {
                _client = new TcpClient();

                _client.Connect(hostname, port);

                _stream = _client.GetStream();
            }
            catch (Exception e) {
                Logger.LogError("Error connecting to server: " + e);
                return false;
            }

            Logger.Log("Connected successfully!");
            StartCoroutine(ClientRoutine(password));
            return true;
        }

        private bool _running = true;

        private bool _authenticated;

        private IEnumerator ClientRoutine(string password) {
            try {
                float timeUntilPing = 0;

                bool authenticationRequired = false;

                DateTime lastPingDateTime = DateTime.Now;

                byte[] buffer = new byte[1024];
                StringBuilder messageBuilder = new StringBuilder();

                while (_running) {
                    try {
                        if (_authenticated && (timeUntilPing -= Time.unscaledDeltaTime) <= 0) {
                            timeUntilPing = pingInterval;
                            lastPingDateTime = DateTime.Now;
                            SendCommandToServer(new MultiplayerCommand(MultiplayerConstants.Ping));
                        }

                        if (!_authenticated && authenticationRequired) {
                            Debug.Log("Sending password to server...");
                            SendCommandToServer(new MultiplayerCommand(MultiplayerConstants.Auth, new[] { password }));
                            authenticationRequired = false;
                        }

                        if (_stream.DataAvailable) {
                            int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                            string messageChunk = MultiplayerConstants.TcpEncoding.GetString(buffer, 0, bytesRead);
                            messageBuilder.Append(messageChunk);

                            if (messageChunk.EndsWith("\n")) {
                                string[] commands = messageBuilder.ToString().TrimEnd().Split('\n');

                                foreach (string command in commands) {
                                    // catch message handler exceptions
                                    try {
                                        MultiplayerCommand multiplayerCommand = MultiplayerCommand.Parse(command);

                                        switch (multiplayerCommand.Command) {
                                            case MultiplayerConstants.AuthStatus:
                                                switch (multiplayerCommand.Args[0]) {
                                                    case MultiplayerConstants.AuthStatusRequired:
                                                        Debug.Log("Authentication is required.");
                                                        authenticationRequired = true;
                                                        break;
                                                    case MultiplayerConstants.AuthStatusSuccess:
                                                        if (!_authenticated) {
                                                            _authenticated = true;
                                                            authenticationRequired = false;
                                                            OnAuthenticatedSuccessfully(uint.Parse(multiplayerCommand.Args[1]));
                                                            Debug.Log("Authenticated successfully!");
                                                        }
                                                        else {
                                                            Debug.LogWarning("Client is already authenticated...");
                                                        }

                                                        break;
                                                    case MultiplayerConstants.AuthStatusError:
                                                        _running = false;
                                                        Debug.LogWarning("Authentication error: " + multiplayerCommand.Args[1]);
                                                        break;
                                                }

                                                break;
                                            case MultiplayerConstants.Heartbeat:
                                                Debug.Log("Received heartbeat from server.");
                                                break;
                                            case MultiplayerConstants.PingResponse:
                                                PingMilliseconds = DateTime.Now.Subtract(lastPingDateTime).TotalMilliseconds;

                                                Debug.Log("Received pong. Current ping is " + PingMilliseconds + "ms.");
                                                break;
                                            case MultiplayerConstants.Message:
                                                OnMessageReceived(multiplayerCommand.Args[0]);
                                                break;
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
                Logger.Log("Server closed, closing streams...");

                // catch closing exceptions
                try {
                    _stream?.Close();
                    _client.Close();
                }
                catch (Exception e) {
                    Logger.LogWarning("Error closing streams: " + e);
                }

                OnServerClosed();
            }
        }

        private void SendCommandToServer(MultiplayerCommand command) {
            byte[] data = MultiplayerConstants.TcpEncoding.GetBytes(command + "\n");
            _stream.Write(data, 0, data.Length);
        }

        protected void SendMessageToServer(string message) {
            // catch data send exceptions
            try {
                if (!_authenticated) {
                    throw new ClientNotAuthenticatedException();
                }

                SendCommandToServer(new MultiplayerCommand(MultiplayerConstants.Message, new[] { message }));
            }
            catch (Exception e) {
                Logger.LogWarning("Error sending message to server: " + e);
            }
        }

        protected void Disconnect() {
            Logger.Log("Disconnect requested");

            _running = false;
        }

        protected abstract void OnAuthenticatedSuccessfully(uint clientId);
        protected abstract void OnMessageReceived(string message);
        protected abstract void OnServerClosed();
    }
}