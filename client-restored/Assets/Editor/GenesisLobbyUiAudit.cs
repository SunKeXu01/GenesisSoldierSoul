using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using GenesisSoldierSoul.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class GenesisLobbyUiAudit
{
    [MenuItem("Genesis/UI/Audit Create Room Map Selector")]
    public static void AuditCreateRoomMapSelector()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/Chuangjian.unity", OpenSceneMode.Single);
        var confirmObject = GameObject.Find(
            "Canvas/RawImage 1/RawImage/Button 1");
        var canvas = Object.FindObjectOfType<Canvas>();
        if (confirmObject == null || canvas == null)
            throw new System.InvalidOperationException(
                "Create-room confirmation button or Canvas is missing.");

        var runtimeType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(
                "GenesisSoldierSoul.Multiplayer.GenesisLobbyRooms", true);
        var host = new GameObject("CreateRoomLayoutAuditHost");
        var runtime = host.AddComponent(runtimeType);
        runtimeType.GetMethod(
                "CreateMapSelector",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runtime, new object[] { confirmObject.transform.root });
        Canvas.ForceUpdateCanvases();

        var selector = Object.FindObjectsOfType<RectTransform>()
            .SingleOrDefault(item => item.name == "GenesisMapSelector");
        var confirm = confirmObject.transform as RectTransform;
        if (selector == null || confirm == null)
            throw new System.InvalidOperationException(
                "Create-room selector or confirmation RectTransform is missing.");
        var selectorBounds = RectTransformUtility
            .CalculateRelativeRectTransformBounds(canvas.transform, selector);
        var confirmBounds = RectTransformUtility
            .CalculateRelativeRectTransformBounds(canvas.transform, confirm);
        var overlaps = ToRect(selectorBounds).Overlaps(ToRect(confirmBounds));
        var selectedMap = selector.Find("SelectedMap")
            .GetComponent<Text>();
        var selectedMapBlocksRaycasts = selectedMap == null
            || selectedMap.raycastTarget;
        var selectorInsideCanvas = ToRect(
            RectTransformUtility.CalculateRelativeRectTransformBounds(
                canvas.transform, canvas.transform as RectTransform))
            .Overlaps(ToRect(selectorBounds));

        Debug.Log(string.Format(
            "[GenesisCreateRoomLayoutAudit] scene={0}, selector={1}, "
                + "confirm={2}, overlaps={3}, selectorInsideCanvas={4}, "
                + "selectedMapRaycast={5}",
            scene.name, selectorBounds, confirmBounds, overlaps,
            selectorInsideCanvas,
            selectedMapBlocksRaycasts));
        var failed = overlaps || !selectorInsideCanvas
            || selectedMapBlocksRaycasts;
        Object.DestroyImmediate(host);
        Object.DestroyImmediate(selector.gameObject);
        if (failed)
        {
            throw new System.InvalidOperationException(
                "Create-room map selector blocks confirmation or is outside the Canvas.");
        }
    }

    private static Rect ToRect(Bounds bounds)
    {
        return Rect.MinMaxRect(
            bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
    }

    [MenuItem("Genesis/UI/Audit Functional Lobby Settings")]
    public static void AuditFunctionalLobbySettings()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/Zhu.unity", OpenSceneMode.Single);
        var buttonObject = GameObject.Find(
            "Canvas/RawImage 1/RawImage/Button 11");
        var recoveredPanel = GameObject.Find(
            "Canvas/RawImage 1/RawImage/RawImage 2");
        if (buttonObject == null || recoveredPanel == null)
            throw new System.InvalidOperationException(
                "Recovered settings entry or panel is missing.");
        var opener = buttonObject.GetComponent<Kaiqi>();
        var background = recoveredPanel.GetComponent<RawImage>();
        var backgroundPath = background == null || background.texture == null
            ? string.Empty
            : AssetDatabase.GetAssetPath(background.texture);
        if (opener == null || opener.option != recoveredPanel
            || backgroundPath != "Assets/Texture2D/设置 1.png")
        {
            throw new System.InvalidOperationException(
                "Recovered settings artwork is not connected to Button 11.");
        }

        var runtimeType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(
                "GenesisSoldierSoul.Multiplayer.GenesisLobbySettings", true);
        var host = new GameObject("SettingsAuditHost");
        var runtime = host.AddComponent(runtimeType);
        var create = runtimeType.GetMethod(
            "CreateFunctionalPanel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var bind = runtimeType.GetMethod(
            "BindRecoveredFooter",
            BindingFlags.Instance | BindingFlags.NonPublic);
        create.Invoke(runtime, new object[] { recoveredPanel.transform });
        bind.Invoke(runtime, new object[] { recoveredPanel.transform });
        runtimeType.GetField(
                "draft", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(runtime, GenesisUserSettings.Current);
        runtimeType.GetMethod(
                "RefreshValues", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runtime, null);

        var functional = recoveredPanel.transform.Find(
            "GenesisFunctionalSettings") as RectTransform;
        var panelRect = recoveredPanel.transform as RectTransform;
        var footerButtons = new[] { "Button 1", "Button 2", "Button 3" }
            .Count(name => recoveredPanel.transform.Find(name) != null);
        var stepButtons = functional == null
            ? new Button[0]
            : functional.GetComponentsInChildren<Button>(true);
        var labels = functional == null
            ? new Text[0]
            : functional.GetComponentsInChildren<Text>(true);
        var contained = functional != null && panelRect != null
            && functional.rect.width <= panelRect.rect.width
            && functional.rect.height <= panelRect.rect.height;

        var defaults = GenesisUserSettings.Defaults;
        var sanitized = GenesisUserSettings.Sanitize(defaults);
        recoveredPanel.SetActive(true);
        var previewPath = RenderSettingsPreview();
        Object.DestroyImmediate(host);
        if (functional != null)
            Object.DestroyImmediate(functional.gameObject);
        GenesisUserSettings.ApplyCurrent();

        Debug.Log(string.Format(
            "[GenesisLobbySettingsAudit] scene={0}, artwork={1}, "
                + "footerButtons={2}, stepButtons={3}, labels={4}, "
                + "contained={5}, defaultsValid={6}, preview={7}",
            scene.name, backgroundPath, footerButtons, stepButtons.Length,
            labels.Length, contained, sanitized.Equals(defaults), previewPath));
        if (footerButtons != 3 || stepButtons.Length != 12
            || labels.Length < 19 || !contained || !sanitized.Equals(defaults))
        {
            throw new System.InvalidOperationException(
                "Functional settings panel audit failed.");
        }
    }

    private static string RenderSettingsPreview()
    {
        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
            throw new System.InvalidOperationException("Lobby Canvas is missing.");
        var cameraObject = new GameObject("LobbySettingsPreviewCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.025f, 0.035f, 0.045f, 1f);
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.orthographic = true;
        var previousMode = canvas.renderMode;
        var previousCamera = canvas.worldCamera;
        var texture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        camera.targetTexture = texture;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
        image.Apply();
        var outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../recovery/lobby-settings-preview.png"));
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        canvas.renderMode = previousMode;
        canvas.worldCamera = previousCamera;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(cameraObject);
        return outputPath;
    }

    [MenuItem("Genesis/UI/Audit Lobby Buttons")]
    public static void AuditLobbyButtons()
    {
        var output = new StringBuilder();
        output.AppendLine(
            "scene,active,interactable,hierarchy,center,size,persistentEvents,invalidEvents,callbacks,behaviours,openTargets");

        var scenePaths = new[]
        {
            "Assets/Scenes/Loading.unity",
            "Assets/Scenes/Zhu.unity",
            "Assets/Scenes/Ziyou1.unity",
        };
        var buttonCount = 0;
        var invalidEventCount = 0;
        foreach (var scenePath in scenePaths)
        {
            var scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            var buttons = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Button>(true))
                .OrderBy(button => HierarchyPath(button.transform))
                .ToArray();
            buttonCount += buttons.Length;
            foreach (var button in buttons)
            {
                var rect = button.transform as RectTransform;
                var center = rect == null
                    ? Vector2.zero
                    : (Vector2)rect.TransformPoint(rect.rect.center);
                var size = rect == null ? Vector2.zero : rect.rect.size;
                var behaviours = button.GetComponents<MonoBehaviour>()
                    .Where(item => item != null)
                    .Select(item => item.GetType().Name)
                    .ToArray();
                var openTargets = button.GetComponents<Kaiqi>()
                    .Where(item => item != null && item.option != null)
                    .Select(item => HierarchyPath(item.option.transform))
                    .ToArray();
                foreach (var opener in button.GetComponents<Kaiqi>())
                {
                    if (scene.name != "Zhu" || opener == null
                        || opener.option == null
                        || opener.transform.parent == null
                        || opener.transform.parent.name != "RawImage")
                        continue;
                    var textures = opener.option
                        .GetComponentsInChildren<RawImage>(true)
                        .Where(item => item.texture != null)
                        .Select(item => AssetDatabase.GetAssetPath(item.texture))
                        .Distinct()
                        .ToArray();
                    var texts = opener.option
                        .GetComponentsInChildren<Text>(true)
                        .Select(item => item.text)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct()
                        .ToArray();
                    Debug.Log(
                        "[GenesisLobbyPanel] button="
                        + HierarchyPath(button.transform)
                        + ", target=" + HierarchyPath(opener.option.transform)
                        + ", textures=" + string.Join(";", textures)
                        + ", texts=" + string.Join(";", texts));
                }
                var callbackNames = new string[
                    button.onClick.GetPersistentEventCount()];
                var invalidForButton = 0;
                for (var eventIndex = 0;
                     eventIndex < callbackNames.Length;
                     eventIndex += 1)
                {
                    var target = button.onClick.GetPersistentTarget(eventIndex);
                    var method = button.onClick.GetPersistentMethodName(eventIndex);
                    if (target == null || string.IsNullOrWhiteSpace(method))
                    {
                        invalidForButton += 1;
                        callbackNames[eventIndex] = "MISSING";
                    }
                    else
                    {
                        callbackNames[eventIndex] =
                            target.GetType().Name + "." + method;
                    }
                }
                invalidEventCount += invalidForButton;
                output.Append(scene.name).Append(',')
                    .Append(button.gameObject.activeInHierarchy).Append(',')
                    .Append(button.interactable).Append(',')
                    .Append(Csv(HierarchyPath(button.transform))).Append(',')
                    .Append(Csv(center.ToString("F1"))).Append(',')
                    .Append(Csv(size.ToString("F1"))).Append(',')
                    .Append(button.onClick.GetPersistentEventCount()).Append(',')
                    .Append(invalidForButton).Append(',')
                    .Append(Csv(string.Join(";", callbackNames))).Append(',')
                    .Append(Csv(string.Join(";", behaviours))).Append(',')
                    .AppendLine(Csv(string.Join(";", openTargets)));
            }
        }

        var auditDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../recovery"));
        Directory.CreateDirectory(auditDirectory);
        var auditPath = Path.Combine(auditDirectory, "lobby-button-audit.csv");
        File.WriteAllText(auditPath, output.ToString(), new UTF8Encoding(true));
        Debug.Log(
            "[GenesisLobbyUiAudit] " + buttonCount
            + " buttons, invalidEvents=" + invalidEventCount
            + " written to " + auditPath);
        if (invalidEventCount > 0)
            throw new System.InvalidOperationException(
                "Recovered lobby contains " + invalidEventCount
                + " persistent callbacks with missing targets or methods.");
    }

    private static string HierarchyPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
