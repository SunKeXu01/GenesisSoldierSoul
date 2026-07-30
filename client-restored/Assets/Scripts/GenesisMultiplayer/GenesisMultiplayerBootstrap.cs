using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GenesisSoldierSoul.Multiplayer
{
    internal sealed class GenesisMultiplayerBootstrap : MonoBehaviour
    {
        private const string RemotePlayerResource = "OriginalGame/RemotePlayer";

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

            var camera = player.GetComponentInChildren<Camera>(true);
            var remotePrefab = Resources.Load<GameObject>(RemotePlayerResource);
            var client = player.gameObject.AddComponent<GenesisNetworkClient>();
            client.Configure(
                player.transform,
                camera == null ? null : camera.transform,
                remotePrefab,
                scene.name.ToLowerInvariant());

            if (scene.name == "Pyramid"
                && player.GetComponent<GenesisMatchController>() == null)
            {
                var match = player.gameObject.AddComponent<GenesisMatchController>();
                match.Configure(player.transform, camera, client);
            }
        }
    }
}
