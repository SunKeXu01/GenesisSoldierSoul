using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GenesisSoldierSoul.Multiplayer
{
    public sealed class GenesisMultiplayerBootstrap : MonoBehaviour
    {
        private const string RemotePlayerResource = "OriginalGame/RemotePlayer";
        private static readonly string[] PlayableMapScenes =
        {
            "Pyramid",
            "NewConstructionSite",
            "BiochemicalTown",
            "ClassicConstructionSite",
            "SteelFactory",
            "IceFireMaze",
            "RadiationDistrict",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            GameObject instance = new GameObject("GenesisMultiplayerBootstrap");
            DontDestroyOnLoad(instance);
            instance.AddComponent<GenesisMultiplayerBootstrap>();
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
            StartCoroutine(AttachAfterSceneInitialization(scene));
        }

        private static IEnumerator AttachAfterSceneInitialization(Scene scene)
        {
            yield return null;
            if (!scene.isLoaded)
                yield break;

            CharacterController player = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var candidate = root.GetComponentInChildren<CharacterController>(true);
                if (candidate != null && root.name == "First Person Player")
                {
                    player = candidate;
                    break;
                }
            }

            if (player == null || player.GetComponent<GenesisNetworkClient>() != null)
                yield break;

            // Several archived scenes stored the recovered gameplay rig disabled
            // and relied on now-missing loading code to enable it.
            player.gameObject.SetActive(true);

            var camera = player.GetComponentInChildren<Camera>(true);
            var remotePrefab = Resources.Load<GameObject>(RemotePlayerResource);
            GenesisNetworkClient client = null;
            if (!GenesisLobbySession.IsTraining)
            {
                client = player.gameObject.AddComponent<GenesisNetworkClient>();
                var networkRoom = GenesisLobbySession.HasRoom
                    ? GenesisLobbySession.RoomId
                    : scene.name.ToLowerInvariant();
                client.Configure(
                    player.transform,
                    camera == null ? null : camera.transform,
                    remotePrefab,
                    networkRoom,
                    scene.name.ToLowerInvariant());
            }

            if (IsPlayableMap(scene.name)
                && player.GetComponent<GenesisMatchController>() == null)
            {
                var match = player.gameObject.AddComponent<GenesisMatchController>();
                match.Configure(player.transform, camera, client);
                if (GenesisLobbySession.IsTraining)
                {
                    var training =
                        player.gameObject.AddComponent<GenesisTrainingArena>();
                    training.Configure(player.transform, remotePrefab, match);
                }
            }
        }

        public static bool IsPlayableMap(string sceneName)
        {
            return System.Array.IndexOf(PlayableMapScenes, sceneName) >= 0;
        }

        public static string GetDisplayName(string sceneName)
        {
            switch (sceneName)
            {
                case "Pyramid": return "PYRAMID";
                case "NewConstructionSite": return "NEW CONSTRUCTION";
                case "ClassicConstructionSite": return "CLASSIC CONSTRUCTION";
                case "SteelFactory": return "STEEL FACTORY";
                case "BiochemicalTown": return "BIOCHEMICAL TOWN";
                case "RadiationDistrict": return "RADIATION DISTRICT";
                case "IceFireMaze": return "ICE AND FIRE";
                default: return sceneName;
            }
        }

        public static string GetNextPlayableMap(string sceneName)
        {
            var index = System.Array.IndexOf(PlayableMapScenes, sceneName);
            return PlayableMapScenes[(index + 1 + PlayableMapScenes.Length)
                % PlayableMapScenes.Length];
        }
    }
}
