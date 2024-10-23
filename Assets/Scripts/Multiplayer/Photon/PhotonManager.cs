// Copyright 2023 The Open Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if FUSION_WEAVER

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using TiltBrush;

namespace OpenBrush.Multiplayer
{
    public class PhotonManager : IConnectionHandler, INetworkRunnerCallbacks
    {
        private NetworkRunner m_Runner;

        MultiplayerManager m_Manager;

        List<PlayerRef> m_PlayersSpawning;

        PhotonPlayerRig m_LocalPlayer;

        AppSettings m_PhotonAppSettings;

        public event Action Disconnected;

        public PhotonManager(MultiplayerManager manager)
        {
            m_Manager = manager;
            m_PlayersSpawning = new List<PlayerRef>();

            InitializeRunner();

            m_PhotonAppSettings = new AppSettings
            {
                AppIdFusion = App.Config.PhotonFusionSecrets.ClientId,
                // Need this set for some reason
                FixedRegion = "",
            };
        }

        private void InitializeRunner()
        {
            var runnerGO = new GameObject("Photon Network Components");
            m_Runner = runnerGO.AddComponent<NetworkRunner>();
            m_Runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            m_Runner.ProvideInput = true;
            m_Runner.AddCallbacks(this);
            ControllerConsoleScript.m_Instance.AddNewLine("Runner Initialized");
        }

        public async Task<bool> Init()
        {
            await Task.Yield();
            //return true;
            var result = await m_Runner.JoinSessionLobby(SessionLobby.Shared, customAppSettings: m_PhotonAppSettings);

            if (result.Ok)
            {
                ControllerConsoleScript.m_Instance.AddNewLine("Connected to lobby");
            }
            else
            {
                ControllerConsoleScript.m_Instance.AddNewLine("Failed to join lobby!");
            }

            return result.Ok;
        }

        public async Task<bool> Connect(RoomCreateData roomCreateData)
        {
            if (m_Runner == null)
            {
                InitializeRunner();
            }

            var args = new StartGameArgs()
            {
                GameMode = GameMode.Shared,
                SessionName = roomCreateData.roomName,
                CustomPhotonAppSettings = m_PhotonAppSettings,
                PlayerCount = roomCreateData.maxPlayers != 0 ? roomCreateData.maxPlayers : null,
                SceneManager = m_Runner.gameObject.GetComponent<NetworkSceneManagerDefault>(),
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,
            };

            var result = await m_Runner.StartGame(args);

            if (result.Ok)
            {
                ControllerConsoleScript.m_Instance.AddNewLine("Joined Room");
            }
            else
            {
                ControllerConsoleScript.m_Instance.AddNewLine("Failed to join Room!");
            }

            return result.Ok;
            
        }

        public bool IsConnected()
        {
            if(m_Runner == null)
            {
                return false;
            }
            return m_Runner.IsRunning;
        }

        public bool IsInRoom() 
        {
            if (m_Runner == null)
            {
                return false;
            }
            return m_Runner.IsInSession;
        }


        public async Task<bool> Disconnect(bool force)
        {
            if(m_Runner != null)
            {

                if (m_LocalPlayer != null)
                {
                    m_Runner.Despawn(m_LocalPlayer.Object);
                    m_LocalPlayer = null;
                }

                await m_Runner.Shutdown(forceShutdownProcedure: force);
                GameObject.Destroy(m_Runner.gameObject);
                return m_Runner.IsShutdown;
            }
            return true;
        }

        public void Update()
        {
            var copy = m_PlayersSpawning.ToList();
            foreach (var player in copy)
            {
                var newPlayer = m_Runner.GetPlayerObject(player);
                if (newPlayer != null)
                {
                    m_Manager.remotePlayerJoined?.Invoke(player.PlayerId, newPlayer.GetComponent<PhotonPlayerRig>());
                    m_PlayersSpawning.Remove(player);
                }
            }
        }

#region IConnectionHandler Methods
        public async Task<bool> PerformCommand(BaseCommand command)
        {
            await Task.Yield();
            return ProcessCommand(command);
        }

        public async Task<bool> UndoCommand(BaseCommand command)
        {
            PhotonRPC.RPC_Undo(m_Runner, command.GetType().ToString());
            await Task.Yield();
            return true;
        }

        public async Task<bool> RedoCommand(BaseCommand command)
        {
            PhotonRPC.RPC_Redo(m_Runner, command.GetType().ToString());
            await Task.Yield();
            return true;
        }

        public async Task<bool> RpcSyncToSharedAnchor(string uuid)
        {
            PhotonRPC.RPC_SyncToSharedAnchor(m_Runner, uuid);
            await Task.Yield();
            return true;
        }
#endregion

#region Command Methods
        private bool ProcessCommand(BaseCommand command)
        {
            bool success = true;
            switch(command)
            {
                case BrushStrokeCommand:
                    success = CommandBrushStroke(command as BrushStrokeCommand);
                    break;
                case DeleteStrokeCommand:
                    success = CommandDeleteStroke(command as DeleteStrokeCommand);
                    break;
                case BaseCommand:
                    success = CommandBase(command);
                    break;
                default:
                    // Don't know how to process this command
                    success = false;
                    break;
            }

            if(command.ChildrenCount > 0)
            {
                foreach(var child in command.Children)
                {
                    success &= ProcessCommand(child);
                }
            }

            return success;
        }

