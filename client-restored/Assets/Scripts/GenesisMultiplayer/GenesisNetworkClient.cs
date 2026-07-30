using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    public sealed class GenesisNetworkClient : MonoBehaviour
    {
        private static string sessionPlayerName;

        [Header("连接")]
        [SerializeField] private string serverUrl = "";
        [SerializeField] private string room = "public";
        [SerializeField] private string playerName = "新兵";

        [Header("原程序对象")]
        [SerializeField] private Transform localPlayer;
        [SerializeField] private Transform viewCamera;
        [SerializeField] private GameObject remotePlayerPrefab;

        [Header("同步")]
        [SerializeField, Range(5, 30)] private int inputRate = 20;
        [SerializeField] private float reconciliationSpeed = 12f;
        [SerializeField] private float remoteInterpolationSpeed = 15f;

        private readonly Dictionary<string, RemotePlayerView> remotes =
            new Dictionary<string, RemotePlayerView>();
        private readonly Dictionary<string, string> playerNames =
            new Dictionary<string, string>();
        private int socketId;
        private int sequence;
        private float nextInputAt;
        private float nextRosterAt;
        private float reconnectAt;
        private string localPlayerId;
        private bool connected;
        private bool destroying;
        private Vector3 authoritativePosition;
        private bool hasAuthoritativePosition;
        private Vector3 worldOrigin;
        private long localRespawnAt;
        private float localNetworkY;
        private CharacterController localController;
        private bool hasLocalAliveState;
        private bool localWasAlive;

        public event Action<bool> ConnectionChanged;
        public event Action<GenesisLocalState> LocalStateChanged;
        public event Action<bool> HitConfirmed;
        public event Action<GenesisPlayerState[]> RosterChanged;
        public event Action<string, string> PlayerKilled;

        public bool IsConnected
        {
            get { return connected && !string.IsNullOrEmpty(localPlayerId); }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int GenesisSocketConnect(string url, string gameObjectName);

        [DllImport("__Internal")]
        private static extern void GenesisSocketSend(int id, string message);

        [DllImport("__Internal")]
        private static extern void GenesisSocketClose(int id);
#endif

        private void Awake()
        {
            if (localPlayer == null)
            {
                localPlayer = transform;
            }
            localController = localPlayer.GetComponent<CharacterController>();

            if (viewCamera == null && Camera.main != null)
            {
                viewCamera = Camera.main.transform;
            }

            if (playerName == "新兵")
            {
                if (string.IsNullOrEmpty(sessionPlayerName))
                    sessionPlayerName =
                        "PLAYER" + UnityEngine.Random.Range(1000, 10000);
                playerName = sessionPlayerName;
            }
        }

        public void Configure(
            Transform player,
            Transform cameraTransform,
            GameObject opponentPrefab,
            string roomId)
        {
            localPlayer = player;
            viewCamera = cameraTransform;
            remotePlayerPrefab = opponentPrefab;
            room = string.IsNullOrWhiteSpace(roomId) ? "public" : roomId;
            worldOrigin = player == null ? Vector3.zero : player.position;
            localController = player == null
                ? null
                : player.GetComponent<CharacterController>();
        }

        private void Start()
        {
            Connect();
        }

        private void Update()
        {
            if (!connected || string.IsNullOrEmpty(localPlayerId))
            {
                if (!destroying
                    && reconnectAt > 0f
                    && Time.unscaledTime >= reconnectAt)
                {
                    reconnectAt = 0f;
                    Connect();
                }
                return;
            }

            if (Time.unscaledTime >= nextInputAt)
            {
                nextInputAt = Time.unscaledTime + 1f / inputRate;
                SendInput();
            }

            if (hasAuthoritativePosition && localPlayer != null)
            {
                // The recovered CharacterController owns gravity, grounding and
                // jumping. Reapplying the server's simulated Y position here
                // makes both systems fight every frame and causes vertical jitter.
                Vector3 horizontalAuthority = authoritativePosition;
                horizontalAuthority.y = localPlayer.position.y;
                Vector3 correction = horizontalAuthority - localPlayer.position;
                correction.y = 0f;
                float factor =
                    1f - Mathf.Exp(-reconciliationSpeed * Time.deltaTime);
                Vector3 collisionAwareStep = correction * factor;
                collisionAwareStep = Vector3.ClampMagnitude(
                    collisionAwareStep, 0.35f);
                if (localController != null && localController.enabled)
                {
                    // CharacterController.Move performs a swept collision test.
                    // Directly assigning Transform.position bypasses walls and was
                    // the cause of players being pulled through recovered geometry.
                    localController.Move(collisionAwareStep);
                }
                else
                {
                    localPlayer.position += collisionAwareStep;
                }
            }

            if (localPlayer != null && hasAuthoritativePosition)
            {
                // Archived scenes spawn the controller slightly above the map
                // and let gravity settle it. Derive the origin from the actual
                // local root and its server-relative height; the archived Ground
                // layers are incomplete, so CharacterController.isGrounded is
                // not a reliable calibration gate in every recovered scene.
                worldOrigin.y = Mathf.MoveTowards(
                    worldOrigin.y,
                    localPlayer.position.y - localNetworkY,
                    Time.deltaTime * 6f);
            }

            foreach (RemotePlayerView remote in remotes.Values)
            {
                remote.Update(remoteInterpolationSpeed, Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            destroying = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (socketId != 0)
            {
                GenesisSocketClose(socketId);
            }
#endif
        }

        public void Connect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            socketId = GenesisSocketConnect(serverUrl, gameObject.name);
#else
            Debug.Log(
                "GenesisNetworkClient: WebSocket 浏览器桥接仅在 WebGL 构建中启用。");
#endif
        }

        public void OnSocketOpen(string id)
        {
            int.TryParse(id, out socketId);
            connected = true;
            reconnectAt = 0f;
            JoinMessage join = new JoinMessage
            {
                type = "join",
                name = string.IsNullOrWhiteSpace(playerName)
                    ? "PLAYER"
                    : playerName.Trim(),
                room = room,
            };
            Send(JsonUtility.ToJson(join));
        }

        public void OnSocketMessage(string json)
        {
            MessageType envelope = JsonUtility.FromJson<MessageType>(json);
            if (envelope == null)
            {
                return;
            }

            if (envelope.type == "welcome")
            {
                WelcomeMessage welcome = JsonUtility.FromJson<WelcomeMessage>(json);
                localPlayerId = welcome.id;
                if (ConnectionChanged != null)
                    ConnectionChanged(true);
                return;
            }

            if (envelope.type == "snapshot")
            {
                ApplySnapshot(JsonUtility.FromJson<SnapshotMessage>(json));
                return;
            }

            if (envelope.type == "hit")
            {
                HitMessage hit = JsonUtility.FromJson<HitMessage>(json);
                if (HitConfirmed != null)
                    HitConfirmed(hit.shooterId == localPlayerId);
                return;
            }

            if (envelope.type == "death")
            {
                DeathMessage death = JsonUtility.FromJson<DeathMessage>(json);
                if (death.victimId == localPlayerId)
                    localRespawnAt = death.respawnAt;
                string killerName = ResolvePlayerName(death.killerId);
                string victimName = ResolvePlayerName(death.victimId);
                if (PlayerKilled != null)
                    PlayerKilled(killerName, victimName);
                Debug.Log("击杀：" + killerName + " -> " + victimName);
            }
        }

        public void OnSocketClose(string reason)
        {
            connected = false;
            localPlayerId = null;
            socketId = 0;
            hasAuthoritativePosition = false;
            ClearRemotePlayers();
            if (!destroying)
                reconnectAt = Time.unscaledTime + 2f;
            if (ConnectionChanged != null)
                ConnectionChanged(false);
            Debug.LogWarning(
                "对战服务器连接已关闭：" + reason + "，2 秒后自动重连。");
        }

        public void OnSocketError(string reason)
        {
            Debug.LogError("对战服务器连接错误：" + reason);
        }

        private void SendInput()
        {
            float yaw = localPlayer != null ? localPlayer.eulerAngles.y : 0f;
            float pitch = viewCamera != null
                ? NormalizePitch(viewCamera.eulerAngles.x)
                : 0f;
            InputMessage input = new InputMessage
            {
                type = "input",
                sequence = ++sequence,
                moveX = Input.GetAxisRaw("Horizontal"),
                moveZ = Input.GetAxisRaw("Vertical"),
                jump = Input.GetKey(KeyCode.Space),
                yaw = yaw,
                pitch = pitch,
                position = SerializableVector3.From(
                    localPlayer == null
                        ? Vector3.zero
                        : localPlayer.position - worldOrigin),
            };
            Send(JsonUtility.ToJson(input));
        }

        public bool RequestShoot(string weapon)
        {
            if (!IsConnected || viewCamera == null)
                return false;
            ShootMessage shoot = new ShootMessage
            {
                type = "shoot",
                sequence = ++sequence,
                weapon = weapon == "knife" ? "knife" : "pistol",
                direction = SerializableVector3.From(viewCamera.forward.normalized),
            };
            Send(JsonUtility.ToJson(shoot));
            return true;
        }

        private void ApplySnapshot(SnapshotMessage snapshot)
        {
            if (snapshot == null || snapshot.players == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>();
            foreach (PlayerSnapshot player in snapshot.players)
            {
                playerNames[player.id] = string.IsNullOrWhiteSpace(player.name)
                    ? "PLAYER"
                    : player.name;
                if (player.id == localPlayerId)
                {
                    localNetworkY = player.position.y;
                    Vector3 snapshotPosition = ToWorld(player.position);
                    bool respawned = hasLocalAliveState
                        && !localWasAlive
                        && player.alive;
                    if (!hasAuthoritativePosition || respawned)
                        TeleportLocalPlayer(snapshotPosition);
                    authoritativePosition = snapshotPosition;
                    hasAuthoritativePosition = true;
                    if (player.alive)
                        localRespawnAt = 0;
                    localWasAlive = player.alive;
                    hasLocalAliveState = true;
                    if (LocalStateChanged != null)
                    {
                        LocalStateChanged(new GenesisLocalState
                        {
                            health = player.health,
                            kills = player.kills,
                            deaths = player.deaths,
                            alive = player.alive,
                            roundState = snapshot.roundState,
                            roundEndsAt = snapshot.roundEndsAt,
                            serverTime = snapshot.serverTime,
                            respawnAt = localRespawnAt,
                        });
                    }
                    continue;
                }

                seen.Add(player.id);
                RemotePlayerView remote;
                if (!remotes.TryGetValue(player.id, out remote))
                {
                    if (remotePlayerPrefab == null)
                    {
                        continue;
                    }

                    var anchor = new GameObject("Remote_" + player.name);
                    anchor.transform.SetPositionAndRotation(
                        ToWorld(player.position),
                        Quaternion.Euler(0f, player.yaw, 0f));
                    GameObject instance = Instantiate(
                        remotePlayerPrefab, anchor.transform);
                    instance.name = "RecoveredCharacter";
                    NormalizeRemotePresentation(
                        instance.transform, anchor.transform);
                    remote = new RemotePlayerView(anchor.transform);
                    remotes.Add(player.id, remote);
                }

                remote.SetTarget(
                    ToWorld(player.position),
                    Quaternion.Euler(0f, player.yaw, 0f),
                    player.alive);
            }

            List<string> removed = new List<string>();
            foreach (KeyValuePair<string, RemotePlayerView> pair in remotes)
            {
                if (!seen.Contains(pair.Key))
                {
                    Destroy(pair.Value.Transform.gameObject);
                    removed.Add(pair.Key);
                }
            }

            foreach (string id in removed)
            {
                remotes.Remove(id);
                playerNames.Remove(id);
            }

            if (RosterChanged != null && Time.unscaledTime >= nextRosterAt)
            {
                nextRosterAt = Time.unscaledTime + 0.25f;
                GenesisPlayerState[] roster =
                    new GenesisPlayerState[snapshot.players.Length];
                for (int index = 0; index < snapshot.players.Length; index++)
                {
                    PlayerSnapshot player = snapshot.players[index];
                    roster[index] = new GenesisPlayerState
                    {
                        name = playerNames[player.id],
                        kills = player.kills,
                        deaths = player.deaths,
                        alive = player.alive,
                        isLocal = player.id == localPlayerId,
                    };
                }
                RosterChanged(roster);
            }
        }

        private string ResolvePlayerName(string id)
        {
            string name;
            return !string.IsNullOrEmpty(id)
                && playerNames.TryGetValue(id, out name)
                    ? name
                    : "PLAYER";
        }

        private void TeleportLocalPlayer(Vector3 position)
        {
            if (localPlayer == null)
                return;
            bool controllerWasEnabled =
                localController != null && localController.enabled;
            if (controllerWasEnabled)
                localController.enabled = false;
            localPlayer.position = position;
            if (controllerWasEnabled)
                localController.enabled = true;
        }

        private void ClearRemotePlayers()
        {
            foreach (RemotePlayerView remote in remotes.Values)
                Destroy(remote.Transform.gameObject);
            remotes.Clear();
            playerNames.Clear();
        }

        private static void NormalizeRemotePresentation(
            Transform presentation,
            Transform anchor)
        {
            Renderer[] renderers =
                presentation.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            if (bounds.size.y > 0.01f)
            {
                const float targetHeight = 1.9f;
                presentation.localScale *= targetHeight / bounds.size.y;
            }

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            // Network positions represent the local controller's body center.
            // Centering the recovered mesh on an independent anchor prevents its
            // archived 3.16 m bounds and offset pivot from floating above players.
            presentation.position += anchor.position - bounds.center;
        }

        private void Send(string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (socketId != 0)
            {
                GenesisSocketSend(socketId, json);
            }
#endif
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }

        private Vector3 ToWorld(SerializableVector3 position)
        {
            return worldOrigin + position.ToVector3();
        }
    }

    internal sealed class RemotePlayerView
    {
        public readonly Transform Transform;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private bool visible = true;

        public RemotePlayerView(Transform transform)
        {
            Transform = transform;
            targetPosition = transform.position;
            targetRotation = transform.rotation;
        }

        public void SetTarget(
            Vector3 position,
            Quaternion rotation,
            bool shouldBeVisible)
        {
            targetPosition = position;
            targetRotation = rotation;
            if (visible != shouldBeVisible)
            {
                visible = shouldBeVisible;
                Transform.gameObject.SetActive(visible);
            }
        }

        public void Update(float speed, float deltaTime)
        {
            float factor = 1f - Mathf.Exp(-speed * deltaTime);
            Transform.position = Vector3.Lerp(
                Transform.position,
                targetPosition,
                factor);
            Transform.rotation = Quaternion.Slerp(
                Transform.rotation,
                targetRotation,
                factor);
        }
    }

    [Serializable]
    internal sealed class MessageType
    {
        public string type;
    }

    [Serializable]
    internal sealed class JoinMessage
    {
        public string type;
        public string name;
        public string room;
    }

    [Serializable]
    internal sealed class InputMessage
    {
        public string type;
        public int sequence;
        public float moveX;
        public float moveZ;
        public bool jump;
        public float yaw;
        public float pitch;
        public SerializableVector3 position;
    }

    [Serializable]
    internal sealed class ShootMessage
    {
        public string type;
        public int sequence;
        public string weapon;
        public SerializableVector3 direction;
    }

    [Serializable]
    internal sealed class WelcomeMessage
    {
        public string type;
        public string id;
        public string room;
        public int tickRate;
        public long serverTime;
    }

    [Serializable]
    internal sealed class SnapshotMessage
    {
        public string type;
        public int tick;
        public long serverTime;
        public PlayerSnapshot[] players;
        public string roundState;
        public long roundEndsAt;
    }

    [Serializable]
    internal sealed class HitMessage
    {
        public string type;
        public string shooterId;
        public string targetId;
        public int damage;
        public int targetHealth;
    }

    [Serializable]
    internal sealed class DeathMessage
    {
        public string type;
        public string killerId;
        public string victimId;
        public long respawnAt;
    }

    [Serializable]
    internal sealed class PlayerSnapshot
    {
        public string id;
        public string name;
        public SerializableVector3 position;
        public float yaw;
        public float pitch;
        public int health;
        public int kills;
        public int deaths;
        public bool alive;
        public int lastInputSequence;
    }

    [Serializable]
    internal struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }

        public static SerializableVector3 From(Vector3 value)
        {
            return new SerializableVector3
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }
    }

    public struct GenesisLocalState
    {
        public int health;
        public int kills;
        public int deaths;
        public bool alive;
        public string roundState;
        public long roundEndsAt;
        public long serverTime;
        public long respawnAt;
    }

    public struct GenesisPlayerState
    {
        public string name;
        public int kills;
        public int deaths;
        public bool alive;
        public bool isLocal;
    }
}
