using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GenesisSoldierSoul.Multiplayer
{
    internal sealed class GenesisRuntimeProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject instance = new GameObject("GenesisRuntimeProbe");
            DontDestroyOnLoad(instance);
            instance.AddComponent<GenesisRuntimeProbe>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            LogScene(SceneManager.GetActiveScene());
            StartCoroutine(LogDelayedScene());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogScene(scene);
        }

        private static void LogScene(Scene scene)
        {
            string roots = string.Join(
                ", ",
                scene.GetRootGameObjects().Select(root => root.name).Take(20));
            Debug.Log(
                "[GenesisProbe] Scene=" +
                scene.name +
                " roots=" +
                roots);
        }

        private static IEnumerator LogDelayedScene()
        {
            yield return new WaitForSecondsRealtime(5f);
            LogScene(SceneManager.GetActiveScene());
        }
    }
}