        private bool CommandBrushStroke(BrushStrokeCommand command)
        {
            var stroke = command.m_Stroke;
            int maxPointsPerChunk = NetworkingConstants.MaxControlPointsPerChunk;


            if (stroke.m_ControlPoints.Length > maxPointsPerChunk)
            {
                // Split and Send
                int numSplits = stroke.m_ControlPoints.Length / maxPointsPerChunk;

                var firstStroke = new Stroke(stroke)
                {
                    m_ControlPoints = stroke.m_ControlPoints.Take(maxPointsPerChunk).ToArray(),
                    m_ControlPointsToDrop = stroke.m_ControlPointsToDrop.Take(maxPointsPerChunk).ToArray()
                };

                var netStroke = new NetworkedStroke().Init(firstStroke);

                var strokeGuid = Guid.NewGuid();

                // First Stroke
                PhotonRPC.RPC_BrushStrokeBegin(m_Runner, strokeGuid, netStroke, stroke.m_ControlPoints.Length);

                // Middle
                for (int rounds = 1; rounds < numSplits + 1; ++rounds)
                {
                    var controlPoints = stroke.m_ControlPoints.Skip(rounds* maxPointsPerChunk).Take(maxPointsPerChunk).ToArray();
                    var dropPoints = stroke.m_ControlPointsToDrop.Skip(rounds* maxPointsPerChunk).Take(maxPointsPerChunk).ToArray();

                    var netControlPoints = new NetworkedControlPoint[controlPoints.Length];

                    for (int point = 0; point < controlPoints.Length; ++ point)
                    {
                        netControlPoints[point] = new NetworkedControlPoint().Init(controlPoints[point]);
                    }

                    PhotonRPC.RPC_BrushStrokeContinue(m_Runner, strokeGuid, rounds * maxPointsPerChunk, netControlPoints, dropPoints);
                }

                // End
                PhotonRPC.RPC_BrushStrokeComplete(m_Runner, strokeGuid, command.Guid, command.ParentGuid, command.ChildrenCount);
            }
            else
            {
                // Can send in one.
                PhotonRPC.RPC_BrushStrokeFull(m_Runner, new NetworkedStroke().Init(command.m_Stroke), command.Guid, command.ParentGuid, command.ChildrenCount);
            }
            return true;
        }

        private bool CommandBase(BaseCommand command)
        {
            PhotonRPC.RPC_BaseCommand(m_Runner, command.Guid, command.ParentGuid, command.ChildrenCount);
            return true;
        }

        private bool CommandDeleteStroke(DeleteStrokeCommand command)
        {
            PhotonRPC.RPC_DeleteStroke(m_Runner, command.m_TargetStroke.m_Seed, command.Guid, command.ParentGuid, command.ChildrenCount);
            return true;
        }
#endregion

#region Photon Callbacks
        public void OnConnectedToServer(NetworkRunner runner)
        {
            var rpc = m_Runner.gameObject.AddComponent<PhotonRPC>();
            m_Runner.AddSimulationBehaviour(rpc);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"OnPlayerJoined called. PlayerRef: {player.PlayerId}");

            try
            {

                if (player == m_Runner.LocalPlayer)
            {
                var playerPrefab = Resources.Load("Multiplayer/Photon/PhotonPlayerRig") as GameObject;
                var playerObj = m_Runner.Spawn(playerPrefab, inputAuthority: m_Runner.LocalPlayer);
                m_LocalPlayer = playerObj.GetComponent<PhotonPlayerRig>();
                m_Runner.SetPlayerObject(m_Runner.LocalPlayer, playerObj);

                m_Manager.localPlayerJoined?.Invoke(player.PlayerId, m_LocalPlayer);
            }
            else
            {
                m_PlayersSpawning.Add(player);
            }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception in OnPlayerJoined: {ex.Message}");
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            m_Manager.playerLeft?.Invoke(player.PlayerId);
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            var roomData = new List<RoomData>();
            foreach (var session in sessionList)
            {
                RoomData data = new RoomData()
                {
                    roomName = session.Name,
                    @private = session.IsOpen,
                    numPlayers = session.PlayerCount,
                    maxPlayers = session.MaxPlayers
                };

                roomData.Add(data);
            }

            m_Manager.roomDataRefreshed?.Invoke(roomData);
        }
#endregion

#region Unused Photon Callbacks 
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) {
            Disconnected?.Invoke();
        }
        public void OnDisconnectedFromServer(NetworkRunner runner) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
#endregion
    }
}

#endif // FUSION_WEAVER
