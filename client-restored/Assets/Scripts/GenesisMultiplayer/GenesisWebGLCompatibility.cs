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
            var restoredCloseButtons = 0;
            // AssetRipper preserved the serialized Button.onClick callbacks.
            // The recovered scripts also add the same callback again in Start(),
            // causing two LoadScene calls from one click and aborting WebGL.
            // Remove runtime-added listeners while retaining serialized originals.
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button.gameObject.scene != SceneManager.GetActiveScene())
                    continue;
                button.onClick.RemoveAllListeners();

                // Asset recovery also produced full-panel decorative Buttons
                // with no persistent callback. They sit above real controls in
                // several lobby popups and consume every pointer ray, including
                // the visible close button. A zero-callback button has no action
                // to preserve, so make it transparent to UI raycasts.
                if (button.onClick.GetPersistentEventCount() == 0)
                {
                    button.interactable = false;
                    if (button.targetGraphic != null)
                        button.targetGraphic.raycastTarget = false;
                }

                // Some recovered UnityEvents retain their serialized count but
                // lose the callable target in WebGL. Rebind the small close
                // component directly; setting the same panel inactive twice is
                // harmless when the persistent callback is still valid.
                foreach (var close in button.GetComponents<guanbi>())
                {
                    if (close.option == null)
                        continue;
                    button.onClick.AddListener(close.Click);
                    restoredCloseButtons += 1;
                }
            }
            if (restoredCloseButtons > 0)
            {
                Debug.Log(
                    "[GenesisWebGL] 已恢复 " + restoredCloseButtons
                    + " 个大厅弹窗关闭按钮。");
            }

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == "Ziyou1")
            {
                GenesisExitGameplayMode();
                // The recovered lobby already contains the original “训练模式”
                // button, but its archived callback only played the click sound.
                // Pyramid is the verified first map in the recovered-map
                // rotation. Later rounds advance through the other promoted maps.
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
            else if (GenesisMultiplayerBootstrap.IsPlayableMap(activeScene.name))
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.None;
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
            // Install canvas-relative mouse tracking before loading gameplay.
            // Fullscreen remains an explicit template-button choice and the
            // WebGL client intentionally does not request Pointer Lock.
            GenesisEnterGameplayMode();
            GenesisLobbySession.EnterTraining();
            SceneManager.LoadScene("Pyramid");
        }
#endif
    }
}
