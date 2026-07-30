using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    public sealed class GenesisNetworkClient : MonoBehaviour
    {
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
        private int socketId;
        private int sequence;
        private float nextInputAt;
        private string localPlayerId;
        private bool connected;
        private Vector3 authoritativePosition;
        private bool hasAuthoritativePosition;
        private Vector3 worldOrigin;

        public event Action<bool> ConnectionChanged;
        public event Action<GenesisLocalState> LocalStateChanged;
        public event Action<bool> HitConfirmed;

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

            if (viewCamera == null && Camera.main != null)
            {
                viewCamera = Camera.main.transform;
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
        }

        private void Start()
        {
            Connect();
        }

        private void Update()
        {
            if (!connected || string.IsNullOrEmpty(localPlayerId))
            {
                return;
            }

            if (Time.unscaledTime >= nextInputAt)
            {
                nextInputAt = Time.unscaledTime + 1f / inputRate;
                SendInput();
            }

            if (hasAuthoritativePosition && localPlayer != null)
            {
                localPlayer.position = Vector3.Lerp(
                    localPlayer.position,
                    authoritativePosition,
                    1f - Mathf.Exp(-reconciliationSpeed * Time.deltaTime));
            }

            foreach (RemotePlayerView remote in remotes.Values)
            {
                remote.Update(remoteInterpolationSpeed, Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
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
            JoinMessage join = new JoinMessage
            {
                type = "join",
                name = string.IsNullOrWhiteSpace(playerName)
                    ? "新兵"
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
                Debug.Log("击杀：" + death.killerId + " -> " + death.victimId);
            }
        }

        public void OnSocketClose(string reason)
        {
            connected = false;
            localPlayerId = null;
            if (ConnectionChanged != null)
                ConnectionChanged(false);
            Debug.LogWarning("对战服务器连接已关闭：" + reason);
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
                if (player.id == localPlayerId)
                {
                    authoritativePosition = ToWorld(player.position);
                    hasAuthoritativePosition = true;
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

                    GameObject instance = Instantiate(
                        remotePlayerPrefab,
                        ToWorld(player.position),
                        Quaternion.Euler(0f, player.yaw, 0f));
                    instance.name = "Remote_" + player.name;
                    remote = new RemotePlayerView(instance.transform);
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
            }
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
    }
}
