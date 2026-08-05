using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GenesisSoldierSoul.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Connects functional settings to the recovered Zhu lobby artwork instead
    /// of replacing the archived panel with a new menu.
    /// </summary>
    internal sealed class GenesisLobbySettings : MonoBehaviour
    {
        private const string LobbyScene = "Zhu";
        private const string SettingsButtonPath =
            "Canvas/RawImage 1/RawImage/Button 11";
        private const string SettingsPanelPath =
            "Canvas/RawImage 1/RawImage/RawImage 2";

        private readonly Dictionary<int, float> sourceBaseVolumes =
            new Dictionary<int, float>();
        private GenesisSettingsSnapshot draft;
        private GameObject functionalPanel;
        private Text masterValue;
        private Text musicValue;
        private Text effectsValue;
        private Text sensitivityValue;
        private Text qualityValue;
        private Text frameRateValue;
        private float nextRuntimeApplyAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var instance = new GameObject("GenesisLobbySettings");
            DontDestroyOnLoad(instance);
            instance.AddComponent<GenesisLobbySettings>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GenesisUserSettings.ApplyCurrent();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StopAllCoroutines();
            sourceBaseVolumes.Clear();
            functionalPanel = null;
            GenesisUserSettings.ApplyCurrent();
            ApplyRuntimeConsumers();
            if (scene.name == LobbyScene)
                StartCoroutine(AttachToRecoveredSettings());
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRuntimeApplyAt)
                return;
            nextRuntimeApplyAt = Time.unscaledTime + 0.5f;
            ApplyRuntimeConsumers();
        }

        private IEnumerator AttachToRecoveredSettings()
        {
            yield return null;
            yield return null;
            var buttonObject = FindSceneObject(SettingsButtonPath);
            var settingsButton = buttonObject == null
                ? null
                : buttonObject.GetComponent<Button>();
            var recoveredPanel = FindSceneObject(SettingsPanelPath);
            if (settingsButton == null || recoveredPanel == null)
            {
                Debug.LogWarning(
                    "[GenesisSettings] Recovered settings controls are missing.");
                yield break;
            }
            settingsButton.onClick.AddListener(delegate
            {
                StartCoroutine(ShowAfterRecoveredPanelOpens());
            });
            BindRecoveredFooter(recoveredPanel.transform);
            if (recoveredPanel.activeInHierarchy)
                ShowSettings(recoveredPanel.transform);
            Debug.Log(
                "[GenesisSettings] Recovered settings panel is now functional.");
        }

        private IEnumerator ShowAfterRecoveredPanelOpens()
        {
            yield return null;
            var recoveredPanel = FindSceneObject(SettingsPanelPath);
            if (recoveredPanel != null)
                ShowSettings(recoveredPanel.transform);
        }

        private void BindRecoveredFooter(Transform panel)
        {
            var cancel = panel.Find("Button 1");
            var save = panel.Find("Button 2");
            var defaults = panel.Find("Button 3");
            if (cancel != null)
                cancel.GetComponent<Button>().onClick.AddListener(CancelDraft);
            if (save != null)
                save.GetComponent<Button>().onClick.AddListener(SaveDraft);
            if (defaults != null)
                defaults.GetComponent<Button>().onClick.AddListener(ResetDraft);
        }

        private void ShowSettings(Transform parent)
        {
            draft = GenesisUserSettings.Current;
            if (functionalPanel == null)
                CreateFunctionalPanel(parent);
            functionalPanel.SetActive(true);
            RefreshValues();
        }

        private void CreateFunctionalPanel(Transform parent)
        {
            functionalPanel = new GameObject(
                "GenesisFunctionalSettings",
                typeof(RectTransform),
                typeof(Image));
            functionalPanel.transform.SetParent(parent, false);
            var rect = (RectTransform)functionalPanel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(350f, 260f);
            rect.anchoredPosition = new Vector2(0f, 18f);
            functionalPanel.GetComponent<Image>().color =
                new Color(0.025f, 0.04f, 0.05f, 0.94f);

            CreateText(rect, "运行时设置", new Vector2(0f, 112f),
                new Vector2(330f, 28f), 18, TextAnchor.MiddleCenter,
                new Color(0.55f, 0.9f, 1f, 1f));
            masterValue = CreateStepRow(rect, "总音量", 75f,
                delegate { AdjustVolume(ref draft.MasterVolume, -0.05f); },
                delegate { AdjustVolume(ref draft.MasterVolume, 0.05f); });
            musicValue = CreateStepRow(rect, "音乐", 39f,
                delegate { AdjustVolume(ref draft.MusicVolume, -0.05f); },
                delegate { AdjustVolume(ref draft.MusicVolume, 0.05f); });
            effectsValue = CreateStepRow(rect, "音效", 3f,
                delegate { AdjustVolume(ref draft.EffectsVolume, -0.05f); },
                delegate { AdjustVolume(ref draft.EffectsVolume, 0.05f); });
            sensitivityValue = CreateStepRow(rect, "鼠标灵敏度", -33f,
                delegate
                {
                    draft.MouseSensitivity = Mathf.Clamp(
                        draft.MouseSensitivity - 10f, 25f, 250f);
                    PreviewDraft();
                },
                delegate
                {
                    draft.MouseSensitivity = Mathf.Clamp(
                        draft.MouseSensitivity + 10f, 25f, 250f);
                    PreviewDraft();
                });
            qualityValue = CreateStepRow(rect, "画质", -69f,
                delegate { AdjustQuality(-1); },
                delegate { AdjustQuality(1); });
            frameRateValue = CreateStepRow(rect, "帧率上限", -105f,
                delegate { AdjustFrameRate(-1); },
                delegate { AdjustFrameRate(1); });
        }

        private Text CreateStepRow(
            RectTransform parent,
            string label,
            float y,
            UnityEngine.Events.UnityAction decrease,
            UnityEngine.Events.UnityAction increase)
        {
            CreateText(parent, label, new Vector2(-112f, y),
                new Vector2(105f, 28f), 14, TextAnchor.MiddleLeft, Color.white);
            var value = CreateText(parent, string.Empty, new Vector2(28f, y),
                new Vector2(95f, 28f), 14, TextAnchor.MiddleCenter,
                new Color(0.75f, 0.94f, 1f, 1f));
            CreateButton(parent, "−", new Vector2(101f, y), decrease);
            CreateButton(parent, "+", new Vector2(139f, y), increase);
            return value;
        }

        private static Text CreateText(
            RectTransform parent,
            string value,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var item = new GameObject("Text_" + value, typeof(RectTransform), typeof(Text));
            item.transform.SetParent(parent, false);
            var rect = (RectTransform)item.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = item.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            return text;
        }

        private static void CreateButton(
            RectTransform parent,
            string label,
            Vector2 position,
            UnityEngine.Events.UnityAction action)
        {
            var item = new GameObject(
                "Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            item.transform.SetParent(parent, false);
            var rect = (RectTransform)item.transform;
            rect.sizeDelta = new Vector2(32f, 24f);
            rect.anchoredPosition = position;
            item.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.27f, 1f);
            item.GetComponent<Button>().onClick.AddListener(action);
            CreateText(rect, label, Vector2.zero, rect.sizeDelta, 16,
                TextAnchor.MiddleCenter, Color.white);
        }

        private void AdjustVolume(ref float value, float delta)
        {
            value = Mathf.Clamp01(value + delta);
            PreviewDraft();
        }

        private void AdjustQuality(int direction)
        {
            draft.QualityLevel = Mathf.Clamp(
                draft.QualityLevel + direction,
                0,
                Mathf.Max(0, QualitySettings.names.Length - 1));
            PreviewDraft();
        }

        private void AdjustFrameRate(int direction)
        {
            var rates = new[] { 30, 45, 60 };
            var index = System.Array.IndexOf(rates, draft.TargetFrameRate);
            draft.TargetFrameRate = rates[Mathf.Clamp(index + direction, 0, 2)];
            PreviewDraft();
        }

        private void PreviewDraft()
        {
            draft = GenesisUserSettings.Sanitize(draft);
            GenesisUserSettings.Apply(draft);
            RefreshValues();
            ApplyRuntimeConsumers(draft);
        }

        private void SaveDraft()
        {
            GenesisUserSettings.Save(draft);
            draft = GenesisUserSettings.Current;
            RefreshValues();
        }

        private void ResetDraft()
        {
            draft = GenesisUserSettings.Defaults;
            PreviewDraft();
        }

        private void CancelDraft()
        {
            draft = GenesisUserSettings.Current;
            GenesisUserSettings.ApplyCurrent();
            ApplyRuntimeConsumers();
            RefreshValues();
        }

        private void RefreshValues()
        {
            if (masterValue == null)
                return;
            masterValue.text = Mathf.RoundToInt(draft.MasterVolume * 100f) + "%";
            musicValue.text = Mathf.RoundToInt(draft.MusicVolume * 100f) + "%";
            effectsValue.text = Mathf.RoundToInt(draft.EffectsVolume * 100f) + "%";
            sensitivityValue.text = Mathf.RoundToInt(draft.MouseSensitivity).ToString();
            qualityValue.text = QualitySettings.names.Length == 0
                ? "默认"
                : QualitySettings.names[Mathf.Clamp(
                    draft.QualityLevel, 0, QualitySettings.names.Length - 1)];
            frameRateValue.text = draft.TargetFrameRate + " FPS";
        }

        private void ApplyRuntimeConsumers()
        {
            ApplyRuntimeConsumers(GenesisUserSettings.Current);
        }

        private void ApplyRuntimeConsumers(GenesisSettingsSnapshot value)
        {
            foreach (var mouse in FindObjectsOfType<MouseLook>())
                mouse.mouseSensitivity = value.MouseSensitivity;
            foreach (var source in FindObjectsOfType<AudioSource>())
            {
                var id = source.GetInstanceID();
                float baseVolume;
                if (!sourceBaseVolumes.TryGetValue(id, out baseVolume))
                {
                    baseVolume = source.volume;
                    sourceBaseVolumes[id] = baseVolume;
                }
                source.volume = baseVolume
                    * (source.loop || source.playOnAwake
                        ? value.MusicVolume
                        : value.EffectsVolume);
            }
        }

        private static GameObject FindSceneObject(string hierarchyPath)
        {
            var segments = hierarchyPath.Split('/');
            if (segments.Length == 0)
                return null;
            var scene = SceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects()
                .FirstOrDefault(item => item.name == segments[0]);
            if (root == null)
                return null;
            var current = root.transform;
            for (var index = 1; index < segments.Length; index += 1)
            {
                current = current.Find(segments[index]);
                if (current == null)
                    return null;
            }
            return current.gameObject;
        }
    }
}
