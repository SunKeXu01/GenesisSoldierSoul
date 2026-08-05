using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GenesisSoldierSoul.Multiplayer
{
    public static class GenesisLobbySession
    {
        private const string RoomKey = "Genesis.LobbyRoom";
        private const string TrainingKey = "Genesis.Training";
        private const string MapKey = "Genesis.LobbyMap";

        public static string RoomId
        {
            get { return PlayerPrefs.GetString(RoomKey, string.Empty); }
        }

        public static bool HasRoom
        {
            get { return !string.IsNullOrWhiteSpace(RoomId); }
        }

        public static bool IsTraining
        {
            get { return PlayerPrefs.GetInt(TrainingKey, 0) == 1; }
        }

        public static string SelectedMap
        {
            get { return PlayerPrefs.GetString(MapKey, "pyramid"); }
        }

        public static void SelectMap(string mapId)
        {
            PlayerPrefs.SetString(MapKey, GenesisLobbyRooms.NormalizeMap(mapId));
            PlayerPrefs.Save();
        }

        public static void Select(string roomId)
        {
            PlayerPrefs.SetString(
                RoomKey,
                string.IsNullOrWhiteSpace(roomId)
                    ? string.Empty
                    : roomId.Trim().ToLowerInvariant());
            PlayerPrefs.DeleteKey(TrainingKey);
            PlayerPrefs.Save();
        }

        public static void EnterTraining()
        {
            PlayerPrefs.DeleteKey(RoomKey);
            PlayerPrefs.SetInt(TrainingKey, 1);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(RoomKey);
            PlayerPrefs.DeleteKey(TrainingKey);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Reconnects the recovered free-channel and create-room screens to a real
    /// same-origin room service without replacing the archived lobby artwork.
    /// </summary>
    internal sealed class GenesisLobbyRooms : MonoBehaviour
    {
        private const string RoomListScene = "Ziyou1";
        private const string CreateRoomScene = "Chuangjian";
        private GameObject roomPanel;
        private GameObject mapPanel;
        private Text selectedMapText;
        private Text roomStatus;
        private bool requestRunning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var instance = new GameObject("GenesisLobbyRooms");
            DontDestroyOnLoad(instance);
            instance.AddComponent<GenesisLobbyRooms>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StopAllCoroutines();
            requestRunning = false;
            if (scene.name == RoomListScene)
                StartCoroutine(InitializeRoomList());
            else if (scene.name == CreateRoomScene)
                StartCoroutine(InitializeCreateRoom());
        }

        private IEnumerator InitializeRoomList()
        {
            // WebGL compatibility restores serialized button behavior one frame
            // after loading. Attach the live room UI after that pass.
            yield return null;
            yield return null;
            CreateRoomPanel();
            while (SceneManager.GetActiveScene().name == RoomListScene)
            {
                yield return RefreshRooms();
                yield return new WaitForSecondsRealtime(3f);
            }
        }

        private IEnumerator InitializeCreateRoom()
        {
            yield return null;
            yield return null;
            var confirmObject = GameObject.Find(
                "Canvas/RawImage 1/RawImage/Button 1");
            var confirm = confirmObject == null
                ? null
                : confirmObject.GetComponent<Button>();
            if (confirm == null)
            {
                Debug.LogWarning(
                    "[GenesisLobby] 创建房间确认按钮未找到。");
                yield break;
            }

            // Replace the archived callback that only opened a decorative waiting
            // room. The recovered create screen remains visible and this original
            // confirmation button now creates a server-backed room.
            confirm.onClick = new Button.ButtonClickedEvent();
            confirm.onClick.AddListener(delegate
            {
                if (!requestRunning)
                    StartCoroutine(CreateRoom(confirm));
            });
            CreateMapSelector(confirm.transform.root);
        }

        private void CreateMapSelector(Transform sceneRoot)
        {
            if (mapPanel != null)
                Destroy(mapPanel);
            var canvas = sceneRoot.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                return;

            mapPanel = new GameObject(
                "GenesisMapSelector", typeof(RectTransform), typeof(Image));
            mapPanel.transform.SetParent(canvas.transform, false);
            var rect = mapPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Keep the selector below the recovered confirmation controls. The
            // previous -112 offset covered the original create-room button in
            // WebGL and made every non-training map unreachable.
            rect.anchoredPosition = new Vector2(0f, -175f);
            rect.sizeDelta = new Vector2(430f, 64f);
            mapPanel.GetComponent<Image>().color =
                new Color(0.025f, 0.04f, 0.04f, 0.9f);

            selectedMapText = CreateText(
                mapPanel.transform, "SelectedMap", Vector2.zero,
                new Vector2(320f, 56f), 17, TextAnchor.MiddleCenter);
            selectedMapText.raycastTarget = false;
            CreateSelectorButton("Previous", new Vector2(-188f, 0f), "<", -1);
            CreateSelectorButton("Next", new Vector2(188f, 0f), ">", 1);
            GenesisLobbySession.SelectMap(GenesisLobbySession.SelectedMap);
            RefreshSelectedMapText();
        }

        private void CreateSelectorButton(
            string name, Vector2 position, string caption, int direction)
        {
            var buttonObject = new GameObject(
                name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(mapPanel.transform, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(44f, 44f);
            buttonObject.GetComponent<Image>().color =
                new Color(0.18f, 0.33f, 0.28f, 0.96f);
            var label = CreateText(
                buttonObject.transform, "Label", Vector2.zero,
                rect.sizeDelta, 24, TextAnchor.MiddleCenter);
            label.text = caption;
            buttonObject.GetComponent<Button>().onClick.AddListener(delegate
            {
                CycleSelectedMap(direction);
            });
        }

        private void CycleSelectedMap(int direction)
        {
            var current = Array.IndexOf(MapIds, GenesisLobbySession.SelectedMap);
            if (current < 0)
                current = 0;
            var next = (current + direction + MapIds.Length) % MapIds.Length;
            GenesisLobbySession.SelectMap(MapIds[next]);
            RefreshSelectedMapText();
        }

        private void RefreshSelectedMapText()
        {
            if (selectedMapText != null)
                selectedMapText.text =
                    "MAP  " + DisplayMap(GenesisLobbySession.SelectedMap);
        }

        private void CreateRoomPanel()
        {
            if (roomPanel != null)
                Destroy(roomPanel);
            var canvasObject = GameObject.Find("Canvas");
            var canvas = canvasObject == null
                ? FindObjectOfType<Canvas>()
                : canvasObject.GetComponent<Canvas>();
            if (canvas == null)
                return;

            roomPanel = new GameObject(
                "GenesisRoomBrowser",
                typeof(RectTransform),
                typeof(Image));
            roomPanel.transform.SetParent(canvas.transform, false);
            var panelRect = roomPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(76f, 48f);
            panelRect.sizeDelta = new Vector2(352f, 154f);
            roomPanel.GetComponent<Image>().color =
                new Color(0.035f, 0.045f, 0.045f, 0.86f);

            roomStatus = CreateText(
                roomPanel.transform,
                "Status",
                new Vector2(0f, 0f),
                new Vector2(330f, 136f),
                15,
                TextAnchor.MiddleCenter);
            roomStatus.text = "LOADING ROOMS...";
        }

        private IEnumerator RefreshRooms()
        {
            if (requestRunning || roomPanel == null)
                yield break;
            requestRunning = true;
            using (var request = UnityWebRequest.Get(RoomApiUrl()))
            {
                yield return request.SendWebRequest();
                requestRunning = false;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (roomStatus != null)
                        roomStatus.text =
                            "ROOM SERVER OFFLINE\n" + request.error;
                    yield break;
                }

                var response = JsonUtility.FromJson<RoomListResponse>(
                    request.downloadHandler.text);
                RenderRooms(response == null ? null : response.rooms);
            }
        }

        private void RenderRooms(LobbyRoomData[] rooms)
        {
            if (roomPanel == null)
                return;
            foreach (Transform child in roomPanel.transform)
            {
                if (child.gameObject != roomStatus.gameObject)
                    Destroy(child.gameObject);
            }

            if (rooms == null || rooms.Length == 0)
            {
                roomStatus.gameObject.SetActive(true);
                roomStatus.text =
                    "NO PLAYER ROOMS\nCREATE ROOM OR ENTER TRAINING";
                return;
            }

            roomStatus.gameObject.SetActive(false);
            var shown = Mathf.Min(5, rooms.Length);
            for (var index = 0; index < shown; index++)
            {
                var room = rooms[index];
                var row = new GameObject(
                    "Room_" + room.id,
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button));
                row.transform.SetParent(roomPanel.transform, false);
                var rect = row.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -5f - index * 29f);
                rect.sizeDelta = new Vector2(338f, 25f);
                row.GetComponent<Image>().color = index % 2 == 0
                    ? new Color(0.16f, 0.19f, 0.18f, 0.96f)
                    : new Color(0.11f, 0.14f, 0.13f, 0.96f);

                var label = CreateText(
                    row.transform,
                    "Label",
                    Vector2.zero,
                    rect.sizeDelta,
                    14,
                    TextAnchor.MiddleLeft);
                label.rectTransform.anchoredPosition = new Vector2(8f, 0f);
                label.text = string.Format(
                    "{0}  {1,-16}  MAP {2,-10}  {3}/{4}  JOIN",
                    ShortId(room.id),
                    Truncate(room.name, 16),
                    DisplayMap(room.map),
                    room.players,
                    room.maxPlayers);

                var selectedRoom = room;
                row.GetComponent<Button>().onClick.AddListener(delegate
                {
                    if (selectedRoom.players >= selectedRoom.maxPlayers)
                        return;
                    GenesisLobbySession.Select(selectedRoom.id);
                    SceneManager.LoadScene(SceneName(selectedRoom.map));
                });
            }
        }

        private IEnumerator CreateRoom(Button confirm)
        {
            requestRunning = true;
            confirm.interactable = false;
            var requestData = new CreateRoomRequest
            {
                name = "Genesis-Room-" + UnityEngine.Random.Range(1000, 10000),
                map = GenesisLobbySession.SelectedMap,
            };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestData));
            using (var request = new UnityWebRequest(
                       RoomApiUrl(), UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                requestRunning = false;
                confirm.interactable = true;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        "[GenesisLobby] 创建房间失败: " + request.error);
                    yield break;
                }

                var response = JsonUtility.FromJson<CreateRoomResponse>(
                    request.downloadHandler.text);
                if (response == null || response.room == null)
                {
                    Debug.LogError("[GenesisLobby] 创建房间响应无效。");
                    yield break;
                }
                GenesisLobbySession.Select(response.room.id);
                SceneManager.LoadScene(SceneName(response.room.map));
            }
        }

        private static Text CreateText(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.88f, 0.92f, 0.86f, 1f);
            text.raycastTarget = false;
            return text;
        }

        private static string RoomApiUrl()
        {
            if (!string.IsNullOrWhiteSpace(Application.absoluteURL))
            {
                Uri uri;
                if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out uri))
                    return uri.GetLeftPart(UriPartial.Authority) + "/api/rooms";
            }
            return "http://127.0.0.1:8080/api/rooms";
        }

        private static string SceneName(string map)
        {
            switch (map)
            {
                case "newconstructionsite": return "NewConstructionSite";
                case "biochemicaltown": return "BiochemicalTown";
                case "classicconstructionsite": return "ClassicConstructionSite";
                case "steelfactory": return "SteelFactory";
                case "icefiremaze": return "IceFireMaze";
                case "radiationdistrict": return "RadiationDistrict";
                default: return "Pyramid";
            }
        }

        internal static readonly string[] MapIds =
        {
            "pyramid",
            "newconstructionsite",
            "biochemicaltown",
            "classicconstructionsite",
            "steelfactory",
            "icefiremaze",
            "radiationdistrict",
        };

        internal static string NormalizeMap(string map)
        {
            var normalized = string.IsNullOrWhiteSpace(map)
                ? string.Empty
                : map.Trim().ToLowerInvariant();
            return Array.IndexOf(MapIds, normalized) >= 0
                ? normalized
                : "pyramid";
        }

        private static string DisplayMap(string map)
        {
            switch (map)
            {
                case "newconstructionsite": return "NEW SITE";
                case "biochemicaltown": return "BIO TOWN";
                case "classicconstructionsite": return "OLD SITE";
                case "steelfactory": return "STEEL";
                case "icefiremaze": return "ICE/FIRE";
                case "radiationdistrict": return "RADIATION";
                default: return "PYRAMID";
            }
        }

        private static string ShortId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "--------";
            return value.Length <= 8 ? value : value.Substring(0, 8);
        }

        private static string Truncate(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length)
                return value ?? string.Empty;
            return value.Substring(0, length - 1) + "…";
        }
    }

    [Serializable]
    internal sealed class LobbyRoomData
    {
        public string id;
        public string name;
        public string map;
        public int players;
        public int maxPlayers;
        public long createdAt;
    }

    [Serializable]
    internal sealed class RoomListResponse
    {
        public LobbyRoomData[] rooms;
    }

    [Serializable]
    internal sealed class CreateRoomRequest
    {
        public string name;
        public string map;
    }

    [Serializable]
    internal sealed class CreateRoomResponse
    {
        public LobbyRoomData room;
    }
}
