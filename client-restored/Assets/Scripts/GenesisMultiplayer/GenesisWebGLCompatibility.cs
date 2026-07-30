using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Keeps recovered desktop-only presentation features from blocking the
    /// browser build. The original Scene1 intro uses an embedded VideoClip,
    /// which Unity WebGL cannot play.
    /// </summary>
    internal sealed class GenesisWebGLCompatibility : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void GenesisEnterGameplayMode();

        [DllImport("__Internal")]
        private static extern void GenesisExitGameplayMode();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject instance = new GameObject("GenesisWebGLCompatibility");
            DontDestroyOnLoad(instance);
            instance.AddComponent<GenesisWebGLCompatibility>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(ApplyAfterLayout());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(ApplyAfterLayout());
        }

        private static IEnumerator ApplyAfterLayout()
        {
            yield return null;
            // AssetRipper preserved the serialized Button.onClick callbacks.
            // The recovered scripts also add the same callback again in Start(),
            // causing two LoadScene calls from one click and aborting WebGL.
            // Remove runtime-added listeners while retaining serialized originals.
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button.gameObject.scene == SceneManager.GetActiveScene())
                    button.onClick.RemoveAllListeners();
            }

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == "Ziyou1")
            {
                GenesisExitGameplayMode();
                // The recovered lobby already contains the original “训练模式”
                // button, but its archived callback only played the click sound.
                // Use the recovered pyramid scene because it is the most complete
                // surviving map package (environment, colliders and scene light).
                var trainingButton = GameObject.Find(
                    "Canvas/RawImage 1/RawImage/Button 2");
                var button = trainingButton == null
                    ? null
                    : trainingButton.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.AddListener(EnterPyramid);
                    Debug.Log(
                        "[GenesisWebGL] 已接通原版训练模式按钮和恢复地图。");
                }
            }
            else if (activeScene.name == "Pyramid")
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (activeScene.name != "Scene1")
            {
                yield break;
            }

            GameObject introOverlay = GameObject.Find("Image_jz");
            if (introOverlay != null)
            {
                introOverlay.SetActive(false);
                Debug.Log(
                    "[GenesisWebGL] 已跳过浏览器不支持的原版内嵌片头视频。");
            }
        }

        private static void EnterPyramid()
        {
            // Fullscreen and pointer lock are browser-gated APIs. They must be
            // requested synchronously from the original button click, before
            // the asynchronous scene load consumes the user gesture.
            GenesisEnterGameplayMode();
            SceneManager.LoadScene("Pyramid");
        }
#endif
    }
}
