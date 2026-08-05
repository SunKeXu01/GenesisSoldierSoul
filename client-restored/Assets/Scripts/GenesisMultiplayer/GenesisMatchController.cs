using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Linq;
using GenesisSoldierSoul.WeaponActions;
using GenesisSoldierSoul.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Reconnects the recovered maps' original HUD, weapon models and audio to a
    /// small but complete browser-playable match loop.
    /// </summary>
    public sealed class GenesisMatchController : MonoBehaviour
    {
        private const int PistolMagazineSize = 12;
        private const int PistolStartingReserve = 48;
        private const float PistolInterval = 0.25f;
        private const float KnifeInterval = 0.5f;
        private const float PistolReloadDuration = 1.45f;

        private Transform player;
        private Camera viewCamera;
        private Camera weaponCamera;
        private float lastViewmodelAspect = -1f;
        private GenesisNetworkClient network;
        private GenesisTrainingArena trainingArena;
        private PlayerMovement movement;
        private Music1 jumping;
        private GenesisFpsMotor fpsMotor;
        private MouseLook mouseLook;
        private RawImage knifeOverlay;
        private RawImage crosshair;
        private RawImage radar;
        private RectTransform radarOverlay;
        private Image radarLocalMarker;
        private Image radarHeadingNeedle;
        private readonly List<Image> radarContacts = new List<Image>();
        private GenesisPlayerState[] radarRoster = new GenesisPlayerState[0];
        private RawImage weaponIcon;
        private RawImage scopeOverlay;
        private Texture2D rifleIconTexture;
        private Texture2D pistolIconTexture;
        private Texture2D knifeIconTexture;
        private Texture2D grenadeIconTexture;
        private GameObject rifle;
        private Animation rifleAnimation;
        private bool usesRecoveredFirstPersonRig;
        private Transform rifleMuzzle;
        private Vector3 rifleRestPosition;
        private Quaternion rifleRestRotation;
        private GameObject pistol;
        private Animation pistolAnimation;
        private bool usesRecoveredPistolRig;
        private bool usesRecoveredM9Viewmodel;
        private Transform pistolMuzzle;
        private Vector3 pistolRestPosition;
        private Quaternion pistolRestRotation;
        private GameObject knife;
        private Animation knifeAnimation;
        private Transform knifeRigTransform;
        private Renderer[] knifeRenderers;
        private Vector3 knifeRestPosition;
        private Quaternion knifeRestRotation;
        private GameObject grenade;
        private Animation grenadeAnimation;
        private Vector3 grenadeNormalizationOffset;
        private Vector3 grenadeRestPosition;
        private Quaternion grenadeRestRotation;
        private int grenadeCount = 1;
        private bool throwingGrenade;
        private readonly Dictionary<string, GenesisGrenadeProjectile>
            activeGrenades =
                new Dictionary<string, GenesisGrenadeProjectile>();
        private AudioSource weaponAudio;
        private AudioClip pistolFireAudio;
        private AudioClip pistolReloadAudio;
        private AudioClip pistolDeployAudio;
        private AudioClip rifleFireAudio;
        private AudioClip rifleReloadAudio;
        private AudioClip rifleDeployAudio;
        private AudioClip slashAudio;
        private AudioClip hitAudio;
        private AudioClip harmedAudio;
        private AudioClip deathAudio;
        private AudioClip stageStartAudio;
        private AudioClip roundWinAudio;
        private AudioClip headshotAnnouncerAudio;
        private AudioClip doubleKillAudio;
        private AudioClip tripleKillAudio;
        private AudioClip multiKillAudio;
        private AudioClip knifeKillAudio;
        private AudioClip grenadeThrowAudio;
        private AudioClip grenadeExplosionAudio;
        private Text healthText;
        private Text armorText;
        private Text ammoText;
        private Text scoreText;
        private Text statusText;
        private Text controlsText;
        private Text mapText;
        private Text scoreboardText;
        private Text killFeedText;
        private Image healthPanel;
        private Image ammoPanel;
        private Image scorePanel;
        private Image radarCaptionPanel;
        private Image scoreboardPanel;
        private Image killFeedPanel;
        private Image weaponBarPanel;
        private RectTransform healthFillRect;
        private RectTransform armorFillRect;
        private readonly Text[] weaponSlotLabels = new Text[4];
        private Image damageVignette;
        private int pistolMagazine = PistolMagazineSize;
        private int pistolReserve = PistolStartingReserve;
        private int rifleMagazine = 30;
        private int rifleReserve = 90;
        private int rifleMagazineSize = 30;
        private int health = 100;
        private int armor;
        private int kills;
        private int deaths;
        private bool alive = true;
        private bool roundOver;
        private bool reloading;
        private bool switchingWeapon;
        private readonly GenesisWeaponActionTracker weaponActions =
            new GenesisWeaponActionTracker();
        private readonly GenesisWeaponSelectionTracker weaponSelection =
            new GenesisWeaponSelectionTracker();
        private GenesisWeaponActionState weaponAction
        {
            get { return weaponActions.State; }
        }
        private int weaponActionRevision
        {
            get { return weaponActions.Revision; }
        }
        private WeaponSlot selectedWeapon
        {
            get { return weaponSelection.Current; }
            set { weaponSelection.Select(value); }
        }
        private float nextAttackAt;
        private int shotSequence;
        private float recoil;
        private float knifeSwingStartedAt = -10f;
        private float weaponTransition;
        private float reloadMotion;
        private float locomotionPhase;
        private float sprintBlend;
        private float airborneViewBlend;
        private Coroutine switchRoutine;
        private Coroutine reloadRoutine;
        private float hitMarkerUntil;
        private float roundSeconds = 180f;
        private float returnAt;
        private float browserPitch;
        private Vector3 previousBrowserMousePosition;
        private bool hasBrowserMousePosition;
        private long respawnAt;
        private long protectedUntil;
        private long lastServerTime;
        private float serverStateReceivedAt;
        private float killFeedUntil;
        private float scoreboardVisibleUntil;
        private readonly List<string> killFeedLines = new List<string>();
        private float damageVignetteAlpha;
        private float lastHarmedSoundAt;
        private float lastTrainingKillAt;
        private int trainingKillChain;
        private float lastNetworkKillAt;
        private int networkKillChain;
        private float trainingRespawnAt;
        private Vector3 trainingSpawnPosition;
        private string nextMapScene;
        private string rifleWeaponId = GenesisWeaponLoadout.M4A1;
        private string rifleDisplayName = "M4A1";
        private float rifleInterval = 0.1f;
        private float rifleReloadDuration = 2.1f;
        private float defaultViewFieldOfView = 60f;
        private Vector3 cameraRestPosition;
        private bool scoped;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern float GenesisConsumeMouseDeltaX();

        [DllImport("__Internal")]
        private static extern float GenesisConsumeMouseDeltaY();
#endif

        public void Configure(
            Transform playerTransform,
            Camera camera,
            GenesisNetworkClient networkClient)
        {
            player = playerTransform;
            viewCamera = camera;
            network = networkClient;
        }

        private void OnEnable()
        {
            StartCoroutine(InitializeAfterRecoveredLoading());
        }

        private IEnumerator InitializeAfterRecoveredLoading()
        {
            yield return null;
            if (player == null)
                player = transform;
            if (viewCamera == null)
                viewCamera = player.GetComponentInChildren<Camera>(true);
            if (network == null)
                network = player.GetComponent<GenesisNetworkClient>();
            trainingArena = player.GetComponent<GenesisTrainingArena>();
            trainingSpawnPosition = player.position;

            movement = player.GetComponent<PlayerMovement>();
            jumping = player.GetComponent<Music1>();
            if (movement != null)
                movement.enabled = false;
            if (jumping != null)
                jumping.enabled = false;
            fpsMotor = player.GetComponent<GenesisFpsMotor>();
            if (fpsMotor == null)
                fpsMotor = player.gameObject.AddComponent<GenesisFpsMotor>();
            CharacterController playerController =
                player.GetComponent<CharacterController>();
            if (playerController != null)
            {
                // The recovered controller used radius 0.2 and skin width 0.2.
                // With the archived 0.5 player scale that capsule was too narrow
                // for map seams and its skin was as wide as the capsule itself.
                playerController.radius = Mathf.Max(
                    playerController.radius, 0.6f);
                playerController.skinWidth = Mathf.Min(
                    0.06f, playerController.radius * 0.1f);
                playerController.minMoveDistance = 0f;
                playerController.enableOverlapRecovery = true;
            }
            mouseLook = viewCamera == null
                ? null
                : viewCamera.GetComponent<MouseLook>();
            if (viewCamera != null)
            {
                cameraRestPosition = viewCamera.transform.localPosition;
#if UNITY_WEBGL && !UNITY_EDITOR
                // Several archived scenes persist a steep downward camera pitch.
                // Starting WebGL from that angle hides nearby players behind the
                // lower edge of the viewport and makes normal mouse-look feel
                // broken until a large corrective movement is received.
                browserPitch = 0f;
                viewCamera.transform.localRotation = Quaternion.identity;
#else
                browserPitch = NormalizeAngle(viewCamera.transform.localEulerAngles.x);
#endif
                Rigidbody cameraBody = viewCamera.GetComponent<Rigidbody>();
                if (cameraBody != null)
                {
                    // A dynamic rigidbody on a child camera receives tiny physics
                    // corrections while the player controller moves, which appears
                    // as first-person vertical shaking.
                    cameraBody.velocity = Vector3.zero;
                    cameraBody.angularVelocity = Vector3.zero;
                    cameraBody.isKinematic = true;
                    cameraBody.detectCollisions = false;
                }
            }
            previousBrowserMousePosition = Input.mousePosition;
            hasBrowserMousePosition = true;
            rifleWeaponId = GenesisWeaponLoadout.Primary;
            rifleDisplayName = GenesisWeaponLoadout.DisplayName;
            rifleInterval = GenesisWeaponLoadout.FireInterval;
            rifleReloadDuration = GenesisWeaponLoadout.ReloadDuration;
            rifleMagazineSize = GenesisWeaponLoadout.MagazineSize;
            rifleMagazine = rifleMagazineSize;
            rifleReserve = GenesisWeaponLoadout.StartingReserve;
            if (viewCamera != null)
                defaultViewFieldOfView = viewCamera.fieldOfView;

            EnsureRecoveredMapVisibility();
            EnsureRecoveredEnvironment();
            DisableRecoveredConflicts();
            FindRecoveredHud();
            CreateDynamicHudText();
            CreateWeaponCamera();
            CreateRecoveredRifle();
            CreateRecoveredPistol();
            CreateRecoveredKnife();
            CreateRecoveredGrenade();
            ApplyViewmodelFraming(true);
            CreateAudio();
            SubscribeNetwork();
            SetWeapon(WeaponSlot.Rifle, false);
            SetPlayerControl(true);
            if (trainingArena != null
                && weaponAudio != null
                && stageStartAudio != null)
                StartCoroutine(PlayOneShotWhenLoaded(
                    stageStartAudio, 0.72f));
            if (trainingArena != null && mapText != null)
                mapText.text = "TRAINING  PYRAMID    TARGETS 4";
            UpdateHud();
            Cursor.visible = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser mouse-look already consumes canvas-relative movement.
            // Requesting Pointer Lock from background QA/game tabs produces
            // Chromium UnknownError entries and is unnecessary for this path.
            Cursor.lockState = CursorLockMode.None;
#else
            Cursor.lockState = CursorLockMode.Locked;
#endif
            Debug.Log(
                "[GenesisMatch] 恢复地图对战闭环已初始化: "
                + SceneManager.GetActiveScene().name);
        }

        private static void EnsureRecoveredMapVisibility()
        {
            var activeLights = FindObjectsOfType<Light>()
                .Any(light => light.enabled && light.gameObject.activeInHierarchy);
            if (activeLights)
                return;

            // Some recovered maps only referenced baked lighting data that was
            // absent from the archive. Preserve their original materials and
            // geometry, but provide neutral runtime illumination so the assets
            // remain visible in WebGL.
            RenderSettings.ambientMode =
                UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.48f, 0.48f);

            var lightObject = new GameObject("Recovered Map Visibility Light");
            var recoveredLight = lightObject.AddComponent<Light>();
            recoveredLight.type = LightType.Directional;
            recoveredLight.color = Color.white;
            recoveredLight.intensity = 1.05f;
            recoveredLight.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        private void EnsureRecoveredEnvironment()
        {
            if (viewCamera == null)
                return;

            // Several exported cameras retained a custom skybox material that
            // points at an obsolete shader. The scene RenderSettings already
            // reference the recovered six-sided skybox, so make the camera use
            // that authoritative material instead of rendering a flat dark sky.
            var recoveredSkybox = RenderSettings.skybox;
            if (recoveredSkybox != null
                && recoveredSkybox.shader != null
                && recoveredSkybox.shader.isSupported)
            {
                var cameraSkybox = viewCamera.GetComponent<Skybox>();
                if (cameraSkybox == null)
                    cameraSkybox = viewCamera.gameObject.AddComponent<Skybox>();
                cameraSkybox.material = recoveredSkybox;
                cameraSkybox.enabled = true;
                viewCamera.clearFlags = CameraClearFlags.Skybox;
                Debug.Log(
                    "[GenesisEnvironment] Skybox=" + recoveredSkybox.name
                    + " shader=" + recoveredSkybox.shader.name);
            }

            RenderSettings.ambientMode =
                UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.46f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.17f);
            RenderSettings.ambientIntensity = 1.08f;
            RenderSettings.reflectionIntensity = 0.72f;
        }

        private void OnDestroy()
        {
            if (network == null)
                return;
            network.ConnectionChanged -= OnConnectionChanged;
            network.LocalStateChanged -= OnLocalStateChanged;
            network.HitConfirmed -= OnHitConfirmed;
            network.RosterChanged -= OnRosterChanged;
            network.PlayerKilled -= OnPlayerKilled;
            network.GrenadeThrown -= OnGrenadeThrown;
            network.GrenadeExploded -= OnGrenadeExploded;
        }

        private void Update()
        {
            if (healthText == null)
                return;

            UpdateRadarPresentation();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Time.timeScale = 1f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                GenesisLobbySession.Clear();
                SceneManager.LoadScene("Ziyou1");
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            if (Input.GetMouseButtonDown(0)
                && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                return;
            }
#endif

            roundSeconds = Mathf.Max(0f, roundSeconds - Time.unscaledDeltaTime);
            if (!roundOver && roundSeconds <= 0f)
                FinishRound();
            if (roundOver && returnAt > 0f && Time.unscaledTime >= returnAt)
            {
                if (nextMapScene == "Ziyou1")
                {
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                    GenesisLobbySession.Clear();
                }
                SceneManager.LoadScene(nextMapScene);
                return;
            }

            if (!roundOver && alive)
            {
                UpdateBrowserMouseLook();
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    SetWeapon(WeaponSlot.Rifle, true);
                if (Input.GetKeyDown(KeyCode.Alpha2))
                    SetWeapon(WeaponSlot.Pistol, true);
                if (Input.GetKeyDown(KeyCode.Alpha3))
                    SetWeapon(WeaponSlot.Knife, true);
                if (Input.GetKeyDown(KeyCode.Alpha4) && grenadeCount > 0)
                    SetWeapon(WeaponSlot.Grenade, true);
                if (selectedWeapon != WeaponSlot.Knife
                    && selectedWeapon != WeaponSlot.Grenade
                    && Input.GetKeyDown(KeyCode.R))
                    BeginReload();
                if (rifleWeaponId == GenesisWeaponLoadout.AWP
                    && selectedWeapon == WeaponSlot.Rifle
                    && Input.GetMouseButtonDown(1))
                    scoped = !scoped;
                var usesSinglePressAttack =
                    ((rifleWeaponId == GenesisWeaponLoadout.AWP
                            || rifleWeaponId == GenesisWeaponLoadout.Shotgun)
                        && selectedWeapon == WeaponSlot.Rifle)
                    || selectedWeapon == WeaponSlot.Grenade;
                var attackPressed = usesSinglePressAttack
                    ? Input.GetMouseButtonDown(0)
                    : Input.GetMouseButton(0);
                if (attackPressed && Time.time >= nextAttackAt)
                    Attack();
            }

            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 7f);
            ApplyViewmodelFraming(false);
            UpdateScopePresentation();

            UpdateCrosshairPresentation();
            if (Input.GetKeyDown(KeyCode.Tab))
                scoreboardVisibleUntil = Time.unscaledTime + 0.9f;
            if (scoreboardPanel != null)
                scoreboardPanel.gameObject.SetActive(
                    Input.GetKey(KeyCode.Tab)
                    || Time.unscaledTime < scoreboardVisibleUntil);
            if (killFeedText != null
                && killFeedLines.Count > 0
                && Time.unscaledTime >= killFeedUntil)
            {
                killFeedLines.Clear();
                killFeedText.text = string.Empty;
                if (killFeedPanel != null)
                    killFeedPanel.gameObject.SetActive(false);
            }
            damageVignetteAlpha = Mathf.MoveTowards(
                damageVignetteAlpha, 0f, Time.unscaledDeltaTime * 0.8f);
            if (damageVignette != null)
                damageVignette.color =
                    new Color(0.65f, 0f, 0f, damageVignetteAlpha);
            UpdateHud();
        }

        private void UpdateCrosshairPresentation()
        {
            if (crosshair == null)
                return;
            crosshair.color = Time.unscaledTime < hitMarkerUntil
                ? new Color(1f, 0.2f, 0.1f, 1f)
                : new Color(0.72f, 1f, 0.58f, 0.95f);
            var baseSize = selectedWeapon == WeaponSlot.Pistol
                ? 28f
                : selectedWeapon == WeaponSlot.Knife
                    ? 22f
                    : selectedWeapon == WeaponSlot.Grenade
                        ? 38f
                        : rifleWeaponId == GenesisWeaponLoadout.Shotgun
                            ? 46f
                            : rifleWeaponId == GenesisWeaponLoadout.AWP
                                ? 24f
                                : 34f;
            var movementSpread = Mathf.Clamp01(new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")).magnitude) * 12f;
            var firingSpread = Mathf.Clamp01(recoil / 2.5f) * 16f;
            var targetSize = baseSize + movementSpread + firingSpread;
            var rect = crosshair.rectTransform;
            rect.sizeDelta = Vector2.Lerp(
                rect.sizeDelta,
                Vector2.one * targetSize,
                1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
        }

        private void LateUpdate()
        {
            // Legacy Animation clips can write the viewmodel root after Update.
            // Apply the calibrated pose after animation sampling so firing and
            // idle clips cannot lift the cut shoulder ends into the viewport.
            UpdateFirstPersonMotion();
            if (knife != null
                && knife.activeInHierarchy
                && selectedWeapon == WeaponSlot.Knife)
                NormalizeRecoveredKnifeRig();
        }

        private void DisableRecoveredConflicts()
        {
            var delayedStart = player.GetComponent<Test4>();
            if (delayedStart != null)
                delayedStart.enabled = false;
            var stop = player.GetComponent<Stop>();
            if (stop != null)
                stop.enabled = false;
            var oldKnifeSound = player.GetComponent<Sound2>();
            if (oldKnifeSound != null)
                oldKnifeSound.enabled = false;
        }

        private void FindRecoveredHud()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var rawImage in root.GetComponentsInChildren<RawImage>(true))
                {
                    var textureName = rawImage.texture == null
                        ? string.Empty
                        : rawImage.texture.name;
                    if (textureName == "准星")
                        crosshair = rawImage;
                    else if (textureName.StartsWith("雷达"))
                        radar = rawImage;
                    else if (textureName.Contains("小刀"))
                        knifeOverlay = rawImage;
                }
            }

            if (crosshair != null)
            {
                var recoveredCrosshair =
                    Resources.Load<Texture2D>("OriginalGame/UI/crosshair");
                if (recoveredCrosshair != null)
                    crosshair.texture = recoveredCrosshair;
                var crosshairRect = crosshair.rectTransform;
                crosshairRect.anchorMin = new Vector2(0.5f, 0.5f);
                crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
                crosshairRect.pivot = new Vector2(0.5f, 0.5f);
                crosshairRect.anchoredPosition = Vector2.zero;
                crosshairRect.sizeDelta = new Vector2(34f, 34f);
                crosshair.color = new Color(0.72f, 1f, 0.58f, 0.95f);
            }
            if (radar != null)
            {
                var radarRect = radar.rectTransform;
                radarRect.anchorMin = new Vector2(0f, 1f);
                radarRect.anchorMax = new Vector2(0f, 1f);
                radarRect.pivot = new Vector2(0f, 1f);
                radarRect.anchoredPosition = new Vector2(14f, -14f);
                radarRect.sizeDelta = new Vector2(238f, 238f);
                radar.color = Color.white;
                CreateRadarOverlay();
            }
        }

        private void CreateRadarOverlay()
        {
            var overlayObject = new GameObject(
                "GenesisRadarOverlay", typeof(RectTransform));
            overlayObject.transform.SetParent(radar.transform, false);
            radarOverlay = overlayObject.GetComponent<RectTransform>();
            radarOverlay.anchorMin = new Vector2(0.5f, 0.5f);
            radarOverlay.anchorMax = new Vector2(0.5f, 0.5f);
            radarOverlay.pivot = new Vector2(0.5f, 0.5f);
            radarOverlay.anchoredPosition = Vector2.zero;
            radarOverlay.sizeDelta = new Vector2(206f, 206f);

            // A neutral outline keeps the radar readable even on recovered maps
            // whose archived radar texture is absent or very dark.
            CreateRadarLine("North", new Vector2(0f, 102f), new Vector2(206f, 2f));
            CreateRadarLine("South", new Vector2(0f, -102f), new Vector2(206f, 2f));
            CreateRadarLine("West", new Vector2(-102f, 0f), new Vector2(2f, 206f));
            CreateRadarLine("East", new Vector2(102f, 0f), new Vector2(2f, 206f));
            CreateRadarLine("NorthAxis", new Vector2(0f, 72f), new Vector2(1f, 50f));

            radarLocalMarker = CreateRadarImage(
                "LocalPlayer", Vector2.zero, new Vector2(11f, 11f),
                new Color(0.2f, 0.95f, 1f, 1f));
            radarLocalMarker.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, 45f);
            radarHeadingNeedle = CreateRadarImage(
                "LocalHeading", new Vector2(0f, 9f), new Vector2(3f, 13f),
                new Color(0.72f, 1f, 1f, 1f));

            for (var index = 0; index < 16; index += 1)
            {
                var contact = CreateRadarImage(
                    "Contact_" + index, Vector2.zero, new Vector2(7f, 7f),
                    new Color(1f, 0.28f, 0.18f, 1f));
                contact.gameObject.SetActive(false);
                radarContacts.Add(contact);
            }
        }

        private void CreateRadarLine(string name, Vector2 position, Vector2 size)
        {
            CreateRadarImage(
                name, position, size, new Color(0.55f, 0.9f, 0.78f, 0.58f));
        }

        private Image CreateRadarImage(
            string name, Vector2 position, Vector2 size, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(radarOverlay, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void UpdateRadarPresentation()
        {
            if (radarOverlay == null || player == null)
                return;

            var yaw = player.eulerAngles.y;
            if (radarLocalMarker != null)
                radarLocalMarker.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, 45f - yaw);
            if (radarHeadingNeedle != null)
            {
                radarHeadingNeedle.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, -yaw);
                var direction = new Vector2(
                    Mathf.Sin(yaw * Mathf.Deg2Rad),
                    Mathf.Cos(yaw * Mathf.Deg2Rad));
                radarHeadingNeedle.rectTransform.anchoredPosition = direction * 10f;
            }

            var contactIndex = 0;
            for (var index = 0; index < radarRoster.Length; index += 1)
            {
                var entry = radarRoster[index];
                if (entry.isLocal || !entry.alive)
                    continue;
                PlaceRadarContact(contactIndex++, entry.position);
            }

            // Training has no server roster. Its bots follow the same red enemy
            // rule as remote players in the current free-for-all mode.
            if (trainingArena != null)
            {
                var bots = FindObjectsOfType<GenesisTrainingBot>();
                for (var index = 0; index < bots.Length; index += 1)
                {
                    if (bots[index].Alive)
                        PlaceRadarContact(contactIndex++, bots[index].transform.position);
                }
            }
            for (var index = contactIndex; index < radarContacts.Count; index += 1)
                radarContacts[index].gameObject.SetActive(false);
        }

        private void PlaceRadarContact(int index, Vector3 worldPosition)
        {
            if (index >= radarContacts.Count)
                return;
            var offset = CalculateRadarOffset(
                player.position, worldPosition, 45f, 94f);
            var contact = radarContacts[index];
            contact.rectTransform.anchoredPosition = offset;
            contact.gameObject.SetActive(true);
        }

        public static Vector2 CalculateRadarOffset(
            Vector3 origin, Vector3 target, float worldRange, float pixelRadius)
        {
            var delta = target - origin;
            var range = Mathf.Max(0.01f, worldRange);
            var radius = Mathf.Max(0f, pixelRadius);
            return Vector2.ClampMagnitude(
                new Vector2(delta.x, delta.z) * (radius / range), radius);
        }

        private void CreateDynamicHudText()
        {
            Canvas canvas = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var candidate = root.GetComponent<Canvas>();
                if (candidate != null && root.activeInHierarchy)
                {
                    canvas = candidate;
                    break;
                }
            }
            if (canvas == null)
                return;
            ConfigureResponsiveHudCanvas(canvas);

            healthPanel = CreatePanel(
                canvas.transform, "GenesisHealthPanel", new Vector2(0f, 0f),
                new Vector2(20f, 18f), new Vector2(250f, 76f),
                new Color(0.025f, 0.07f, 0.095f, 0.9f));
            healthText = CreateText(
                healthPanel.transform, "GenesisHealth", new Vector2(-65f, 10f),
                new Vector2(92f, 38f), 29, TextAnchor.MiddleCenter);
            armorText = CreateText(
                healthPanel.transform, "GenesisArmor", new Vector2(62f, 10f),
                new Vector2(92f, 38f), 29, TextAnchor.MiddleCenter);
            CreateCaption(
                healthPanel.transform, "HP", new Vector2(-65f, -21f));
            CreateCaption(
                healthPanel.transform, "ARMOR", new Vector2(62f, -21f));
            healthFillRect = CreateMeter(
                healthPanel.transform, "GenesisHealthFill",
                new Vector2(-65f, -33f), new Color(0.2f, 0.86f, 0.42f, 1f));
            armorFillRect = CreateMeter(
                healthPanel.transform, "GenesisArmorFill",
                new Vector2(62f, -33f), new Color(0.25f, 0.65f, 1f, 1f));

            ammoPanel = CreatePanel(
                canvas.transform, "GenesisAmmoPanel", new Vector2(1f, 0f),
                new Vector2(-20f, 18f), new Vector2(290f, 76f),
                new Color(0.055f, 0.055f, 0.045f, 0.9f));
            ammoText = CreateText(
                ammoPanel.transform, "GenesisAmmo", new Vector2(57f, 0f),
                new Vector2(174f, 64f), 22, TextAnchor.MiddleRight);

            scorePanel = CreatePanel(
                canvas.transform, "GenesisScorePanel", new Vector2(0.5f, 1f),
                new Vector2(0f, -69f), new Vector2(250f, 30f),
                new Color(0.025f, 0.04f, 0.05f, 0.82f));
            scoreText = CreateText(
                scorePanel.transform, "GenesisScore", Vector2.zero,
                new Vector2(240f, 28f), 17, TextAnchor.MiddleCenter);
            statusText = CreateText(
                canvas.transform, "GenesisStatus", Vector2.zero,
                new Vector2(560f, 90f), 28, TextAnchor.MiddleCenter);
            controlsText = CreateText(
                canvas.transform, "GenesisControls", new Vector2(0f, 7f),
                new Vector2(460f, 20f), 11, TextAnchor.MiddleCenter);

            radarCaptionPanel = CreatePanel(
                canvas.transform, "GenesisRadarCaptionPanel",
                new Vector2(0f, 1f), new Vector2(16f, -274f),
                new Vector2(300f, 30f),
                new Color(0.02f, 0.05f, 0.065f, 0.86f));
            mapText = CreateText(
                radarCaptionPanel.transform, "GenesisMap", Vector2.zero,
                new Vector2(288f, 26f), 13, TextAnchor.MiddleCenter);

            scoreboardPanel = CreatePanel(
                canvas.transform, "GenesisScoreboardPanel",
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(620f, 430f),
                new Color(0.015f, 0.035f, 0.045f, 0.96f));
            scoreboardText = CreateText(
                scoreboardPanel.transform, "GenesisScoreboard", Vector2.zero,
                new Vector2(570f, 390f), 20, TextAnchor.MiddleCenter);

            killFeedPanel = CreatePanel(
                canvas.transform, "GenesisKillFeedPanel", new Vector2(1f, 1f),
                new Vector2(-20f, -78f), new Vector2(360f, 88f),
                new Color(0.025f, 0.035f, 0.04f, 0.68f));
            killFeedText = CreateText(
                killFeedPanel.transform, "GenesisKillFeed", Vector2.zero,
                new Vector2(340f, 74f), 16, TextAnchor.UpperRight);

            weaponBarPanel = CreatePanel(
                canvas.transform, "GenesisWeaponBarPanel", new Vector2(1f, 0f),
                new Vector2(-20f, 102f), new Vector2(290f, 34f),
                new Color(0.02f, 0.035f, 0.045f, 0.84f));
            var slotNames = new[] { "1  M4A1", "2  PISTOL", "3  KNIFE", "4  GRENADE" };
            for (var slot = 0; slot < weaponSlotLabels.Length; slot += 1)
            {
                weaponSlotLabels[slot] = CreateText(
                    weaponBarPanel.transform, "GenesisWeaponSlot" + slot,
                    new Vector2(-108f + slot * 72f, 0f),
                    new Vector2(70f, 30f), 11, TextAnchor.MiddleCenter);
                weaponSlotLabels[slot].text = slotNames[slot];
            }
            rifleIconTexture =
                Resources.Load<Texture2D>("OriginalGame/UI/rifle_icon");
            pistolIconTexture =
                Resources.Load<Texture2D>("OriginalGame/UI/pistol_icon");
            knifeIconTexture =
                Resources.Load<Texture2D>("OriginalGame/UI/knife_icon");
            grenadeIconTexture = Resources.Load<Texture2D>(
                "OriginalGame/FirstPerson/RecoveredClosures/"
                + "Grenade01/Texture2D/DI_Grenade01");
            var iconObject = new GameObject(
                "GenesisWeaponIcon",
                typeof(RectTransform),
                typeof(RawImage));
            iconObject.transform.SetParent(ammoPanel.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchoredPosition = new Vector2(-88f, 0f);
            iconRect.sizeDelta = new Vector2(72f, 62f);
            weaponIcon = iconObject.GetComponent<RawImage>();
            weaponIcon.texture = rifleIconTexture;
            weaponIcon.color = new Color(1f, 1f, 1f, 0.92f);
            weaponIcon.raycastTarget = false;
            var damageObject = new GameObject(
                "GenesisDamageVignette",
                typeof(RectTransform),
                typeof(Image));
            damageObject.transform.SetParent(canvas.transform, false);
            var damageRect = damageObject.GetComponent<RectTransform>();
            damageRect.anchorMin = Vector2.zero;
            damageRect.anchorMax = Vector2.one;
            damageRect.offsetMin = Vector2.zero;
            damageRect.offsetMax = Vector2.zero;
            damageVignette = damageObject.GetComponent<Image>();
            damageVignette.color = Color.clear;
            damageVignette.raycastTarget = false;
            damageObject.transform.SetAsLastSibling();
            CreateScopeOverlay(canvas.transform);

            SetAnchor(statusText.rectTransform, new Vector2(0.5f, 0.5f));
            SetAnchor(controlsText.rectTransform, new Vector2(0.5f, 0f));
            controlsText.text =
                "1-4 SWITCH    R RELOAD    SPACE JUMP    SHIFT RUN    TAB SCOREBOARD";
            if (rifleWeaponId == GenesisWeaponLoadout.AWP)
                controlsText.text += "   RMB SCOPE";
            controlsText.color = new Color(0.65f, 0.72f, 0.7f, 0.82f);
            controlsText.gameObject.SetActive(false);
            mapText.text = "MAP  "
                + GenesisMultiplayerBootstrap.GetDisplayName(
                    SceneManager.GetActiveScene().name);
            mapText.color = new Color(0.85f, 0.9f, 0.72f, 0.95f);
            scoreboardPanel.gameObject.SetActive(false);
            killFeedPanel.gameObject.SetActive(false);
        }

        private static void ConfigureResponsiveHudCanvas(Canvas canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        public static Vector2 CalculateHudReferenceViewport(Vector2 screenSize)
        {
            var widthScale = Mathf.Max(0.01f, screenSize.x / 1280f);
            var heightScale = Mathf.Max(0.01f, screenSize.y / 720f);
            var scale = Mathf.Pow(
                2f,
                Mathf.Lerp(
                    Mathf.Log(widthScale, 2f),
                    Mathf.Log(heightScale, 2f),
                    0.5f));
            return screenSize / scale;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor alignment)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            var shadow = gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private static Image CreatePanel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var panelObject = new GameObject(
                name, typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(parent, false);
            var rect = panelObject.GetComponent<RectTransform>();
            SetAnchor(rect, anchor);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = panelObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var outline = panelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.65f, 0.7f, 0.5f);
            outline.effectDistance = new Vector2(1f, -1f);
            return image;
        }

        private static void CreateCaption(
            Transform parent, string caption, Vector2 position)
        {
            var label = CreateText(
                parent, "GenesisCaption" + caption, position,
                new Vector2(90f, 18f), 10, TextAnchor.MiddleCenter);
            label.text = caption;
            label.color = new Color(0.64f, 0.75f, 0.75f, 0.95f);
        }

        private static RectTransform CreateMeter(
            Transform parent, string name, Vector2 position, Color fillColor)
        {
            var trackObject = new GameObject(
                name + "Track", typeof(RectTransform), typeof(Image));
            trackObject.transform.SetParent(parent, false);
            var trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.anchoredPosition = position;
            trackRect.sizeDelta = new Vector2(96f, 5f);
            var track = trackObject.GetComponent<Image>();
            track.color = new Color(0f, 0f, 0f, 0.7f);
            track.raycastTarget = false;

            var fillObject = new GameObject(
                name, typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(trackObject.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(96f, 0f);
            var fill = fillObject.GetComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;
            return fillRect;
        }

        private static void SetAnchor(RectTransform rect, Vector2 anchor)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
        }

        private void CreateScopeOverlay(Transform parent)
        {
            var scopeObject = new GameObject(
                "GenesisAwpScope",
                typeof(RectTransform),
                typeof(RawImage));
            scopeObject.transform.SetParent(parent, false);
            var rect = scopeObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            scopeOverlay = scopeObject.GetComponent<RawImage>();
            scopeOverlay.texture = CreateScopeTexture();
            scopeOverlay.color = Color.white;
            scopeOverlay.raycastTarget = false;
            scopeObject.transform.SetAsLastSibling();
            scopeObject.SetActive(false);
        }

        private static Texture2D CreateScopeTexture()
        {
            const int size = 512;
            var texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false);
            texture.name = "Genesis_AWP_Scope";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radius = size * 0.43f;
            for (var y = 0; y < size; y += 1)
            {
                for (var x = 0; x < size; x += 1)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var outside = distance > radius;
                    var reticle = !outside
                        && (Mathf.Abs(dx) <= 1.1f
                            || Mathf.Abs(dy) <= 1.1f
                            || Mathf.Abs(distance - radius) <= 1.4f);
                    pixels[y * size + x] = outside
                        ? new Color32(0, 0, 0, 255)
                        : reticle
                            ? new Color32(10, 10, 10, 230)
                            : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void CreateRecoveredPistol()
        {
            if (viewCamera == null)
                return;
            var parent = weaponCamera == null
                ? viewCamera.transform
                : weaponCamera.transform;
            // M9 keeps the archived animated pistol rig, so all ten original
            // animation clips and the FireLocator remain authoritative. The
            // archived Pistol01 remains a complete automatic fallback if the
            // promoted candidate is absent from a build.
            var prefab = Resources.Load<GameObject>(
                "OriginalGame/FirstPerson/M9Viewmodel");
            usesRecoveredM9Viewmodel = prefab != null;
            if (prefab == null)
                prefab = Resources.Load<GameObject>(
                    "OriginalGame/FirstPerson/Pistol/Pistol01");
            usesRecoveredPistolRig = prefab != null;
            if (prefab == null)
                prefab = Resources.Load<GameObject>("OriginalGame/Pistol");
            if (prefab == null)
            {
                Debug.LogWarning("[GenesisMatch] 恢复手枪资源尚未生成。");
                return;
            }
            pistol = CreateViewmodelRoot(parent, "Pistol");
            var pistolAnimationRoot = Instantiate(prefab, pistol.transform);
            pistolAnimationRoot.name = usesRecoveredM9Viewmodel
                ? "AnimationRoot_M9"
                : usesRecoveredPistolRig
                    ? "AnimationRoot_Pistol01_Fallback"
                    : "AnimationRoot_Pistol";
            pistol.transform.localPosition = usesRecoveredPistolRig
                ? new Vector3(0.08f, -0.14f, 0.12f)
                : new Vector3(0.36f, -0.48f, 0.9f);
            pistol.transform.localRotation = usesRecoveredPistolRig
                ? Quaternion.identity
                : Quaternion.Euler(-8f, -10f, 0f);
            pistol.transform.localScale = Vector3.one
                * (usesRecoveredPistolRig ? 0.55f : 0.96f);
            pistolAnimation = pistolAnimationRoot.GetComponent<Animation>();
            if (pistolAnimation != null)
            {
                pistolAnimation.Stop();
                var idle = pistolAnimation["Idle01"];
                if (idle != null)
                    idle.wrapMode = WrapMode.Loop;
                pistolAnimation.Play("Idle01");
            }
            pistolRestPosition = pistol.transform.localPosition;
            pistolRestRotation = pistol.transform.localRotation;
            pistolMuzzle = pistol
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child =>
                    child.name == "FireLocator" || child.name == "Muzzle");
            pistol.GetComponent<GenesisViewmodelRigStructure>().Configure(
                pistolAnimationRoot.transform, null, pistolMuzzle);
            foreach (var collider in pistol.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            SetLayerRecursively(pistol, 31);
        }

        private void CreateRecoveredKnife()
        {
            if (viewCamera == null)
                return;
            var handPrefab = Resources.Load<GameObject>(
                "OriginalGame/FirstPerson/Pistol/Pistol01");
            var knifeMesh = Resources.Load<Mesh>(
                "OriginalGame/FirstPerson/Meshes/Knife");
            var knifeMaterial = Resources.Load<Material>(
                "OriginalGame/FirstPerson/Materials/Knife01");
            if (handPrefab == null || knifeMesh == null || knifeMaterial == null)
                return;
            var parent = weaponCamera == null
                ? viewCamera.transform
                : weaponCamera.transform;
            knife = CreateViewmodelRoot(parent, "Knife");
            var knifeAnimationRoot = Instantiate(handPrefab, knife.transform);
            knifeAnimationRoot.name = "AnimationRoot_KnifeHands";
            knife.transform.localPosition = new Vector3(0.08f, -0.22f, 0.12f);
            knife.transform.localRotation = Quaternion.identity;
            knife.transform.localScale = Vector3.one * 0.55f;

            // The pistol rig contains an undamaged animated right arm. Strip
            // all firearm pieces and the unused left arm, then reuse that
            // skeleton as the close-combat grip.
            foreach (var firearmPart in knifeAnimationRoot.GetComponentsInChildren<MeshRenderer>(true))
                firearmPart.enabled = false;
            foreach (var arm in knifeAnimationRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                arm.enabled = arm.gameObject.name == "Right";

            var blade = new GameObject(
                "Recovered_Knife_Blade",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            blade.transform.SetParent(knifeAnimationRoot.transform, false);
            blade.GetComponent<MeshFilter>().sharedMesh = knifeMesh;
            var bladeRenderer = blade.GetComponent<MeshRenderer>();
            bladeRenderer.sharedMaterial = knifeMaterial;
            var recoveredKnifeRotation = new Quaternion(
                -0.17290609f,
                -0.7868852f,
                0.56706953f,
                -0.17131063f);
            blade.transform.localRotation =
                Quaternion.AngleAxis(-24f, Vector3.forward)
                * recoveredKnifeRotation;
            blade.transform.localScale = Vector3.one * 0.92f;
            CenterRecoveredRenderer(
                bladeRenderer,
                knife.transform,
                new Vector3(-0.02f, 0.065f, 0.61f));

            knifeRestPosition = knife.transform.localPosition;
            knifeRestRotation = knife.transform.localRotation;
            knifeAnimation = knifeAnimationRoot.GetComponent<Animation>();
            if (knifeAnimation != null)
            {
                knifeAnimation.Stop();
                var idle = knifeAnimation["Idle01"];
                if (idle != null)
                    idle.wrapMode = WrapMode.Loop;
                knifeAnimation.Play("Idle01");
            }

            // The damaged knife prefab's own hand bind pose remains excluded;
            // only its intact blade and material are combined with the good
            // pistol-arm skeleton.
            knifeRenderers = null;
            knifeRigTransform = null;
            knife.GetComponent<GenesisViewmodelRigStructure>().Configure(
                knifeAnimationRoot.transform, blade.transform);
            SetLayerRecursively(knife, 31);
            knife.SetActive(false);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(false);
        }

        private void CreateRecoveredGrenade()
        {
            if (viewCamera == null)
                return;
            var prefab = Resources.Load<GameObject>(
                "OriginalGame/FirstPerson/RecoveredClosures/"
                + "Grenade01/GameObject/Grenade01");
            if (prefab == null)
            {
                Debug.LogWarning(
                    "[GenesisMatch] Grenade01 完整依赖闭包未找到。");
                return;
            }
            var parent = weaponCamera == null
                ? viewCamera.transform
                : weaponCamera.transform;
            grenade = CreateViewmodelRoot(parent, "Grenade01");
            var grenadeAnimationRoot = Instantiate(prefab, grenade.transform);
            grenadeAnimationRoot.name = "AnimationRoot_Grenade01";
            grenade.transform.localPosition = new Vector3(0.08f, -0.31f, 0.12f);
            grenade.transform.localRotation = Quaternion.identity;
            grenade.transform.localScale = Vector3.one * 0.55f;
            grenadeAnimation = grenadeAnimationRoot.GetComponent<Animation>();
            if (grenadeAnimation != null)
            {
                grenadeAnimation.Stop();
                var idle = grenadeAnimation["Idle01"];
                if (idle != null)
                    idle.wrapMode = WrapMode.Loop;
                grenadeAnimation.Play("Idle01");
                grenadeAnimation.Sample();
            }
            NormalizeGrenadeViewmodel();
            grenade.GetComponent<GenesisViewmodelRigStructure>().Configure(
                grenadeAnimationRoot.transform);
            grenadeNormalizationOffset = grenade.transform.localPosition
                - new Vector3(0.08f, -0.31f, 0.12f);
            grenadeRestPosition = grenade.transform.localPosition;
            grenadeRestRotation = grenade.transform.localRotation;
            foreach (var collider in grenade.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            SetLayerRecursively(grenade, 31);
            grenade.SetActive(false);
        }

        private void NormalizeGrenadeViewmodel()
        {
            if (grenade == null || grenade.transform.parent == null)
                return;
            var renderers = grenade.GetComponentsInChildren<Renderer>(true)
                .Where(item => item.enabled)
                .ToArray();
            if (renderers.Length == 0)
                return;
            foreach (var skinned in
                grenade.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
            }
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index += 1)
                bounds.Encapsulate(renderers[index].bounds);
            var targetCenter = grenade.transform.parent.TransformPoint(
                new Vector3(0.18f, -0.18f, 0.72f));
            grenade.transform.position += targetCenter - bounds.center;
        }

        private static void CenterRecoveredRenderer(
            Renderer renderer,
            Transform reference,
            Vector3 localCenter)
        {
            if (renderer == null || reference == null)
                return;
            renderer.transform.position +=
                reference.TransformPoint(localCenter) - renderer.bounds.center;
        }

        private void NormalizeRecoveredKnifeRig()
        {
            if (knife == null
                || knifeRigTransform == null
                || knifeRenderers == null
                || knifeRenderers.Length == 0)
                return;
            var bounds = knifeRenderers[0].bounds;
            for (var index = 1; index < knifeRenderers.Length; index += 1)
                bounds.Encapsulate(knifeRenderers[index].bounds);
            var targetCenter = knife.transform.TransformPoint(
                new Vector3(0.12f, -0.24f, 0.62f));
            knifeRigTransform.position += targetCenter - bounds.center;
        }

        private void CreateWeaponCamera()
        {
            if (viewCamera == null)
                return;
            viewCamera.cullingMask &= ~(1 << 31);
            var cameraObject = new GameObject("Genesis Viewmodel Camera");
            cameraObject.transform.SetParent(viewCamera.transform, false);
            weaponCamera = cameraObject.AddComponent<Camera>();
            weaponCamera.clearFlags = CameraClearFlags.Depth;
            weaponCamera.cullingMask = 1 << 31;
            weaponCamera.depth = viewCamera.depth + 1f;
            // The restored camera uses Unity's forward-Z convention, unlike
            // the recovered vp_FPWeapon camera hierarchy. Keep its calibrated
            // viewmodel FOV while reusing the original weapon socket.
            weaponCamera.fieldOfView = 52f;
            weaponCamera.nearClipPlane = 0.01f;
            weaponCamera.farClipPlane = 3f;
            weaponCamera.allowHDR = false;
            weaponCamera.allowMSAA = viewCamera.allowMSAA;
        }

        private GenesisViewmodelKind RifleViewmodelKind
        {
            get
            {
                if (rifleWeaponId == GenesisWeaponLoadout.M4A1)
                    return GenesisViewmodelKind.M4A1;
                if (rifleWeaponId == GenesisWeaponLoadout.AK74M)
                    return GenesisViewmodelKind.AK74M;
                if (rifleWeaponId == GenesisWeaponLoadout.AWP)
                    return GenesisViewmodelKind.AWP;
                if (rifleWeaponId == GenesisWeaponLoadout.Shotgun)
                    return GenesisViewmodelKind.Shotgun;
                return GenesisViewmodelKind.Rifle;
            }
        }

        private void ApplyViewmodelFraming(bool force)
        {
            if (weaponCamera == null)
                return;
            var aspect = weaponCamera.aspect > 0.1f
                ? weaponCamera.aspect
                : GenesisViewmodelProfiles.ReferenceAspect;
            if (!force && Mathf.Abs(aspect - lastViewmodelAspect) < 0.001f)
                return;
            lastViewmodelAspect = aspect;

            var riflePose = GenesisViewmodelProfiles.Get(
                RifleViewmodelKind, aspect);
            ApplyViewmodelPose(
                rifle, riflePose, ref rifleRestPosition, ref rifleRestRotation);
            var pistolPose = GenesisViewmodelProfiles.Get(
                usesRecoveredM9Viewmodel
                    ? GenesisViewmodelKind.M9
                    : GenesisViewmodelKind.Pistol,
                aspect);
            ApplyViewmodelPose(
                pistol, pistolPose, ref pistolRestPosition, ref pistolRestRotation);
            var knifePose = GenesisViewmodelProfiles.Get(
                GenesisViewmodelKind.Knife, aspect);
            ApplyViewmodelPose(
                knife, knifePose, ref knifeRestPosition, ref knifeRestRotation);
            var grenadeProfile = GenesisViewmodelProfiles.Get(
                GenesisViewmodelKind.Grenade, aspect);
            // The archived grenade rig's mesh bounds are far from its root.
            // Preserve the one-time renderer normalization when the shared
            // aspect profile reapplies the root pose; otherwise the selected
            // grenade remains active but falls entirely below the viewport.
            var grenadePose = new GenesisViewmodelPose(
                grenadeProfile.Position + grenadeNormalizationOffset,
                grenadeProfile.EulerAngles,
                grenadeProfile.Scale,
                grenadeProfile.FieldOfView,
                grenadeProfile.NearClip);
            ApplyViewmodelPose(
                grenade, grenadePose, ref grenadeRestPosition,
                ref grenadeRestRotation);

            GenesisViewmodelPose activePose;
            if (selectedWeapon == WeaponSlot.Pistol)
                activePose = pistolPose;
            else if (selectedWeapon == WeaponSlot.Knife)
                activePose = knifePose;
            else if (selectedWeapon == WeaponSlot.Grenade)
                activePose = grenadePose;
            else
                activePose = riflePose;
            weaponCamera.fieldOfView = activePose.FieldOfView;
            weaponCamera.nearClipPlane = activePose.NearClip;
        }

        private static void ApplyViewmodelPose(
            GameObject viewmodel,
            GenesisViewmodelPose pose,
            ref Vector3 restPosition,
            ref Quaternion restRotation)
        {
            restPosition = pose.Position;
            restRotation = Quaternion.Euler(pose.EulerAngles);
            if (viewmodel == null)
                return;
            viewmodel.transform.localPosition = restPosition;
            viewmodel.transform.localRotation = restRotation;
            viewmodel.transform.localScale = Vector3.one * pose.Scale;
        }

        private void CreateRecoveredRifle()
        {
            if (viewCamera == null)
                return;
            var resourceName = rifleDisplayName;
            var parent = weaponCamera == null
                ? viewCamera.transform
                : weaponCamera.transform;
            var firstPersonPrefabPath =
                GenesisWeaponLoadout.FirstPersonResourcePath(rifleWeaponId);
            var firstPersonPrefab =
                Resources.Load<GameObject>(firstPersonPrefabPath);
            usesRecoveredFirstPersonRig = firstPersonPrefab != null;
            if (usesRecoveredFirstPersonRig)
            {
                rifle = CreateViewmodelRoot(parent, resourceName);
                var rifleAnimationRoot = Instantiate(
                    firstPersonPrefab, rifle.transform);
                rifleAnimationRoot.name = "AnimationRoot_" + resourceName;
                rifle.transform.localPosition =
                    rifleWeaponId == GenesisWeaponLoadout.M4A1
                        ? new Vector3(0.08f, -0.14f, 0.12f)
                        : new Vector3(0.08f, -0.31f, 0.12f);
                rifle.transform.localRotation = Quaternion.identity;
                rifle.transform.localScale = Vector3.one
                    * (rifleWeaponId == GenesisWeaponLoadout.M4A1
                        ? 0.4f
                        : 0.55f);
                rifleAnimation = rifleAnimationRoot.GetComponent<Animation>();
                if (rifleAnimation != null)
                {
                    var wield = Resources.Load<AnimationClip>(
                        "OriginalGame/FirstPerson/Animations/Wield");
                    if (wield != null && rifleAnimation.GetClip("Wield") == null)
                        rifleAnimation.AddClip(wield, "Wield");
                    rifleAnimation.Stop();
                    var idle = rifleAnimation["Idle01"];
                    if (idle != null)
                        idle.wrapMode = WrapMode.Loop;
                    rifleAnimation.Play("Idle01");
                }
                rifleMuzzle = rifle
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => child.name == "Muzzle");
                rifle.GetComponent<GenesisViewmodelRigStructure>().Configure(
                    rifleAnimationRoot.transform, null, rifleMuzzle);
            }
            else if (selectedWeapon == WeaponSlot.Knife)
            {
                rifle = new GameObject("Recovered_" + resourceName);
                rifle.transform.SetParent(parent, false);
                rifle.transform.localPosition =
                    new Vector3(0.3f, -0.28f, 0.98f);
                // The recovered M4 mesh points its muzzle down local -Z.
                rifle.transform.localRotation =
                    Quaternion.Euler(2f, 172f, -3f);
                rifle.transform.localScale = Vector3.one;
                BuildFirstPersonRifle(
                    rifle.transform,
                    rifleWeaponId == GenesisWeaponLoadout.M16);
            }
            rifleRestPosition = rifle.transform.localPosition;
            rifleRestRotation = rifle.transform.localRotation;
            foreach (var collider in rifle.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            SetLayerRecursively(rifle, 31);

            if (rifleMuzzle == null)
            {
                var muzzleObject = new GameObject("Muzzle");
                muzzleObject.transform.SetParent(rifle.transform, false);
                muzzleObject.transform.localPosition =
                    usesRecoveredFirstPersonRig
                        ? new Vector3(0f, 0f, 1.1f)
                        : new Vector3(0f, 0.02f, -0.33f);
                muzzleObject.transform.localRotation = Quaternion.identity;
                rifleMuzzle = muzzleObject.transform;
            }
            var rifleStructure = rifle.GetComponent<GenesisViewmodelRigStructure>();
            if (rifleStructure != null)
            {
                rifleStructure.Configure(
                    rifleStructure.AnimationRoot ?? rifle.transform,
                    rifleStructure.WeaponRoot,
                    rifleMuzzle);
            }
        }

        private static GameObject CreateViewmodelRoot(
            Transform parent,
            string roleName)
        {
            var root = new GameObject("ViewmodelRoot_" + roleName);
            root.transform.SetParent(parent, false);
            root.AddComponent<GenesisViewmodelRigStructure>();
            return root;
        }

        private static void BuildFirstPersonRifle(Transform root, bool isM16)
        {
            var m4Reference = Resources.Load<GameObject>("OriginalGame/M4A1");
            var pistolReference = Resources.Load<GameObject>("OriginalGame/Pistol");
            var skinSource = FindRecoveredMaterial(pistolReference, "Handgun02-2");
            if (m4Reference == null || skinSource == null)
                return;

            var model = Instantiate(m4Reference, root);
            model.name = isM16 ? "Recovered_M16_Model" : "Recovered_M4A1_Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
                var longest = Mathf.Max(
                    bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (longest > 0.0001f)
                {
                    var inheritedScale = Mathf.Max(
                        Mathf.Abs(root.lossyScale.x),
                        Mathf.Max(
                            Mathf.Abs(root.lossyScale.y),
                            Mathf.Abs(root.lossyScale.z)));
                    model.transform.localScale *=
                        (isM16 ? 0.68f : 0.62f)
                        * inheritedScale
                        / longest;
                }

                bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
                model.transform.position += root.position - bounds.center;
            }

            var skin = CreateSolidRecoveredMaterial(
                skinSource,
                "CF Gloves",
                new Color(0.12f, 0.14f, 0.13f, 1f));
            CreateViewPart(root, "RightGlove", PrimitiveType.Sphere,
                new Vector3(-0.045f, -0.13f, 0.045f),
                new Vector3(0.095f, 0.115f, 0.1f),
                new Vector3(8f, 0f, -8f), skin);
            CreateViewPart(root, "LeftGlove", PrimitiveType.Sphere,
                new Vector3(0.04f, -0.085f, -0.155f),
                new Vector3(0.09f, 0.105f, 0.115f),
                new Vector3(-8f, 0f, 8f), skin);
        }

        private static Material FindRecoveredMaterial(
            GameObject prefab, string materialName)
        {
            if (prefab == null)
                return null;
            return prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .FirstOrDefault(material =>
                    material != null && material.name == materialName);
        }

        private static Material CreateSolidRecoveredMaterial(
            Material source,
            string name,
            Color color)
        {
            var material = new Material(source)
            {
                name = name,
                color = color,
                mainTexture = null,
            };
            if (material.HasProperty("_BumpMap"))
                material.SetTexture("_BumpMap", null);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.18f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.3f);
            return material;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static GameObject CreateViewPart(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Vector3 rotation,
            Material material)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(rotation);
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            var collider = part.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            return part;
        }

        private void CreateAudio()
        {
            weaponAudio = gameObject.AddComponent<AudioSource>();
            weaponAudio.spatialBlend = 0f;
            weaponAudio.playOnAwake = false;
            pistolFireAudio = Resources.Load<AudioClip>("music/fire");
            pistolReloadAudio = Resources.Load<AudioClip>("music/reload");
            pistolDeployAudio = Resources.Load<AudioClip>("music/deploy");
            var rifleAudioPath = rifleWeaponId == GenesisWeaponLoadout.M16
                ? "OriginalGame/Audio/M16/"
                : rifleWeaponId == GenesisWeaponLoadout.Shotgun
                    ? "OriginalGame/Audio/Shotgun01/"
                    : "OriginalGame/Audio/M4A1/";
            rifleFireAudio = Resources.Load<AudioClip>(rifleAudioPath + "fire");
            rifleReloadAudio = Resources.Load<AudioClip>(rifleAudioPath + "reload");
            rifleDeployAudio = Resources.Load<AudioClip>(rifleAudioPath + "deploy");
            slashAudio = Resources.Load<AudioClip>("music/slash");
            hitAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/Genesis/bullet_hit");
            harmedAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/Genesis/harmed");
            deathAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/Genesis/die");
            stageStartAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/stage_start");
            if (stageStartAudio == null)
                stageStartAudio = Resources.Load<AudioClip>(
                    "OriginalGame/Audio/Genesis/stage_start");
            roundWinAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/round_win");
            if (roundWinAudio == null)
                roundWinAudio = Resources.Load<AudioClip>(
                    "OriginalGame/Audio/Genesis/round_win");
            headshotAnnouncerAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/headshot");
            doubleKillAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/double_kill");
            tripleKillAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/triple_kill");
            multiKillAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/multi_kill");
            knifeKillAudio =
                Resources.Load<AudioClip>("OriginalGame/Audio/CF2/knife_kill");
            grenadeThrowAudio = Resources.Load<AudioClip>(
                "OriginalGame/Audio/CF2/Grenade/fire_in_the_hole");
            grenadeExplosionAudio = Resources.Load<AudioClip>(
                "OriginalGame/Audio/CF2/Grenade/explosion");
            WarmRecoveredAudio();
        }

        private void WarmRecoveredAudio()
        {
            var clips = new[]
            {
                pistolFireAudio,
                pistolReloadAudio,
                pistolDeployAudio,
                rifleFireAudio,
                rifleReloadAudio,
                rifleDeployAudio,
                slashAudio,
                hitAudio,
                harmedAudio,
                deathAudio,
                stageStartAudio,
                roundWinAudio,
                headshotAnnouncerAudio,
                doubleKillAudio,
                tripleKillAudio,
                multiKillAudio,
                knifeKillAudio,
                grenadeThrowAudio,
                grenadeExplosionAudio,
            };
            var loaded = clips
                .Where(clip => clip != null)
                .Distinct()
                .Count(clip => clip.loadState == AudioDataLoadState.Loaded);
            var pending = clips
                .Where(clip => clip != null)
                .Distinct()
                .Count(clip => clip.loadState != AudioDataLoadState.Loaded);
            Debug.Log(
                "[GenesisAudio] Primary=" + rifleDisplayName
                + " fire="
                + (rifleFireAudio == null ? "missing" : rifleFireAudio.name)
                + " reload="
                + (rifleReloadAudio == null ? "missing" : rifleReloadAudio.name)
                + " deploy="
                + (rifleDeployAudio == null ? "missing" : rifleDeployAudio.name)
                + " loaded=" + loaded
                + " pending=" + pending);
        }

        private IEnumerator PlayOneShotWhenLoaded(AudioClip clip, float volume)
        {
            if (clip == null || weaponAudio == null)
                yield break;
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            var timeout = Time.realtimeSinceStartup + 2f;
            while (clip.loadState == AudioDataLoadState.Loading
                && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            if (clip.loadState == AudioDataLoadState.Loaded)
                weaponAudio.PlayOneShot(clip, volume);
        }

        private void PlayCombatSound(AudioClip clip, float volume = 1f)
        {
            if (clip == null || weaponAudio == null)
                return;
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                weaponAudio.PlayOneShot(clip, volume);
                return;
            }
            StartCoroutine(PlayOneShotWhenLoaded(clip, volume));
        }

        private void SubscribeNetwork()
        {
            if (network == null)
                return;
            network.ConnectionChanged += OnConnectionChanged;
            network.LocalStateChanged += OnLocalStateChanged;
            network.HitConfirmed += OnHitConfirmed;
            network.RosterChanged += OnRosterChanged;
            network.PlayerKilled += OnPlayerKilled;
            network.GrenadeThrown += OnGrenadeThrown;
            network.GrenadeExploded += OnGrenadeExploded;
        }

        private int BeginWeaponAction(GenesisWeaponActionState state)
        {
            var revision = weaponActions.Begin(state);
            reloading = state == GenesisWeaponActionState.Reloading;
            switchingWeapon = state == GenesisWeaponActionState.Holstering
                || state == GenesisWeaponActionState.Deploying
                || state == GenesisWeaponActionState.Throwing;
            throwingGrenade = state == GenesisWeaponActionState.Throwing;
            return revision;
        }

        private void CancelReloadAction()
        {
            if (reloadRoutine != null)
            {
                StopCoroutine(reloadRoutine);
                reloadRoutine = null;
            }
            reloading = false;
            reloadMotion = 0f;
        }

        private void ResetWeaponAction()
        {
            weaponActions.Reset();
            CancelReloadAction();
            switchingWeapon = false;
            throwingGrenade = false;
        }

        private void SetWeapon(WeaponSlot weapon, bool playSound)
        {
            if (weapon == WeaponSlot.Grenade && grenadeCount <= 0)
                return;
            if (playSound)
            {
                if (weapon == selectedWeapon && !switchingWeapon)
                    return;
                CancelReloadAction();
                if (switchRoutine != null)
                {
                    StopCoroutine(switchRoutine);
                    switchRoutine = null;
                }
                // A second input during holster/deploy is authoritative. If it
                // selects the weapon that is still active, restore that viewmodel
                // immediately; otherwise start a fresh transition to the newest
                // requested weapon instead of dropping the input.
                if (weapon == selectedWeapon)
                {
                    SetWeapon(weapon, false);
                    return;
                }
                switchRoutine = StartCoroutine(SwitchWeapon(weapon));
                return;
            }

            selectedWeapon = weapon;
            ApplyViewmodelFraming(true);
            if (weapon != WeaponSlot.Rifle)
                scoped = false;
            ResetWeaponAction();
            weaponTransition = 0f;
            if (rifle != null)
                rifle.SetActive(
                    weapon == WeaponSlot.Rifle && alive && !roundOver);
            if (pistol != null)
                pistol.SetActive(
                    weapon == WeaponSlot.Pistol && alive && !roundOver);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(
                    knife == null
                    && weapon == WeaponSlot.Knife
                    && alive
                    && !roundOver);
            if (knife != null)
                knife.SetActive(
                    weapon == WeaponSlot.Knife && alive && !roundOver);
            if (grenade != null)
                grenade.SetActive(
                    weapon == WeaponSlot.Grenade && alive && !roundOver);
            if (weapon == WeaponSlot.Rifle && rifleAnimation != null)
                rifleAnimation.Play("Idle01");
            if (weapon == WeaponSlot.Pistol && pistolAnimation != null)
                pistolAnimation.Play("Idle01");
            if (weapon == WeaponSlot.Grenade && grenadeAnimation != null)
                grenadeAnimation.Play("Idle01");
            var deploy = weapon == WeaponSlot.Rifle
                ? rifleDeployAudio
                : weapon == WeaponSlot.Pistol
                    ? pistolDeployAudio
                    : weapon == WeaponSlot.Grenade
                        ? rifleDeployAudio
                        : null;
            if (playSound)
                PlayCombatSound(deploy);
        }

        private IEnumerator SwitchWeapon(WeaponSlot weapon)
        {
            if (weapon == WeaponSlot.Grenade && grenadeCount <= 0)
                yield break;
            CancelReloadAction();
            var actionRevision = BeginWeaponAction(
                GenesisWeaponActionState.Holstering);
            var elapsed = 0f;
            const float holsterDuration = 0.16f;
            var holsterStart = weaponTransition;
            while (elapsed < holsterDuration)
            {
                if (actionRevision != weaponActionRevision)
                    yield break;
                elapsed += Time.deltaTime;
                weaponTransition = Mathf.SmoothStep(
                    holsterStart, 1f, elapsed / holsterDuration);
                yield return null;
            }

            selectedWeapon = weapon;
            ApplyViewmodelFraming(true);
            weaponActions.Transition(GenesisWeaponActionState.Deploying);
            if (weapon != WeaponSlot.Rifle)
                scoped = false;
            if (rifle != null)
                rifle.SetActive(weapon == WeaponSlot.Rifle && alive && !roundOver);
            if (pistol != null)
                pistol.SetActive(weapon == WeaponSlot.Pistol && alive && !roundOver);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(
                    knife == null
                    && weapon == WeaponSlot.Knife
                    && alive
                    && !roundOver);
            if (knife != null)
                knife.SetActive(
                    weapon == WeaponSlot.Knife && alive && !roundOver);
            if (grenade != null)
                grenade.SetActive(
                    weapon == WeaponSlot.Grenade && alive && !roundOver);
            if (weapon == WeaponSlot.Rifle)
                PlayRifleAnimation("Wield", 0.04f);
            else if (weapon == WeaponSlot.Pistol)
                PlayPistolAnimation("Wield", 0.04f);
            else if (weapon == WeaponSlot.Knife && knifeAnimation != null)
                knifeAnimation.Play("Idle01");
            else if (weapon == WeaponSlot.Grenade && grenadeAnimation != null)
                grenadeAnimation.Play("Idle01");
            if (network != null)
                network.RequestAction(NetworkWeaponName(weapon), "equip");
            var deploy = weapon == WeaponSlot.Rifle
                ? rifleDeployAudio
                : weapon == WeaponSlot.Pistol
                    ? pistolDeployAudio
                    : weapon == WeaponSlot.Grenade
                        ? rifleDeployAudio
                        : slashAudio;
            PlayCombatSound(deploy, 0.78f);

            elapsed = 0f;
            const float deployDuration = 0.25f;
            while (elapsed < deployDuration)
            {
                if (actionRevision != weaponActionRevision)
                    yield break;
                elapsed += Time.deltaTime;
                weaponTransition = Mathf.SmoothStep(
                    1f, 0f, elapsed / deployDuration);
                yield return null;
            }
            weaponTransition = 0f;
            weaponActions.Transition(GenesisWeaponActionState.Ready);
            switchingWeapon = false;
            switchRoutine = null;
        }

        private void UpdateFirstPersonMotion()
        {
            var moveAmount = fpsMotor == null ? 0f : fpsMotor.NormalizedSpeed;
            var moveInput = fpsMotor == null
                ? Vector2.zero
                : fpsMotor.MoveInput;
            var airborne = fpsMotor == null ? 0f : fpsMotor.AirborneBlend;
            var landing = fpsMotor == null ? 0f : fpsMotor.LandingKick;
            airborneViewBlend = Mathf.MoveTowards(
                airborneViewBlend,
                fpsMotor != null && !fpsMotor.IsGrounded ? 1f : 0f,
                Time.deltaTime * (fpsMotor != null && fpsMotor.IsGrounded
                    ? 9f
                    : 5f));
            if (fpsMotor != null && !fpsMotor.IsGrounded)
                moveAmount *= 0.3f;
            sprintBlend = Mathf.MoveTowards(
                sprintBlend,
                fpsMotor != null && fpsMotor.IsSprinting ? moveAmount : 0f,
                Time.deltaTime * 5.5f);
            locomotionPhase += Time.deltaTime * Mathf.Lerp(6f, 13f, moveAmount);
            var bob = new Vector3(
                Mathf.Sin(locomotionPhase) * 0.018f,
                -Mathf.Abs(Mathf.Cos(locomotionPhase)) * 0.014f,
                0f) * moveAmount;
            // Preserve the archived viewmodel's directional feel: strafing rolls
            // the hands into the movement and sprinting lowers the weapon instead
            // of translating the same rigid pose across the screen.
            var directionalOffset = new Vector3(
                -moveInput.x * 0.016f,
                -Mathf.Abs(moveInput.y) * 0.004f - sprintBlend * 0.035f,
                -moveInput.y * 0.008f - sprintBlend * 0.025f) * moveAmount;
            var directionalRotation = new Vector3(
                moveInput.y * 0.9f + sprintBlend * 3.2f,
                -moveInput.x * 1.4f,
                -moveInput.x * 3.4f - sprintBlend * 4.5f) * moveAmount;
            var airborneOffset = new Vector3(
                0f,
                airborne * 0.035f - airborneViewBlend * 0.025f
                    - landing * 0.055f,
                -airborneViewBlend * 0.035f + landing * 0.018f);
            var airborneRotation = new Vector3(
                -airborne * 2.8f + landing * 5.5f,
                0f,
                airborneViewBlend * -1.4f);
            if (viewCamera != null)
            {
                // The archived controller used 0.1 horizontal/vertical head-bob
                // ranges plus a short 0.1 landing bob. Use the same motion shape
                // at a restrained camera-space amplitude; the viewmodel inherits
                // it and therefore no longer appears to slide rigidly over maps.
                var cameraBob = new Vector3(
                    Mathf.Sin(locomotionPhase) * 0.022f,
                    -Mathf.Abs(Mathf.Cos(locomotionPhase * 2f)) * 0.018f,
                    0f) * moveAmount;
                cameraBob.y += airborne * 0.025f - landing * 0.075f;
                viewCamera.transform.localPosition = cameraRestPosition + cameraBob;
            }
            var transition = new Vector3(
                0.15f * weaponTransition,
                -0.58f * weaponTransition,
                -0.1f * weaponTransition);
            var reloadOffset = new Vector3(
                -0.08f * reloadMotion,
                -0.16f * reloadMotion,
                -0.04f * reloadMotion);

            if (rifle != null)
            {
                rifle.transform.localPosition = rifleRestPosition
                    + bob * (usesRecoveredFirstPersonRig ? 0.72f : 1f)
                    + directionalOffset + airborneOffset
                    + transition + reloadOffset
                    + new Vector3(0f, 0f, -recoil * 0.075f);
                rifle.transform.localRotation = rifleRestRotation
                    * Quaternion.Euler(
                        -recoil * 6f
                            + moveAmount * Mathf.Sin(locomotionPhase) * 0.8f
                            + directionalRotation.x + airborneRotation.x,
                        reloadMotion * 22f + directionalRotation.y,
                        weaponTransition * 18f
                            + reloadMotion * 34f
                            + directionalRotation.z + airborneRotation.z);
            }
            if (pistol != null)
            {
                pistol.transform.localPosition = pistolRestPosition
                    + bob * (usesRecoveredPistolRig ? 0.72f : 1.15f)
                    + directionalOffset * 1.1f + airborneOffset
                    + transition + reloadOffset
                    + new Vector3(0f, 0f, -recoil * 0.09f);
                pistol.transform.localRotation = pistolRestRotation
                    * Quaternion.Euler(
                        -recoil * 9f + directionalRotation.x
                            + airborneRotation.x,
                        reloadMotion * 26f + directionalRotation.y,
                        weaponTransition * 22f
                            + reloadMotion * 42f
                            + directionalRotation.z + airborneRotation.z);
            }
            if (knifeOverlay != null)
            {
                var rect = knifeOverlay.rectTransform;
                rect.localRotation = Quaternion.Euler(
                    0f, 0f,
                    weaponTransition * -34f
                    + recoil * -12f
                    + moveAmount * Mathf.Sin(locomotionPhase) * 1.8f);
                rect.localScale = Vector3.one
                    * Mathf.Lerp(1f, 0.86f, weaponTransition);
            }
            if (knife != null)
            {
                var knifeSwingProgress = Mathf.Clamp01(
                    (Time.time - knifeSwingStartedAt) / 0.46f);
                var knifeSwing = knifeSwingProgress < 1f
                    ? Mathf.Sin(knifeSwingProgress * Mathf.PI)
                    : 0f;
                knife.transform.localPosition = knifeRestPosition
                    + bob * 0.78f + directionalOffset + airborneOffset
                    + transition
                    + new Vector3(
                        -recoil * 0.1f - knifeSwing * 0.16f,
                        -recoil * 0.07f - knifeSwing * 0.11f,
                        -recoil * 0.055f + knifeSwing * 0.2f);
                knife.transform.localRotation = knifeRestRotation
                    * Quaternion.Euler(
                        -recoil * 42f - knifeSwing * 54f,
                        weaponTransition * 20f
                            + recoil * 28f
                            + knifeSwing * 34f,
                        weaponTransition * 18f
                            - recoil * 34f
                            - knifeSwing * 48f
                            + directionalRotation.z + airborneRotation.z);
            }
            if (grenade != null)
            {
                grenade.transform.localPosition = grenadeRestPosition
                    + bob * 0.72f + directionalOffset + airborneOffset
                    + transition;
                grenade.transform.localRotation = grenadeRestRotation
                    * Quaternion.Euler(
                        moveAmount * Mathf.Sin(locomotionPhase) * 0.7f
                            + directionalRotation.x + airborneRotation.x,
                        weaponTransition * 18f + directionalRotation.y,
                        weaponTransition * 16f + directionalRotation.z
                            + airborneRotation.z);
            }
        }

        private void UpdateScopePresentation()
        {
            var active = scoped
                && rifleWeaponId == GenesisWeaponLoadout.AWP
                && selectedWeapon == WeaponSlot.Rifle
                && alive
                && !roundOver
                && !switchingWeapon;
            if (viewCamera != null)
            {
                viewCamera.fieldOfView = Mathf.MoveTowards(
                    viewCamera.fieldOfView,
                    active
                        ? 24f
                        : defaultViewFieldOfView + sprintBlend * 3f,
                    Time.unscaledDeltaTime * 125f);
            }
            if (weaponCamera != null)
                weaponCamera.enabled = !active;
            if (scopeOverlay != null)
                scopeOverlay.gameObject.SetActive(active);
            if (crosshair != null)
                crosshair.gameObject.SetActive(!active);
        }

        private void Attack()
        {
            if (weaponAction != GenesisWeaponActionState.Ready
                && weaponAction != GenesisWeaponActionState.Firing)
                return;
            var attackWeapon = selectedWeapon == WeaponSlot.Rifle
                ? rifleWeaponId
                : selectedWeapon == WeaponSlot.Pistol
                    ? "pistol"
                    : selectedWeapon == WeaponSlot.Knife
                        ? "knife"
                        : "grenade";
            var movementAmount = Mathf.Clamp01(new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")).magnitude);
            var shotDirection = viewCamera == null
                ? Vector3.forward
                : GenesisCombatRules.ShotDirection(
                    viewCamera.transform.forward,
                    viewCamera.transform.up,
                    attackWeapon,
                    ++shotSequence,
                    movementAmount,
                    recoil,
                    scoped);
            if (selectedWeapon == WeaponSlot.Rifle)
            {
                if (rifleMagazine <= 0)
                {
                    BeginReload();
                    return;
                }
                rifleMagazine -= 1;
                nextAttackAt = Time.time + rifleInterval;
                BeginWeaponAction(GenesisWeaponActionState.Firing);
                recoil = Mathf.Min(2.5f, recoil
                    + GenesisCombatRules.Profile(rifleWeaponId).RecoilKick);
                PlayRifleAnimation("Fire", 0.03f);
                PlayCombatSound(rifleFireAudio, 0.8f);
                PlayMuzzleFlash(
                    rifleMuzzle,
                    shotDirection,
                    GenesisCombatRules.Profile(rifleWeaponId).Range);
                CreateShellEjection(rifleMuzzle, rifleWeaponId);
                CreatePredictedImpact(
                    shotDirection,
                    GenesisCombatRules.Profile(rifleWeaponId).Range);
                if (network != null)
                    network.RequestShoot(rifleWeaponId, shotDirection);
            }
            else if (selectedWeapon == WeaponSlot.Pistol)
            {
                if (pistolMagazine <= 0)
                {
                    BeginReload();
                    return;
                }
                pistolMagazine -= 1;
                nextAttackAt = Time.time + PistolInterval;
                BeginWeaponAction(GenesisWeaponActionState.Firing);
                recoil = Mathf.Min(2.5f, recoil
                    + GenesisCombatRules.Profile("pistol").RecoilKick);
                PlayPistolAnimation("Fire", 0.025f);
                PlayCombatSound(pistolFireAudio);
                PlayMuzzleFlash(
                    pistolMuzzle,
                    shotDirection,
                    GenesisCombatRules.Profile("pistol").Range);
                CreateShellEjection(pistolMuzzle, "pistol");
                CreatePredictedImpact(
                    shotDirection,
                    GenesisCombatRules.Profile("pistol").Range);
                if (network != null)
                    network.RequestShoot("pistol", shotDirection);
            }
            else if (selectedWeapon == WeaponSlot.Knife)
            {
                nextAttackAt = Time.time + KnifeInterval;
                BeginWeaponAction(GenesisWeaponActionState.Melee);
                recoil = 1f;
                knifeSwingStartedAt = Time.time;
                PlayKnifeAnimation();
                PlayCombatSound(slashAudio);
                if (network != null)
                    network.RequestShoot("knife", shotDirection);
            }
            else if (selectedWeapon == WeaponSlot.Grenade)
            {
                if (grenadeCount <= 0 || throwingGrenade)
                    return;
                StartCoroutine(ThrowGrenade());
                return;
            }

            if (trainingArena != null && viewCamera != null)
            {
                var trainingWeapon = selectedWeapon == WeaponSlot.Rifle
                    ? rifleWeaponId
                    : selectedWeapon == WeaponSlot.Pistol
                        ? "pistol"
                        : "knife";
                bool killed;
                bool headshot;
                string botName;
                if (trainingArena.TryShoot(
                        GenesisAimAlignment.ShotRay(
                            viewCamera.transform.position,
                            shotDirection),
                        trainingWeapon,
                        out killed,
                        out headshot,
                        out botName))
                {
                    RegisterTrainingHit(killed, headshot, botName);
                }
            }
        }

        private IEnumerator ThrowGrenade()
        {
            var actionRevision = BeginWeaponAction(
                GenesisWeaponActionState.Throwing);
            grenadeCount -= 1;
            nextAttackAt = Time.time + 1f;
            PlayCombatSound(grenadeThrowAudio, 0.76f);
            if (grenadeAnimation != null
                && grenadeAnimation.GetClip("Throw") != null)
            {
                var state = grenadeAnimation["Throw"];
                state.wrapMode = WrapMode.Once;
                grenadeAnimation.CrossFade("Throw", 0.04f);
            }
            yield return new WaitForSeconds(0.42f);
            if (actionRevision != weaponActionRevision)
                yield break;

            var requested = false;
            if (trainingArena != null && viewCamera != null)
            {
                var id = "training-" + Guid.NewGuid().ToString("N");
                SpawnGrenadeProjectile(
                    id,
                    viewCamera.transform.position
                        + viewCamera.transform.forward * 0.85f,
                    viewCamera.transform.forward * 10f + Vector3.up * 2.5f,
                    3f,
                    true,
                    OnTrainingGrenadeFuse);
                requested = true;
            }
            else if (network != null)
            {
                requested = network.RequestGrenade();
            }

            if (!requested)
                grenadeCount += 1;
            if (grenade != null)
                grenade.SetActive(false);
            yield return new WaitForSeconds(0.34f);
            if (actionRevision != weaponActionRevision)
                yield break;
            throwingGrenade = false;
            switchingWeapon = false;
            SetWeapon(weaponSelection.FallbackAfterGrenade(), false);
        }

        private GenesisGrenadeProjectile SpawnGrenadeProjectile(
            string id,
            Vector3 position,
            Vector3 velocity,
            float fuseSeconds,
            bool ignoreLocalPlayer,
            Action<string, Vector3> onFuse)
        {
            GenesisGrenadeProjectile existing;
            if (activeGrenades.TryGetValue(id, out existing) && existing != null)
                return existing;
            var mesh = Resources.Load<Mesh>(
                "OriginalGame/FirstPerson/RecoveredClosures/"
                + "Grenade01/Mesh/GrenadeMesh");
            var material = Resources.Load<Material>(
                "OriginalGame/FirstPerson/RecoveredClosures/"
                + "Grenade01/Material/MA_Grenade01");
            if (mesh == null || material == null)
                return null;
            var projectileObject = new GameObject(
                "Recovered_Grenade01_Projectile",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(SphereCollider),
                typeof(Rigidbody),
                typeof(GenesisGrenadeProjectile));
            projectileObject.transform.position = position;
            projectileObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            projectileObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            var collider = projectileObject.GetComponent<SphereCollider>();
            collider.radius = 0.085f;
            if (ignoreLocalPlayer && player != null)
            {
                foreach (var playerCollider in
                    player.GetComponentsInChildren<Collider>(true))
                {
                    Physics.IgnoreCollision(collider, playerCollider, true);
                }
            }
            var projectile =
                projectileObject.GetComponent<GenesisGrenadeProjectile>();
            projectile.Configure(id, velocity, fuseSeconds, onFuse);
            activeGrenades[id] = projectile;
            return projectile;
        }

        private void OnGrenadeThrown(GenesisGrenadeState state)
        {
            var estimatedServerNow = lastServerTime > 0
                ? lastServerTime
                    + (long)((Time.unscaledTime - serverStateReceivedAt) * 1000f)
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fuse = Mathf.Max(
                0.1f, (state.explodesAt - estimatedServerNow) / 1000f);
            SpawnGrenadeProjectile(
                state.id,
                state.position,
                state.velocity,
                fuse,
                state.isLocalThrow,
                null);
        }

        private void OnGrenadeExploded(GenesisExplosionState state)
        {
            GenesisGrenadeProjectile projectile;
            if (activeGrenades.TryGetValue(state.id, out projectile))
            {
                activeGrenades.Remove(state.id);
                if (projectile != null)
                {
                    projectile.transform.position = state.position;
                    Destroy(projectile.gameObject);
                }
            }
            CreateExplosionEffect(state.position, state.radius);
        }

        private void OnTrainingGrenadeFuse(string id, Vector3 position)
        {
            GenesisGrenadeProjectile projectile;
            if (activeGrenades.TryGetValue(id, out projectile))
            {
                activeGrenades.Remove(id);
                if (projectile != null)
                    Destroy(projectile.gameObject);
            }
            const float radius = 6f;
            CreateExplosionEffect(position, radius);
            if (trainingArena == null)
                return;
            string[] killedNames;
            var hitCount = trainingArena.Explode(
                position, radius, out killedNames);
            if (hitCount > 0)
                hitMarkerUntil = Time.unscaledTime + 0.28f;
            foreach (var killedName in killedNames)
                RegisterTrainingHit(true, false, killedName);

            if (player != null)
            {
                var distance = Vector3.Distance(
                    position, player.position + Vector3.up);
                if (distance <= radius)
                {
                    var falloff = 1f - distance / radius;
                    var damage = Mathf.RoundToInt(
                        Mathf.Lerp(25f, 100f, falloff));
                    ReceiveTrainingDamage(damage, "GRENADE");
                }
            }
        }

        private void CreateExplosionEffect(Vector3 position, float radius)
        {
            if (grenadeExplosionAudio != null)
                AudioSource.PlayClipAtPoint(
                    grenadeExplosionAudio, position, 0.9f);
            var effect = new GameObject("Recovered_Grenade01_Explosion");
            effect.name = "Recovered_Grenade01_Explosion";
            effect.transform.position = position;
            var lightObject = new GameObject("Explosion Flash");
            lightObject.transform.SetParent(effect.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = radius * 1.8f;
            light.color = new Color(1f, 0.38f, 0.08f);
            light.intensity = 8f;
            effect.AddComponent<GenesisExplosionEffect>().Configure(radius);
        }

        private void CreateShellEjection(Transform muzzle, string weaponId)
        {
            if (muzzle == null)
                return;
            var shell = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shell.name = "Recovered_" + weaponId + "_Shell";
            shell.layer = 2;
            shell.transform.position = muzzle.position
                + muzzle.right * 0.035f + muzzle.up * 0.015f;
            shell.transform.localScale = weaponId == "pistol"
                ? new Vector3(0.008f, 0.018f, 0.008f)
                : weaponId == GenesisWeaponLoadout.Shotgun
                    ? new Vector3(0.014f, 0.032f, 0.014f)
                    : new Vector3(0.01f, 0.024f, 0.01f);
            var renderer = shell.GetComponent<Renderer>();
            var shader = Shader.Find("Standard");
            if (renderer != null && shader != null)
            {
                var material = new Material(shader);
                material.name = "Recovered Shell Material";
                material.color = weaponId == GenesisWeaponLoadout.Shotgun
                    ? new Color(0.54f, 0.08f, 0.035f)
                    : new Color(0.72f, 0.52f, 0.16f);
                material.SetFloat("_Metallic", 0.72f);
                material.SetFloat("_Glossiness", 0.58f);
                renderer.material = material;
            }
            var body = shell.AddComponent<Rigidbody>();
            body.mass = 0.018f;
            body.drag = 0.08f;
            body.angularDrag = 0.03f;
            body.velocity = muzzle.right * UnityEngine.Random.Range(1.2f, 1.9f)
                + muzzle.up * UnityEngine.Random.Range(0.65f, 1.15f)
                + muzzle.forward * UnityEngine.Random.Range(-0.25f, 0.2f);
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 24f;
            Destroy(shell, 2.4f);
        }

        private void CreatePredictedImpact(Vector3 direction, float range)
        {
            if (viewCamera == null)
                return;
            RaycastHit hit;
            var ray = GenesisAimAlignment.ShotRay(
                viewCamera.transform.position, direction);
            if (!Physics.Raycast(
                    ray, out hit, range, ~0, QueryTriggerInteraction.Ignore))
                return;
            if (player != null
                && (hit.transform == player || hit.transform.IsChildOf(player)))
                return;
            var blood = hit.transform.GetComponentInParent<GenesisTrainingBot>()
                != null;
            CreateImpactEffect(hit.point, hit.normal, blood);
        }

        private void CreateImpactEffect(
            Vector3 position, Vector3 normal, bool blood)
        {
            var effect = new GameObject(
                blood ? "Recovered_BloodImpact" : "Recovered_BulletImpact");
            effect.transform.position = position + normal * 0.012f;
            effect.transform.rotation = Quaternion.LookRotation(
                normal.sqrMagnitude > 0.001f ? normal : Vector3.up);
            var particles = effect.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.duration = 0.08f;
            main.startLifetime = blood
                ? new ParticleSystem.MinMaxCurve(0.2f, 0.48f)
                : new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSpeed = blood
                ? new ParticleSystem.MinMaxCurve(0.7f, 2.2f)
                : new ParticleSystem.MinMaxCurve(0.45f, 1.5f);
            main.startSize = blood
                ? new ParticleSystem.MinMaxCurve(0.025f, 0.075f)
                : new ParticleSystem.MinMaxCurve(0.012f, 0.045f);
            main.startColor = blood
                ? new ParticleSystem.MinMaxGradient(
                    new Color(0.28f, 0.005f, 0.002f, 0.95f),
                    new Color(0.68f, 0.025f, 0.008f, 0.9f))
                : new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.67f, 0.16f, 0.95f),
                    new Color(0.28f, 0.24f, 0.2f, 0.7f));
            main.gravityModifier = blood ? 0.82f : 0.38f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = blood ? 20 : 14;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            particles.Emit(blood ? 16 : 11);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Sprites/Default");
            if (renderer != null && shader != null)
            {
                var material = new Material(shader);
                material.name = blood
                    ? "Recovered Blood Material"
                    : "Recovered Impact Material";
                material.color = blood
                    ? new Color(0.52f, 0.012f, 0.004f, 0.95f)
                    : new Color(1f, 0.58f, 0.12f, 0.9f);
                renderer.material = material;
            }
            particles.Play(true);
            Destroy(effect, blood ? 0.65f : 0.42f);
        }

        private void PlayMuzzleFlash(
            Transform muzzle,
            Vector3 shotDirection,
            float convergenceDistance)
        {
            if (muzzle == null)
                return;

            var shotRay = GenesisAimAlignment.ShotRay(
                viewCamera == null
                    ? muzzle.position
                    : viewCamera.transform.position,
                shotDirection);
            var muzzleRotation = GenesisAimAlignment.MuzzleRotation(
                muzzle.position,
                shotRay,
                convergenceDistance);

            // The archived Pyramid flash prefab references scripts that were
            // not recovered. Prefer the complete four-plane WarFX resource
            // from the root M4A1 muzzle-flash package; retain a component-only
            // fallback so combat remains functional if that optional package
            // is absent in a fresh checkout.
            var recoveredPrefab = Resources.Load<GameObject>(
                "OriginalGame/RecoveredMuzzleFlash");
            GameObject flash;
            if (recoveredPrefab != null)
            {
                flash = Instantiate(
                    recoveredPrefab, muzzle.position, muzzleRotation);
                flash.name = "Recovered_WarFX_MuzzleFlash";
                flash.transform.localScale *= 0.42f;
            }
            else
            {
                flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flash.name = "Recovered_Procedural_MuzzleFlash";
                flash.transform.position = muzzle.position;
                flash.transform.rotation = muzzleRotation;
                flash.transform.localScale =
                    new Vector3(0.08f, 0.08f, 0.18f);
                var renderer = flash.GetComponent<Renderer>();
                var shader = Shader.Find("Unlit/Color");
                if (renderer != null && shader != null)
                {
                    var material = new Material(shader);
                    material.color = new Color(1f, 0.54f, 0.08f, 1f);
                    renderer.material = material;
                }
            }
            foreach (var collider in flash.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            var flashLight = flash.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.45f, 0.08f);
            flashLight.range = 2.2f;
            flashLight.intensity = 2.6f;
            SetLayerRecursively(flash, 31);
            Destroy(flash, 0.065f);
        }

        private void BeginReload()
        {
            if ((weaponAction != GenesisWeaponActionState.Ready
                    && weaponAction != GenesisWeaponActionState.Firing)
                || selectedWeapon == WeaponSlot.Knife
                || selectedWeapon == WeaponSlot.Grenade)
                return;
            if (selectedWeapon == WeaponSlot.Rifle
                && (rifleMagazine >= rifleMagazineSize || rifleReserve <= 0))
                return;
            if (selectedWeapon == WeaponSlot.Pistol
                && (pistolMagazine >= PistolMagazineSize || pistolReserve <= 0))
                return;
            var actionRevision = BeginWeaponAction(
                GenesisWeaponActionState.Reloading);
            if (selectedWeapon == WeaponSlot.Rifle)
                PlayRifleAnimation(
                    rifleMagazine <= 0 ? "ReloadEmpty" : "Reload", 0.08f);
            else if (selectedWeapon == WeaponSlot.Pistol)
                PlayPistolAnimation(
                    pistolMagazine <= 0 ? "ReloadEmpty" : "StandardReload",
                    0.08f);
            if (network != null)
                network.RequestAction(
                    NetworkWeaponName(selectedWeapon), "reload");
            reloadRoutine = StartCoroutine(
                Reload(selectedWeapon, actionRevision));
        }

        private string NetworkWeaponName(WeaponSlot weapon)
        {
            return weapon == WeaponSlot.Pistol
                ? "pistol"
                : weapon == WeaponSlot.Knife
                    ? "knife"
                    : weapon == WeaponSlot.Grenade
                        ? "grenade"
                        : rifleWeaponId;
        }

        private void PlayRifleAnimation(string clipName, float fadeLength)
        {
            clipName = ResolveRifleClip(clipName);
            if (rifleAnimation == null || rifleAnimation.GetClip(clipName) == null)
            {
                if (weaponAction == GenesisWeaponActionState.Firing)
                    weaponActions.Transition(GenesisWeaponActionState.Ready);
                return;
            }
            var state = rifleAnimation[clipName];
            state.wrapMode = WrapMode.Once;
            state.speed = clipName.StartsWith("Reload", StringComparison.Ordinal)
                ? state.length / rifleReloadDuration
                : 1f;
            rifleAnimation.CrossFade(clipName, fadeLength);
            StartCoroutine(ReturnRifleToIdle(
                state.length / state.speed, weaponActionRevision));
        }

        private string ResolveRifleClip(string requested)
        {
            if (rifleWeaponId != GenesisWeaponLoadout.Shotgun)
                return requested;
            if (requested == "Fire")
                return "Fire01";
            if (requested == "ReloadEmpty")
                return "Reload02";
            if (requested == "Reload")
                return "Reload01";
            return requested;
        }

        private IEnumerator ReturnRifleToIdle(float delay, int actionRevision)
        {
            yield return new WaitForSeconds(Mathf.Max(0.02f, delay));
            if (rifleAnimation == null
                || actionRevision != weaponActionRevision
                || reloading
                || selectedWeapon != WeaponSlot.Rifle)
                yield break;
            rifleAnimation.CrossFade("Idle01", 0.12f);
            if (weaponAction == GenesisWeaponActionState.Firing)
                weaponActions.Transition(GenesisWeaponActionState.Ready);
        }

        private void PlayPistolAnimation(string clipName, float fadeLength)
        {
            if (pistolAnimation == null
                || pistolAnimation.GetClip(clipName) == null)
            {
                if (weaponAction == GenesisWeaponActionState.Firing)
                    weaponActions.Transition(GenesisWeaponActionState.Ready);
                return;
            }
            var state = pistolAnimation[clipName];
            state.wrapMode = WrapMode.Once;
            state.speed = clipName.Contains("Reload")
                ? state.length / PistolReloadDuration
                : 1f;
            pistolAnimation.CrossFade(clipName, fadeLength);
            StartCoroutine(ReturnPistolToIdle(
                state.length / state.speed, weaponActionRevision));
        }

        private IEnumerator ReturnPistolToIdle(float delay, int actionRevision)
        {
            yield return new WaitForSeconds(Mathf.Max(0.02f, delay));
            if (pistolAnimation == null
                || actionRevision != weaponActionRevision
                || reloading
                || selectedWeapon != WeaponSlot.Pistol)
                yield break;
            pistolAnimation.CrossFade("Idle01", 0.12f);
            if (weaponAction == GenesisWeaponActionState.Firing)
                weaponActions.Transition(GenesisWeaponActionState.Ready);
        }

        private void PlayKnifeAnimation()
        {
            if (knifeAnimation == null
                || knifeAnimation.GetClip("Throw") == null)
            {
                if (weaponAction == GenesisWeaponActionState.Melee)
                    weaponActions.Transition(GenesisWeaponActionState.Ready);
                return;
            }
            var state = knifeAnimation["Throw"];
            state.wrapMode = WrapMode.Once;
            knifeAnimation.CrossFade("Throw", 0.025f);
            StartCoroutine(ReturnKnifeToIdle(
                state.length, weaponActionRevision));
        }

        private IEnumerator ReturnKnifeToIdle(float delay, int actionRevision)
        {
            yield return new WaitForSeconds(Mathf.Max(0.02f, delay));
            if (knifeAnimation == null
                || actionRevision != weaponActionRevision
                || selectedWeapon != WeaponSlot.Knife)
                yield break;
            knifeAnimation.CrossFade("Idle01", 0.1f);
            if (weaponAction == GenesisWeaponActionState.Melee)
                weaponActions.Transition(GenesisWeaponActionState.Ready);
        }

        private IEnumerator Reload(WeaponSlot weapon, int actionRevision)
        {
            var reload = weapon == WeaponSlot.Rifle
                ? rifleReloadAudio
                : pistolReloadAudio;
            PlayCombatSound(reload);
            var duration = weapon == WeaponSlot.Rifle
                ? rifleReloadDuration
                : PistolReloadDuration;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (actionRevision != weaponActionRevision)
                    yield break;
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                reloadMotion = Mathf.Sin(normalized * Mathf.PI);
                yield return null;
            }
            if (actionRevision != weaponActionRevision)
                yield break;
            if (weapon == WeaponSlot.Rifle)
            {
                var loaded = GenesisCombatRules.ReloadTransfer(
                    rifleMagazine, rifleReserve, rifleMagazineSize);
                rifleMagazine += loaded;
                rifleReserve -= loaded;
            }
            else
            {
                var loaded = GenesisCombatRules.ReloadTransfer(
                    pistolMagazine, pistolReserve, PistolMagazineSize);
                pistolMagazine += loaded;
                pistolReserve -= loaded;
            }
            reloadMotion = 0f;
            reloading = false;
            if (selectedWeapon == weapon)
            {
                var animation = weapon == WeaponSlot.Rifle
                    ? rifleAnimation
                    : pistolAnimation;
                if (animation != null && animation.GetClip("Idle01") != null)
                    animation.CrossFade("Idle01", 0.12f);
            }
            weaponActions.Transition(GenesisWeaponActionState.Ready);
            reloadRoutine = null;
        }

        private void OnConnectionChanged(bool connected)
        {
            if (!connected)
            {
                SetPlayerControl(false);
                SetAllViewmodelsActive(false);
            }
            else if (alive && !roundOver)
            {
                SetPlayerControl(true);
                SetWeapon(selectedWeapon, false);
            }
            UpdateHud();
        }

        private void OnLocalStateChanged(GenesisLocalState state)
        {
            var wasAlive = alive;
            var previousHealth = health;
            health = state.health;
            kills = state.kills;
            deaths = state.deaths;
            alive = state.alive;
            respawnAt = state.respawnAt;
            protectedUntil = state.protectedUntil;
            lastServerTime = state.serverTime;
            serverStateReceivedAt = Time.unscaledTime;
            if (state.roundEndsAt > state.serverTime)
                roundSeconds = (state.roundEndsAt - state.serverTime) / 1000f;
            if (state.roundState == "ended" && !roundOver)
                FinishRound();

            if (wasAlive != alive)
            {
                if (!alive)
                    PlayCombatSound(deathAudio, 0.72f);
                if (alive)
                {
                    rifleMagazine = rifleMagazineSize;
                    pistolMagazine = PistolMagazineSize;
                    grenadeCount = 1;
                    SetWeapon(selectedWeapon, false);
                }
                SetPlayerControl(alive && !roundOver);
                if (rifle != null)
                    rifle.SetActive(
                        alive
                        && selectedWeapon == WeaponSlot.Rifle
                        && !roundOver);
                if (pistol != null)
                    pistol.SetActive(
                        alive
                        && selectedWeapon == WeaponSlot.Pistol
                        && !roundOver);
                if (knifeOverlay != null)
                    knifeOverlay.gameObject.SetActive(
                        knife == null
                        && alive
                        && selectedWeapon == WeaponSlot.Knife
                        && !roundOver);
                if (knife != null)
                    knife.SetActive(
                        alive
                        && selectedWeapon == WeaponSlot.Knife
                        && !roundOver);
                if (grenade != null)
                    grenade.SetActive(
                        alive
                        && selectedWeapon == WeaponSlot.Grenade
                        && grenadeCount > 0
                        && !roundOver);
                if (alive)
                {
                    var deploy = selectedWeapon == WeaponSlot.Rifle
                        ? rifleDeployAudio
                        : selectedWeapon == WeaponSlot.Pistol
                            ? pistolDeployAudio
                            : null;
                    PlayCombatSound(deploy);
                }
            }
            else if (alive
                && health < previousHealth
                && Time.unscaledTime >= lastHarmedSoundAt + 0.7f)
            {
                damageVignetteAlpha = Mathf.Max(damageVignetteAlpha, 0.28f);
                PlayCombatSound(harmedAudio, 0.56f);
                lastHarmedSoundAt = Time.unscaledTime;
            }
        }

        private void OnHitConfirmed(GenesisHitState state)
        {
            if (state.wasLocalShooter)
            {
                hitMarkerUntil = Time.unscaledTime
                    + (state.targetHealth <= 0 ? 0.3f : 0.16f);
                PlayCombatSound(hitAudio, state.headshot ? 0.58f : 0.45f);
                CreateImpactEffect(state.position, Vector3.up, true);
                if (state.targetHealth <= 0)
                    PlayNetworkKillAnnouncement(state.headshot, state.weapon);
            }
            if (state.wasLocalTarget)
            {
                damageVignetteAlpha = Mathf.Max(damageVignetteAlpha, 0.34f);
                if (state.targetHealth > 0
                    && Time.unscaledTime >= lastHarmedSoundAt + 0.7f)
                {
                    PlayCombatSound(harmedAudio, 0.56f);
                    lastHarmedSoundAt = Time.unscaledTime;
                }
            }
        }

        private void PlayNetworkKillAnnouncement(bool headshot, string weapon)
        {
            if (Time.unscaledTime - lastNetworkKillAt <= 4.5f)
                networkKillChain += 1;
            else
                networkKillChain = 1;
            lastNetworkKillAt = Time.unscaledTime;
            AudioClip announcement = headshot
                ? headshotAnnouncerAudio
                : weapon == "knife"
                    ? knifeKillAudio
                    : networkKillChain == 2
                        ? doubleKillAudio
                        : networkKillChain == 3
                            ? tripleKillAudio
                            : networkKillChain >= 4
                                ? multiKillAudio
                                : null;
            PlayCombatSound(announcement, 0.82f);
        }

        public void RegisterTrainingHit(
            bool killed, bool headshot, string targetName)
        {
            hitMarkerUntil = Time.unscaledTime + (killed ? 0.3f : 0.16f);
            if (!killed)
                PlayCombatSound(hitAudio, 0.42f);
            if (!killed)
                return;

            if (Time.unscaledTime - lastTrainingKillAt <= 4.5f)
                trainingKillChain += 1;
            else
                trainingKillChain = 1;
            lastTrainingKillAt = Time.unscaledTime;

            AudioClip announcement = null;
            if (headshot)
                announcement = headshotAnnouncerAudio;
            else if (selectedWeapon == WeaponSlot.Knife)
                announcement = knifeKillAudio;
            else if (trainingKillChain == 2)
                announcement = doubleKillAudio;
            else if (trainingKillChain == 3)
                announcement = tripleKillAudio;
            else if (trainingKillChain >= 4)
                announcement = multiKillAudio;
            PlayCombatSound(announcement, 0.82f);

            kills += 1;
            PushKillFeed(headshot
                ? "YOU  [HEADSHOT]  " + targetName
                : "YOU  >  " + targetName);
        }

        public void ReceiveTrainingDamage(int damage, string sourceName)
        {
            if (trainingArena == null || !alive || roundOver)
                return;
            health = Mathf.Max(0, health - Mathf.Max(0, damage));
            damageVignetteAlpha = Mathf.Max(damageVignetteAlpha, 0.28f);
            if (weaponAudio != null
                && harmedAudio != null
                && Time.unscaledTime >= lastHarmedSoundAt + 0.7f)
            {
                PlayCombatSound(harmedAudio, 0.56f);
                lastHarmedSoundAt = Time.unscaledTime;
            }
            if (health > 0)
                return;
            StartCoroutine(TrainingRespawn(sourceName));
        }

        private IEnumerator TrainingRespawn(string sourceName)
        {
            alive = false;
            deaths += 1;
            PlayCombatSound(deathAudio, 0.72f);
            trainingRespawnAt = Time.unscaledTime + 3f;
            SetPlayerControl(false);
            if (rifle != null)
                rifle.SetActive(false);
            if (pistol != null)
                pistol.SetActive(false);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(false);
            if (knife != null)
                knife.SetActive(false);
            if (grenade != null)
                grenade.SetActive(false);
            PushKillFeed(sourceName + "  >  YOU");

            yield return new WaitForSecondsRealtime(3f);
            var controller = player.GetComponent<CharacterController>();
            var wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;
            player.position = trainingSpawnPosition;
            if (wasEnabled)
                controller.enabled = true;
            if (trainingArena != null)
                trainingArena.ResetPlayerForTraining(4.5f);
            health = 100;
            alive = true;
            trainingRespawnAt = 0f;
            rifleMagazine = rifleMagazineSize;
            pistolMagazine = PistolMagazineSize;
            grenadeCount = 1;
            SetPlayerControl(!roundOver);
            SetWeapon(selectedWeapon, false);
        }

        private void OnRosterChanged(GenesisPlayerState[] roster)
        {
            if (roster == null)
                return;
            radarRoster = roster;
            if (scoreboardText == null)
                return;
            if (mapText != null)
            {
                mapText.text = "MAP  "
                    + GenesisMultiplayerBootstrap.GetDisplayName(
                        SceneManager.GetActiveScene().name)
                    + "    ONLINE " + roster.Length;
            }
            Array.Sort(roster, delegate(
                GenesisPlayerState left,
                GenesisPlayerState right)
            {
                int killOrder = right.kills.CompareTo(left.kills);
                return killOrder != 0
                    ? killOrder
                    : left.deaths.CompareTo(right.deaths);
            });
            var output = new StringBuilder();
            output.AppendLine("FREE FOR ALL");
            output.AppendLine();
            output.AppendLine("PLAYER                       KILLS  DEATHS");
            foreach (GenesisPlayerState entry in roster)
            {
                string marker = entry.isLocal ? "▶ " : "   ";
                string state = entry.alive ? string.Empty : " [DEAD]";
                output.Append(marker)
                    .Append((entry.name + state).PadRight(24))
                    .Append(entry.kills.ToString().PadLeft(4))
                    .Append("    ")
                    .AppendLine(entry.deaths.ToString().PadLeft(4));
            }
            output.AppendLine();
            output.Append("RELEASE TAB TO RETURN");
            scoreboardText.text = output.ToString();
        }

        private void OnPlayerKilled(string killerName, string victimName)
        {
            PushKillFeed(killerName + "  >  " + victimName);
        }

        private void PushKillFeed(string message)
        {
            if (killFeedText == null || string.IsNullOrWhiteSpace(message))
                return;
            killFeedLines.Insert(
                0,
                "<color=#dcecef>" + message.Replace(
                    "  >  ",
                    "</color>  <color=#ffb84d>></color>  <color=#f2d5bd>")
                + "</color>");
            if (killFeedLines.Count > 4)
                killFeedLines.RemoveRange(4, killFeedLines.Count - 4);
            killFeedText.text = string.Join("\n", killFeedLines.ToArray());
            killFeedUntil = Time.unscaledTime + 4.5f;
            if (killFeedPanel != null)
                killFeedPanel.gameObject.SetActive(true);
        }

        private void SetPlayerControl(bool enabled)
        {
            if (!enabled)
            {
                if (switchRoutine != null)
                {
                    StopCoroutine(switchRoutine);
                    switchRoutine = null;
                }
                ResetWeaponAction();
                ResetAllViewmodelAnimations();
                weaponTransition = 0f;
                scoped = false;
            }
            if (movement != null)
                movement.enabled = false;
            if (jumping != null)
                jumping.enabled = false;
            if (fpsMotor != null)
                fpsMotor.enabled = enabled;
            if (mouseLook != null)
#if UNITY_WEBGL && !UNITY_EDITOR
                mouseLook.enabled = false;
#else
                mouseLook.enabled = enabled;
#endif
        }

        private void SetAllViewmodelsActive(bool active)
        {
            if (rifle != null)
                rifle.SetActive(active && selectedWeapon == WeaponSlot.Rifle);
            if (pistol != null)
                pistol.SetActive(active && selectedWeapon == WeaponSlot.Pistol);
            if (knifeOverlay != null)
            {
                knifeOverlay.gameObject.SetActive(
                    active && knife == null
                    && selectedWeapon == WeaponSlot.Knife);
            }
            if (knife != null)
                knife.SetActive(active && selectedWeapon == WeaponSlot.Knife);
            if (grenade != null)
            {
                grenade.SetActive(
                    active && selectedWeapon == WeaponSlot.Grenade
                    && grenadeCount > 0);
            }
        }

        private void ResetAllViewmodelAnimations()
        {
            ResetViewmodelAnimation(rifleAnimation);
            ResetViewmodelAnimation(pistolAnimation);
            ResetViewmodelAnimation(knifeAnimation);
            ResetViewmodelAnimation(grenadeAnimation);
        }

        private static void ResetViewmodelAnimation(Animation animation)
        {
            if (animation == null)
                return;
            animation.Stop();
            var idle = animation["Idle01"];
            if (idle != null)
                idle.time = 0f;
        }

        private void UpdateBrowserMouseLook()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (viewCamera == null || player == null)
                return;
            var mouseX = GenesisConsumeMouseDeltaX();
            var mouseY = GenesisConsumeMouseDeltaY();
            var mousePosition = Input.mousePosition;
            if (Mathf.Approximately(mouseX, 0f)
                && Mathf.Approximately(mouseY, 0f)
                && hasBrowserMousePosition)
            {
                mouseX = mousePosition.x - previousBrowserMousePosition.x;
                mouseY = mousePosition.y - previousBrowserMousePosition.y;
            }
            previousBrowserMousePosition = mousePosition;
            hasBrowserMousePosition = true;
            if (Mathf.Approximately(mouseX, 0f)
                && Mathf.Approximately(mouseY, 0f))
                return;

            var sensitivity = 0.13f
                * (GenesisUserSettings.Current.MouseSensitivity / 100f);
            browserPitch = Mathf.Clamp(
                browserPitch - mouseY * sensitivity, -90f, 90f);
            viewCamera.transform.localRotation =
                Quaternion.Euler(browserPitch, 0f, 0f);
            player.Rotate(Vector3.up * mouseX * sensitivity);
            if (mouseLook != null && mouseLook.Leida != null)
                mouseLook.Leida.Rotate(Vector3.forward * mouseX * sensitivity);
#endif
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private void FinishRound()
        {
            roundOver = true;
            PlayCombatSound(roundWinAudio, 0.72f);
            SetPlayerControl(false);
            if (rifle != null)
                rifle.SetActive(false);
            if (pistol != null)
                pistol.SetActive(false);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(false);
            if (knife != null)
                knife.SetActive(false);
            if (grenade != null)
                grenade.SetActive(false);
            returnAt = Time.unscaledTime + 8f;
            // A server room owns one fixed map. Cycling only the local client at
            // round end splits players in the same room across different scenes.
            // Online matches return to the room browser; training/offline play
            // keeps the recovered map rotation.
            nextMapScene = GenesisLobbySession.HasRoom
                ? "Ziyou1"
                : GenesisMultiplayerBootstrap.GetNextPlayableMap(
                    SceneManager.GetActiveScene().name);
        }

        private void UpdateHud()
        {
            if (healthText == null)
                return;
            healthText.text = Mathf.Max(0, health).ToString();
            if (armorText != null)
                armorText.text = Mathf.Max(0, armor).ToString();
            if (healthFillRect != null)
                healthFillRect.sizeDelta = new Vector2(
                    96f * Mathf.Clamp01(health / 100f), 0f);
            if (armorFillRect != null)
                armorFillRect.sizeDelta = new Vector2(
                    96f * Mathf.Clamp01(armor / 100f), 0f);
            if (selectedWeapon == WeaponSlot.Rifle)
                ammoText.text = reloading
                    ? rifleDisplayName + "\nRELOADING"
                    : rifleDisplayName + "\n" + rifleMagazine + "  /  " + rifleReserve;
            else if (selectedWeapon == WeaponSlot.Pistol)
                ammoText.text = reloading
                    ? "PISTOL\nRELOADING"
                    : "PISTOL\n" + pistolMagazine + "  /  " + pistolReserve;
            else if (selectedWeapon == WeaponSlot.Knife)
                ammoText.text = "KNIFE\nREADY";
            else
                ammoText.text = "GRENADE\n" + grenadeCount;
            if (weaponIcon != null)
            {
                weaponIcon.texture = selectedWeapon == WeaponSlot.Rifle
                    ? rifleIconTexture
                    : selectedWeapon == WeaponSlot.Pistol
                        ? pistolIconTexture
                        : selectedWeapon == WeaponSlot.Grenade
                            ? grenadeIconTexture
                            : knifeIconTexture;
                weaponIcon.gameObject.SetActive(
                    weaponIcon.texture != null && alive && !roundOver);
            }
            var activeSlot = (int)selectedWeapon;
            for (var slot = 0; slot < weaponSlotLabels.Length; slot += 1)
            {
                if (weaponSlotLabels[slot] == null)
                    continue;
                weaponSlotLabels[slot].color = slot == activeSlot
                    ? new Color(1f, 0.72f, 0.25f, 1f)
                    : new Color(0.66f, 0.76f, 0.77f, 0.82f);
                weaponSlotLabels[slot].fontStyle = slot == activeSlot
                    ? FontStyle.Bold
                    : FontStyle.Normal;
            }
            var minutes = Mathf.FloorToInt(roundSeconds / 60f);
            var seconds = Mathf.FloorToInt(roundSeconds % 60f);
            scoreText.text = string.Format(
                "K  {0}    D  {1}       {2:00}:{3:00}",
                kills, deaths, minutes, seconds);
            if (trainingArena != null && scoreboardText != null)
            {
                scoreboardText.text =
                    "TRAINING RANGE\n\n"
                    + "TARGET KILLS     " + kills + "\n"
                    + "DEATHS           " + deaths + "\n"
                    + "ACCURACY DRILL   ACTIVE\n\n"
                    + "MOVING TARGETS RESPAWN AFTER 3 SECONDS";
            }

            if (roundOver)
            {
                statusText.text = "ROUND OVER\nNEXT: "
                    + GenesisMultiplayerBootstrap.GetDisplayName(nextMapScene);
            }
            else if (!alive)
            {
                var respawnSeconds = trainingArena != null
                    ? Mathf.Max(
                        0,
                        Mathf.CeilToInt(
                            trainingRespawnAt - Time.unscaledTime))
                    : Mathf.Max(
                        0,
                        Mathf.CeilToInt(
                            (respawnAt
                             - (lastServerTime
                                + (long)((Time.unscaledTime
                                          - serverStateReceivedAt) * 1000f)))
                            / 1000f));
                statusText.text = "YOU DIED\nRESPAWN " + respawnSeconds + "s";
            }
            else if (protectedUntil > lastServerTime
                + (long)((Time.unscaledTime - serverStateReceivedAt) * 1000f))
            {
                var protectionSeconds = Mathf.Max(0, Mathf.CeilToInt(
                    (protectedUntil - (lastServerTime
                        + (long)((Time.unscaledTime - serverStateReceivedAt)
                            * 1000f))) / 1000f));
                statusText.text = "SPAWN PROTECTION  " + protectionSeconds + "s";
            }
            else if (network != null && !network.IsConnected)
            {
                statusText.text = "CONNECTING...";
            }
            else
            {
                statusText.text = string.Empty;
            }
        }
    }
}
