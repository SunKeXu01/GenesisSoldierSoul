using System.Collections;
using GenesisSoldierSoul.WeaponActions;
using GenesisSoldierSoul.Catalog;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GenesisSoldierSoul.Multiplayer
{
    public static class GenesisWeaponLoadout
    {
        public const string M4A1 = "m4a1";
        public const string M16 = "m16";
        public const string Shotgun = "shotgun01";
        public const string AK74M = "ak74m";
        public const string AN94 = "an94";
        public const string M249 = "m249";
        public const string FAMAS = "famas";
        public const string MicroGalilBaxi = "microgalil_baxi";
        public const string Gatling = "gatling";
        public const string AUGA1 = "auga1";
        public const string AK47Ice = "ak47_bingzuan";
        public const string AWP = "awp";
        public const string Knife = "knife";
        public const string HandAxe = "axe";
        public const string Nepal = "nepal";
        // The complete Shotgun01 closure is available to the loadout once its
        // recovered root transform has been normalized by the match runtime.
        public const bool RecoveredShotgunEnabled = true;
        // AK-74M and AWP now use their recovered models with the validated
        // shared rifle hand rig and have passed runtime framing/action checks.
        public const bool ExperimentalRecoveredWeaponsEnabled = true;
        private const string PrimaryKey = "Genesis.PrimaryWeapon";
        private const string MeleeKey = "Genesis.MeleeWeapon";

        public static string Primary
        {
            get
            {
                var value = PlayerPrefs.GetString(PrimaryKey, M4A1);
                if (value == M16
                    || (RecoveredShotgunEnabled && value == Shotgun))
                    return value;
                if (ExperimentalRecoveredWeaponsEnabled
                    && (value == AK74M || value == AN94 || value == M249
                        || value == FAMAS || value == MicroGalilBaxi
                        || value == Gatling || value == AUGA1
                        || value == AK47Ice
                        || value == AWP))
                    return value;
                return M4A1;
            }
        }

        public static string DisplayName
        {
            get
            {
                if (Primary == M16)
                    return "M16";
                if (Primary == AK74M)
                    return "AK-74M";
                if (Primary == AN94)
                    return "AN94";
                if (Primary == M249)
                    return "M249";
                if (Primary == FAMAS)
                    return "FAMAS";
                if (Primary == MicroGalilBaxi)
                    return "MICRO GALIL";
                if (Primary == Gatling)
                    return "GATLING";
                if (Primary == AUGA1)
                    return "AUG A1";
                if (Primary == AK47Ice)
                    return "ICE AK47";
                if (Primary == Shotgun)
                    return "SHOTGUN 01";
                return Primary == AWP ? "AWM" : "M4A1";
            }
        }

        public static string Melee
        {
            get
            {
                var value = PlayerPrefs.GetString(MeleeKey, Knife);
                return value == HandAxe || value == Nepal ? value : Knife;
            }
        }

        public static string MeleeDisplayName
        {
            get
            {
                return Melee == HandAxe
                    ? "MILITARY AXE"
                    : Melee == Nepal ? "NEPAL KNIFE" : "KNIFE 01";
            }
        }

        public static void EquipPrimary(string weapon)
        {
            var selected = weapon == M16
                || (RecoveredShotgunEnabled && weapon == Shotgun)
                || (ExperimentalRecoveredWeaponsEnabled
                    && (weapon == AK74M || weapon == AN94 || weapon == M249
                        || weapon == FAMAS || weapon == MicroGalilBaxi
                        || weapon == Gatling || weapon == AUGA1
                        || weapon == AK47Ice
                        || weapon == AWP))
                    ? weapon
                    : M4A1;
            PlayerPrefs.SetString(PrimaryKey, selected);
            PlayerPrefs.Save();
        }

        public static void EquipMelee(string weapon)
        {
            PlayerPrefs.SetString(
                MeleeKey, weapon == HandAxe || weapon == Nepal ? weapon : Knife);
            PlayerPrefs.Save();
        }

        public static string PreviewResourcePath(string weapon)
        {
            if (weapon == M16)
                return "OriginalGame/M16";
            if (weapon == Shotgun)
            {
                return "OriginalGame/FirstPerson/RecoveredClosures/"
                    + "Shotgun01/GameObject/Shotgun01";
            }
            if (weapon == AK74M)
            {
                return "OriginalGame/FirstPerson/"
                    + "AK74MViewmodelCandidate";
            }
            if (weapon == AN94)
                return "OriginalGame/FirstPerson/AN94ViewmodelCandidate";
            if (weapon == M249)
                return "OriginalGame/M249";
            if (weapon == FAMAS)
                return "OriginalGame/FAMAS";
            if (weapon == MicroGalilBaxi)
                return "OriginalGame/MicroGalilBaxi";
            if (weapon == Gatling)
                return "OriginalGame/Gatling";
            if (weapon == AUGA1)
                return "OriginalGame/AUGA1";
            if (weapon == AK47Ice)
                return "OriginalGame/AK47Ice";
            if (weapon == AWP)
            {
                return "OriginalGame/FirstPerson/"
                    + "AWPViewmodelCandidate";
            }
            return "OriginalGame/M4A1";
        }

        public static string FirstPersonResourcePath(string weapon)
        {
            if (weapon == M16)
                return "OriginalGame/FirstPerson/M16ViewmodelCandidate";
            if (weapon == Shotgun)
            {
                return "OriginalGame/FirstPerson/RecoveredClosures/"
                    + "Shotgun01/GameObject/Shotgun01";
            }
            if (weapon == M4A1)
                return "OriginalGame/FirstPerson/M4A1Viewmodel";
            if (weapon == AK74M)
            {
                return "OriginalGame/FirstPerson/"
                    + "AK74MViewmodelCandidate";
            }
            if (weapon == AN94)
                return "OriginalGame/FirstPerson/AN94ViewmodelCandidate";
            if (weapon == M249)
                return "OriginalGame/M249";
            if (weapon == FAMAS)
                return "OriginalGame/FAMAS";
            if (weapon == MicroGalilBaxi)
                return "OriginalGame/MicroGalilBaxi";
            if (weapon == Gatling)
                return "OriginalGame/Gatling";
            if (weapon == AUGA1)
                return "OriginalGame/AUGA1";
            if (weapon == AK47Ice)
                return "OriginalGame/AK47Ice";
            if (weapon == AWP)
            {
                return "OriginalGame/FirstPerson/"
                    + "AWPViewmodelCandidate";
            }
            return "OriginalGame/FirstPerson/AssaultRifle01";
        }

        public static int MagazineSize
        {
            get { return GenesisCombatRules.Profile(Primary).MagazineSize; }
        }

        public static int StartingReserve
        {
            get { return GenesisCombatRules.Profile(Primary).StartingReserve; }
        }

        public static float FireInterval
        {
            get { return GenesisCombatRules.Profile(Primary).FireInterval; }
        }

        public static float ReloadDuration
        {
            get { return GenesisCombatRules.Profile(Primary).ReloadDuration; }
        }
    }

    /// <summary>
    /// Adds live equipment selection to the recovered warehouse panel while
    /// retaining the archived character, tabs, item card and background art.
    /// </summary>
    internal sealed class GenesisWarehouseLoadout : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private const string LobbyScene = "Zhu";
        private const string WarehouseButtonPath =
            "Canvas/RawImage 1/RawImage/Button 4";
        private const string WarehousePanelPath =
            "Canvas/RawImage 1/RawImage/RawImage 5";
        private const string StoreButtonPath =
            "Canvas/RawImage 1/RawImage/Button 7";
        private const string LobbyRootPath =
            "Canvas/RawImage 1/RawImage";

        private GameObject loadoutPanel;
        private Text selectionText;
        private Image m4Background;
        private Image m16Background;
        private Image shotgunBackground;
        private Image akBackground;
        private Image awpBackground;
        private Image anBackground;
        private Image m249Background;
        private Image famasBackground;
        private Image microGalilBackground;
        private Image gatlingBackground;
        private Image augA1Background;
        private Image ak47IceBackground;
        private Image meleeBackground;
        private GameObject previewRoot;
        private GameObject previewModel;
        private Camera previewCamera;
        private RenderTexture previewTexture;
        private RawImage previewImage;
        private GameObject storeCatalogPanel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            ApplyDiagnosticUrlSelection();
            var instance = new GameObject("GenesisWarehouseLoadout");
            DontDestroyOnLoad(instance);
            instance.AddComponent<GenesisWarehouseLoadout>();
        }

        private static void ApplyDiagnosticUrlSelection()
        {
            if (!Debug.isDebugBuild)
                return;
            var url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
                return;
            var lower = url.ToLowerInvariant();
            if (lower.Contains("primary=shotgun01"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.Shotgun);
            else if (lower.Contains("primary=ak74m"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.AK74M);
            else if (lower.Contains("primary=an94"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.AN94);
            else if (lower.Contains("primary=m249"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.M249);
            else if (lower.Contains("primary=famas"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.FAMAS);
            else if (lower.Contains("primary=microgalil_baxi"))
                GenesisWeaponLoadout.EquipPrimary(
                    GenesisWeaponLoadout.MicroGalilBaxi);
            else if (lower.Contains("primary=gatling"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.Gatling);
            else if (lower.Contains("primary=auga1"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.AUGA1);
            else if (lower.Contains("primary=ak47_bingzuan"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.AK47Ice);
            else if (lower.Contains("primary=awp"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.AWP);
            else if (lower.Contains("primary=m16"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.M16);
            else if (lower.Contains("primary=m4a1"))
                GenesisWeaponLoadout.EquipPrimary(GenesisWeaponLoadout.M4A1);
            if (lower.Contains("melee=axe"))
                GenesisWeaponLoadout.EquipMelee(GenesisWeaponLoadout.HandAxe);
            else if (lower.Contains("melee=nepal"))
                GenesisWeaponLoadout.EquipMelee(GenesisWeaponLoadout.Nepal);
            else if (lower.Contains("melee=knife"))
                GenesisWeaponLoadout.EquipMelee(GenesisWeaponLoadout.Knife);
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
            DestroyPreview();
            loadoutPanel = null;
            storeCatalogPanel = null;
            if (scene.name == LobbyScene)
                StartCoroutine(AttachToRecoveredWarehouse());
        }

        private IEnumerator AttachToRecoveredWarehouse()
        {
            yield return null;
            yield return null;
            var buttonObject = GameObject.Find(WarehouseButtonPath);
            var warehouseButton = buttonObject == null
                ? null
                : buttonObject.GetComponent<Button>();
            if (warehouseButton == null)
            {
                Debug.LogWarning("[GenesisLoadout] Recovered warehouse button missing.");
                yield break;
            }

            warehouseButton.onClick.AddListener(delegate
            {
                StartCoroutine(ShowAfterRecoveredPanelOpens());
            });
            var storeObject = GameObject.Find(StoreButtonPath);
            var storeButton = storeObject == null
                ? null
                : storeObject.GetComponent<Button>();
            if (storeButton != null)
                storeButton.onClick.AddListener(ShowStoreCatalog);
            else
                Debug.LogWarning("[GenesisCatalog] Recovered store button missing.");

            var recoveredPanel = FindRecoveredWarehousePanel();
            if (recoveredPanel != null && recoveredPanel.activeInHierarchy)
                CreateLoadoutPanel(recoveredPanel.transform);
        }

        private IEnumerator ShowAfterRecoveredPanelOpens()
        {
            yield return null;
            var recoveredPanel = FindRecoveredWarehousePanel();
            if (recoveredPanel != null)
                CreateLoadoutPanel(recoveredPanel.transform);
        }

        private static GameObject FindRecoveredWarehousePanel()
        {
            var lobbyRoot = GameObject.Find(LobbyRootPath);
            if (lobbyRoot == null)
                return null;
            var panel = lobbyRoot.transform.Find("RawImage 5");
            return panel == null ? null : panel.gameObject;
        }

        private void CreateLoadoutPanel(Transform parent)
        {
            if (loadoutPanel != null)
            {
                RefreshSelection();
                return;
            }

            loadoutPanel = new GameObject(
                "GenesisPrimaryLoadout",
                typeof(RectTransform),
                typeof(Image));
            loadoutPanel.transform.SetParent(parent, false);
            loadoutPanel.transform.SetAsLastSibling();
            var rect = loadoutPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(66f, -12f);
            var experimental =
                GenesisWeaponLoadout.ExperimentalRecoveredWeaponsEnabled;
            rect.sizeDelta = new Vector2(520f, experimental ? 620f : 306f);
            loadoutPanel.GetComponent<Image>().color =
                new Color(0.035f, 0.045f, 0.045f, 0.94f);

            var title = CreateText(
                loadoutPanel.transform,
                "Title",
                new Vector2(0f, experimental ? 263f : 134f),
                new Vector2(500f, 28f),
                18,
                TextAnchor.MiddleCenter);
            title.text = "PRIMARY WEAPON LOADOUT";
            title.color = new Color(0.88f, 0.76f, 0.35f, 1f);

            m4Background = CreateWeaponButton(
                "M4A1",
                "30 DMG  |  600 RPM  |  ORIGINAL AUDIO",
                new Vector2(-96f, experimental ? 189f : 78f),
                GenesisWeaponLoadout.M4A1);
            m16Background = CreateWeaponButton(
                "M16",
                "27 DMG  |  700 RPM  |  ORIGINAL AUDIO",
                new Vector2(-96f, experimental ? 133f : 22f),
                GenesisWeaponLoadout.M16);
            shotgunBackground = CreateWeaponButton(
                "SHOTGUN 01",
                "65 DMG  |  PUMP ACTION  |  RECOVERED PREFAB",
                new Vector2(-96f, experimental ? 77f : -34f),
                GenesisWeaponLoadout.Shotgun);
            if (experimental)
            {
                akBackground = CreateWeaponButton(
                    "AK-74M",
                    "33 DMG  |  632 RPM  |  RECOVERED MODEL",
                    new Vector2(-96f, 21f),
                    GenesisWeaponLoadout.AK74M);
                awpBackground = CreateWeaponButton(
                    "AWM",
                    "85 DMG  |  ORIGINAL FP/TP + AUDIO  |  RMB SCOPE",
                    new Vector2(-96f, -35f),
                    GenesisWeaponLoadout.AWP);
                anBackground = CreateWeaponButton(
                    "AN94",
                    "31 DMG  |  561 RPM  |  ORIGINAL AUDIO",
                    new Vector2(-96f, -91f),
                    GenesisWeaponLoadout.AN94);
                m249Background = CreateWeaponButton(
                    "M249",
                    "28 DMG  |  100 BOX  |  ORIGINAL AUDIO",
                    new Vector2(-96f, -147f),
                    GenesisWeaponLoadout.M249);
                famasBackground = CreateWeaponButton(
                    "FAMAS",
                    "29 DMG  |  660 RPM  |  ORIGINAL AUDIO",
                    new Vector2(-96f, -203f),
                    GenesisWeaponLoadout.FAMAS);
                microGalilBackground = CreateWeaponButton(
                    "MICRO GALIL BRAZIL",
                    "18 DMG  |  800 RPM  |  ORIGINAL AUDIO",
                    new Vector2(-96f, -259f),
                    GenesisWeaponLoadout.MicroGalilBaxi);
                gatlingBackground = CreateWeaponButton(
                    "GATLING",
                    "32 DMG  |  150 BOX  |  RMB PREHEAT",
                    new Vector2(156f, -54f),
                    GenesisWeaponLoadout.Gatling,
                    true);
                augA1Background = CreateWeaponButton(
                    "AUG A1",
                    "27 DMG  |  620 RPM  |  ORIGINAL FP/TP + AUDIO  |  RMB SCOPE",
                    new Vector2(156f, -102f),
                    GenesisWeaponLoadout.AUGA1,
                    true);
                ak47IceBackground = CreateWeaponButton(
                    "ICE AK47",
                    "34 DMG  |  573 RPM  |  ORIGINAL FP/TP + AUDIO",
                    new Vector2(156f, -150f),
                    GenesisWeaponLoadout.AK47Ice,
                    true);
            }

            selectionText = CreateText(
                loadoutPanel.transform,
                "Selection",
                new Vector2(156f, experimental ? -197f : -82f),
                new Vector2(190f, 26f),
                15,
                TextAnchor.MiddleCenter);
            CreateMeleeToggle(experimental);
            CreateWeaponPreview();
            CreatePlayButton();
            CreateCatalogButton();
            RefreshSelection();
        }

        private void CreateMeleeToggle(bool experimental)
        {
            var row = new GameObject(
                "ToggleRecoveredMelee",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            row.transform.SetParent(loadoutPanel.transform, false);
            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(156f, experimental ? -226f : -48f);
            rect.sizeDelta = new Vector2(186f, 28f);
            meleeBackground = row.GetComponent<Image>();
            var label = CreateText(
                row.transform, "Label", Vector2.zero, rect.sizeDelta,
                12, TextAnchor.MiddleCenter);
            label.text = "MELEE: KNIFE / AXE / NEPAL";
            row.GetComponent<Button>().onClick.AddListener(delegate
            {
                GenesisWeaponLoadout.EquipMelee(
                    GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.HandAxe
                        ? GenesisWeaponLoadout.Nepal
                        : GenesisWeaponLoadout.Melee == GenesisWeaponLoadout.Nepal
                            ? GenesisWeaponLoadout.Knife
                            : GenesisWeaponLoadout.HandAxe);
                RefreshSelection();
            });
        }

        private void CreateCatalogButton()
        {
            var item = new GameObject(
                "OpenRecoveredCatalog",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            item.transform.SetParent(loadoutPanel.transform, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(188f,
                GenesisWeaponLoadout.ExperimentalRecoveredWeaponsEnabled
                    ? 207f
                    : 134f);
            rect.sizeDelta = new Vector2(130f, 26f);
            item.GetComponent<Image>().color =
                new Color(0.11f, 0.25f, 0.3f, 0.98f);
            var label = CreateText(
                item.transform, "Label", Vector2.zero, rect.sizeDelta,
                11, TextAnchor.MiddleCenter);
            label.text = "RESOURCE CATALOG";
            item.GetComponent<Button>().onClick.AddListener(ShowStoreCatalog);
        }

        private void ShowStoreCatalog()
        {
            if (storeCatalogPanel != null)
            {
                storeCatalogPanel.SetActive(true);
                storeCatalogPanel.transform.SetAsLastSibling();
                return;
            }
            var backdrop = Resources.Load<GameObject>(
                "OriginalGame/UI/GenesisStoreBackdrop");
            if (backdrop == null)
            {
                Debug.LogWarning("[GenesisCatalog] Recovered store backdrop missing.");
                return;
            }
            var lobbyRoot = GameObject.Find(LobbyRootPath);
            if (lobbyRoot == null)
            {
                Debug.LogWarning("[GenesisCatalog] Recovered lobby root missing.");
                return;
            }
            storeCatalogPanel = Instantiate(backdrop, lobbyRoot.transform);
            storeCatalogPanel.name = "GenesisRecoveredStoreCatalog";
            var rect = storeCatalogPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(66f, -12f);
            rect.sizeDelta = new Vector2(
                520f,
                GenesisWeaponLoadout.ExperimentalRecoveredWeaponsEnabled
                    ? 574f
                    : 306f);
            storeCatalogPanel.transform.SetAsLastSibling();

            var title = CreateText(
                storeCatalogPanel.transform, "CatalogTitle",
                new Vector2(-18f, 236f), new Vector2(410f, 26f),
                16, TextAnchor.MiddleCenter);
            title.text = "RECOVERED RESOURCE CATALOG  /  READ ONLY";
            title.color = new Color(0.62f, 0.9f, 1f, 1f);
            var entries = GenesisStoreCatalog.Build(
                GenesisWeaponLoadout.Primary,
                GenesisWeaponLoadout.ExperimentalRecoveredWeaponsEnabled);
            for (var index = 0; index < entries.Length; index += 1)
            {
                var column = index % 2;
                var row = index / 2;
                CreateCatalogCard(
                    entries[index],
                    new Vector2(column == 0 ? -128f : 96f, 190f - row * 50f));
            }
            var boundary = CreateText(
                storeCatalogPanel.transform, "EvidenceBoundary",
                new Vector2(-28f, -248f), new Vector2(390f, 22f),
                10, TextAnchor.MiddleCenter);
            boundary.text = "NO RECOVERED PRICE / BALANCE / PURCHASE DATA";
            boundary.color = new Color(0.92f, 0.7f, 0.34f, 1f);
            CreateCatalogCloseButton();
        }

        private void CreateCatalogCard(
            GenesisStoreCatalogEntry entry, Vector2 position)
        {
            var item = new GameObject(
                "Catalog_" + entry.Id,
                typeof(RectTransform),
                typeof(Image));
            item.transform.SetParent(storeCatalogPanel.transform, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(210f, 44f);
            item.GetComponent<Image>().color = CatalogColor(entry.Availability);
            var label = CreateText(
                item.transform, "Label", Vector2.zero,
                new Vector2(196f, 40f), 13, TextAnchor.MiddleLeft);
            label.text = entry.DisplayName + "\n<size=10>"
                + AvailabilityLabel(entry.Availability) + "</size>";
        }

        private void CreateCatalogCloseButton()
        {
            var item = new GameObject(
                "CloseRecoveredCatalog",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            item.transform.SetParent(storeCatalogPanel.transform, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(205f, -248f);
            rect.sizeDelta = new Vector2(88f, 24f);
            item.GetComponent<Image>().color = new Color(0.14f, 0.3f, 0.35f, 1f);
            var label = CreateText(
                item.transform, "Label", Vector2.zero, rect.sizeDelta,
                11, TextAnchor.MiddleCenter);
            label.text = "BACK";
            item.GetComponent<Button>().onClick.AddListener(delegate
            {
                storeCatalogPanel.SetActive(false);
            });
        }

        private static string AvailabilityLabel(GenesisCatalogAvailability value)
        {
            if (value == GenesisCatalogAvailability.Equipped)
                return "EQUIPPED / RESOURCE VERIFIED";
            if (value == GenesisCatalogAvailability.Available)
                return "AVAILABLE IN LOADOUT";
            if (value == GenesisCatalogAvailability.StandardIssue)
                return "STANDARD ISSUE";
            return "RECOVERY CANDIDATE / LOCKED";
        }

        private static Color CatalogColor(GenesisCatalogAvailability value)
        {
            if (value == GenesisCatalogAvailability.Equipped)
                return new Color(0.16f, 0.34f, 0.2f, 0.96f);
            if (value == GenesisCatalogAvailability.Available)
                return new Color(0.1f, 0.24f, 0.3f, 0.96f);
            if (value == GenesisCatalogAvailability.StandardIssue)
                return new Color(0.19f, 0.2f, 0.22f, 0.96f);
            return new Color(0.31f, 0.2f, 0.08f, 0.96f);
        }

        private void CreatePlayButton()
        {
            var row = new GameObject(
                "EnterFreeMode",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            row.transform.SetParent(loadoutPanel.transform, false);
            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(
                156f,
                GenesisWeaponLoadout.ExperimentalRecoveredWeaponsEnabled
                    ? -264f
                    : -121f);
            rect.sizeDelta = new Vector2(186f, 34f);
            row.GetComponent<Image>().color =
                new Color(0.42f, 0.31f, 0.08f, 0.98f);
            var label = CreateText(
                row.transform,
                "Label",
                Vector2.zero,
                rect.sizeDelta,
                15,
                TextAnchor.MiddleCenter);
            label.text = "ENTER FREE MODE";
            row.GetComponent<Button>().onClick.AddListener(delegate
            {
                GenesisLobbySession.Clear();
                SceneManager.LoadScene("Ziyou");
            });
        }

        private Image CreateWeaponButton(
            string weaponName,
            string stats,
            Vector2 position,
            string weaponId,
            bool compact = false)
        {
            var row = new GameObject(
                weaponName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            row.transform.SetParent(loadoutPanel.transform, false);
            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = compact
                ? new Vector2(186f, 42f)
                : new Vector2(306f, 48f);

            var label = CreateText(
                row.transform,
                "Label",
                Vector2.zero,
                compact ? new Vector2(174f, 38f) : new Vector2(288f, 42f),
                compact ? 12 : 15,
                TextAnchor.MiddleLeft);
            label.text = weaponName + "\n<size=11>" + stats + "</size>";
            var image = row.GetComponent<Image>();
            row.GetComponent<Button>().onClick.AddListener(delegate
            {
                GenesisWeaponLoadout.EquipPrimary(weaponId);
                RefreshSelection();
            });
            return image;
        }

        private void CreateWeaponPreview()
        {
            previewTexture = new RenderTexture(384, 384, 16)
            {
                name = "GenesisWarehouseWeaponPreview",
                antiAliasing = 2,
            };
            previewTexture.Create();

            var imageObject = new GameObject(
                "WeaponPreview", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(loadoutPanel.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(156f, 70f);
            rect.sizeDelta = new Vector2(184f, 184f);
            previewImage = imageObject.GetComponent<RawImage>();
            previewImage.texture = previewTexture;
            previewImage.color = Color.white;
            previewImage.raycastTarget = false;

            previewRoot = new GameObject("GenesisWarehousePreviewStage");
            previewRoot.transform.SetParent(transform, false);
            previewRoot.transform.position = new Vector3(0f, -1000f, 0f);
            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(previewRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0.08f, 3.2f);
            cameraObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.015f, 0.025f, 0.025f, 1f);
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 1.05f;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.farClipPlane = 8f;
            previewCamera.targetTexture = previewTexture;

            var lightObject = new GameObject("PreviewKeyLight");
            lightObject.transform.SetParent(previewRoot.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(35f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.94f, 0.82f);
        }

        private void RefreshWeaponPreview(string weaponId)
        {
            if (previewRoot == null)
                return;
            if (previewModel != null)
                Destroy(previewModel);
            var prefab = Resources.Load<GameObject>(
                GenesisWeaponLoadout.PreviewResourcePath(weaponId));
            if (prefab == null)
            {
                Debug.LogWarning("[GenesisLoadout] Weapon preview prefab missing: " + weaponId);
                return;
            }

            previewModel = Instantiate(prefab, previewRoot.transform);
            previewModel.name = "Preview_" + weaponId;
            SetLayerRecursively(previewModel.transform, PreviewLayer);
            foreach (var behaviour in previewModel.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (var collider in previewModel.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var audioSource in previewModel.GetComponentsInChildren<AudioSource>(true))
                audioSource.enabled = false;

            var renderers = previewModel.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index += 1)
                bounds.Encapsulate(renderers[index].bounds);
            var longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > 0.001f)
                previewModel.transform.localScale *= 1.7f / longest;
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index += 1)
                bounds.Encapsulate(renderers[index].bounds);
            previewModel.transform.position += previewRoot.transform.position - bounds.center;
            previewModel.transform.localRotation = Quaternion.Euler(12f, -48f, 0f);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
                SetLayerRecursively(child, layer);
        }

        private void Update()
        {
            if (previewModel != null)
                previewModel.transform.Rotate(0f, 18f * Time.unscaledDeltaTime, 0f, Space.World);
        }

        private void DestroyPreview()
        {
            if (previewRoot != null)
                Destroy(previewRoot);
            previewRoot = null;
            previewModel = null;
            previewCamera = null;
            previewImage = null;
            if (previewTexture != null)
            {
                previewTexture.Release();
                Destroy(previewTexture);
                previewTexture = null;
            }
        }

        private void RefreshSelection()
        {
            var selected = GenesisWeaponLoadout.Primary;
            var equipped = new Color(0.18f, 0.34f, 0.18f, 0.98f);
            var available = new Color(0.13f, 0.16f, 0.15f, 0.98f);
            if (m4Background != null)
                m4Background.color =
                    selected == GenesisWeaponLoadout.M4A1 ? equipped : available;
            if (m16Background != null)
                m16Background.color =
                    selected == GenesisWeaponLoadout.M16 ? equipped : available;
            if (shotgunBackground != null)
                shotgunBackground.color =
                    selected == GenesisWeaponLoadout.Shotgun
                        ? equipped
                        : available;
            if (akBackground != null)
                akBackground.color =
                    selected == GenesisWeaponLoadout.AK74M ? equipped : available;
            if (awpBackground != null)
                awpBackground.color =
                    selected == GenesisWeaponLoadout.AWP ? equipped : available;
            if (anBackground != null)
                anBackground.color =
                    selected == GenesisWeaponLoadout.AN94 ? equipped : available;
            if (m249Background != null)
                m249Background.color =
                    selected == GenesisWeaponLoadout.M249 ? equipped : available;
            if (famasBackground != null)
                famasBackground.color =
                    selected == GenesisWeaponLoadout.FAMAS ? equipped : available;
            if (microGalilBackground != null)
                microGalilBackground.color =
                    selected == GenesisWeaponLoadout.MicroGalilBaxi
                        ? equipped
                        : available;
            if (gatlingBackground != null)
                gatlingBackground.color =
                    selected == GenesisWeaponLoadout.Gatling
                        ? equipped
                        : available;
            if (augA1Background != null)
                augA1Background.color =
                    selected == GenesisWeaponLoadout.AUGA1
                        ? equipped
                        : available;
            if (ak47IceBackground != null)
                ak47IceBackground.color =
                    selected == GenesisWeaponLoadout.AK47Ice
                        ? equipped
                        : available;
            if (meleeBackground != null)
                meleeBackground.color = GenesisWeaponLoadout.Melee
                        != GenesisWeaponLoadout.Knife
                    ? equipped
                    : available;
            if (selectionText != null)
                selectionText.text =
                    GenesisWeaponLoadout.DisplayName + "  /  "
                    + GenesisWeaponLoadout.MeleeDisplayName;
            RefreshWeaponPreview(selected);
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
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = new Color(0.88f, 0.92f, 0.86f, 1f);
            text.raycastTarget = false;
            text.supportRichText = true;
            return text;
        }
    }
}
