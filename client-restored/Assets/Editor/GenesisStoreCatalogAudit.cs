using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GenesisSoldierSoul.Catalog;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GenesisStoreCatalogAudit
{
    private const string PrefabPath =
        "Assets/Resources/OriginalGame/UI/GenesisStoreBackdrop.prefab";

    [MenuItem("Genesis/UI/Build Recovered Store Backdrop")]
    public static void BuildRecoveredStoreBackdrop()
    {
        var directory = Path.GetDirectoryName(PrefabPath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var root = new GameObject(
            "GenesisStoreBackdrop", typeof(RectTransform), typeof(RawImage));
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var background = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/OriginalGame/UI/storeUI.110482/bitmap-00004.png");
        var wall = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/OriginalGame/UI/storeUI.110482/bitmap-00293.png");
        if (background == null || wall == null)
            throw new InvalidOperationException("Recovered store textures are missing.");
        root.GetComponent<RawImage>().texture = background;
        root.GetComponent<RawImage>().color = new Color(0.72f, 0.76f, 0.76f, 1f);

        var wallObject = new GameObject(
            "RecoveredWeaponWall", typeof(RectTransform), typeof(RawImage));
        wallObject.transform.SetParent(root.transform, false);
        var wallRect = (RectTransform)wallObject.transform;
        wallRect.anchorMin = new Vector2(0.72f, 0.08f);
        wallRect.anchorMax = new Vector2(0.98f, 0.92f);
        wallRect.offsetMin = Vector2.zero;
        wallRect.offsetMax = Vector2.zero;
        var wallImage = wallObject.GetComponent<RawImage>();
        wallImage.texture = wall;
        wallImage.color = new Color(1f, 1f, 1f, 0.42f);
        wallImage.raycastTarget = false;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GenesisStoreCatalog] Built recovered backdrop: " + PrefabPath);
    }

    [MenuItem("Genesis/UI/Audit Recovered Store Catalog")]
    public static void AuditRecoveredStoreCatalog()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Store backdrop prefab is missing.");
        var rawImages = prefab.GetComponentsInChildren<RawImage>(true);
        var texturePaths = rawImages
            .Where(item => item.texture != null)
            .Select(item => AssetDatabase.GetAssetPath(item.texture))
            .Distinct()
            .ToArray();
        var entries = GenesisStoreCatalog.Build("m4a1", false);
        var missing = entries
            .Where(item => Resources.Load<GameObject>(item.ResourcePath) == null)
            .Select(item => item.Id)
            .ToArray();
        var candidateCount = entries.Count(item =>
            item.Availability == GenesisCatalogAvailability.RecoveryCandidate);
        var standardCount = entries.Count(item =>
            item.Availability == GenesisCatalogAvailability.StandardIssue);
        Debug.Log(string.Format(
            "[GenesisStoreCatalogAudit] entries={0}, missing={1}, "
                + "candidates={2}, standardIssue={3}, textures={4}",
            entries.Length, missing.Length, candidateCount, standardCount,
            string.Join(";", texturePaths)));
        if (entries.Length != 12 || missing.Length != 0
            || candidateCount != 4 || standardCount != 3
            || !texturePaths.Contains(
                "Assets/OriginalGame/UI/storeUI.110482/bitmap-00004.png")
            || !texturePaths.Contains(
                "Assets/OriginalGame/UI/storeUI.110482/bitmap-00293.png"))
        {
            throw new InvalidOperationException("Recovered catalog audit failed.");
        }
    }

    [MenuItem("Genesis/UI/Audit Runtime Store Catalog")]
    public static void AuditRuntimeStoreCatalog()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            "Assets/Scenes/Zhu.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Single);
        var warehouse = GameObject.Find(
            "Canvas/RawImage 1/RawImage/RawImage 5");
        if (warehouse == null)
            throw new InvalidOperationException("Recovered warehouse panel missing.");
        var runtimeType = typeof(
            GenesisSoldierSoul.Multiplayer.GenesisNetworkClient)
            .Assembly.GetType(
                "GenesisSoldierSoul.Multiplayer.GenesisWarehouseLoadout", true);
        var host = new GameObject("CatalogAuditHost");
        var runtime = host.AddComponent(runtimeType);
        runtimeType.GetMethod(
                "CreateLoadoutPanel", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runtime, new object[] { warehouse.transform });
        runtimeType.GetMethod(
                "ShowStoreCatalog", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runtime, null);
        var loadout = warehouse.transform.Find("GenesisPrimaryLoadout");
        var lobbyRoot = GameObject.Find("Canvas/RawImage 1/RawImage");
        var catalog = lobbyRoot == null
            ? null
            : lobbyRoot.transform.Find("GenesisRecoveredStoreCatalog");
        var cards = catalog == null
            ? new Transform[0]
            : catalog.Cast<Transform>()
                .Where(item => item.name.StartsWith("Catalog_", StringComparison.Ordinal))
                .ToArray();
        var buttons = catalog == null
            ? new Button[0]
            : catalog.GetComponentsInChildren<Button>(true);
        var boundary = catalog == null
            ? null
            : catalog.Find("EvidenceBoundary");
        var rawImages = catalog == null
            ? new RawImage[0]
            : catalog.GetComponentsInChildren<RawImage>(true);
        warehouse.SetActive(true);
        var previewPath = RenderCatalogPreview();
        Debug.Log(string.Format(
            "[GenesisStoreCatalogRuntimeAudit] scene={0}, cards={1}, "
                + "buttons={2}, rawImages={3}, boundary={4}, preview={5}",
            scene.name, cards.Length, buttons.Length, rawImages.Length,
            boundary != null, previewPath));
        if (cards.Length != 10 || buttons.Length != 1
            || buttons[0].name != "CloseRecoveredCatalog"
            || rawImages.Length < 2 || boundary == null)
        {
            throw new InvalidOperationException(
                "Runtime catalog contains missing entries or transaction controls.");
        }
        UnityEngine.Object.DestroyImmediate(host);
        if (catalog != null)
            UnityEngine.Object.DestroyImmediate(catalog.gameObject);
        if (loadout != null)
            UnityEngine.Object.DestroyImmediate(loadout.gameObject);
    }

    private static string RenderCatalogPreview()
    {
        var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
        var cameraObject = new GameObject("StoreCatalogPreviewCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 1f);
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
            Application.dataPath, "../../recovery/store-catalog-preview.png"));
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        canvas.renderMode = previousMode;
        canvas.worldCamera = previousCamera;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        return outputPath;
    }
}
