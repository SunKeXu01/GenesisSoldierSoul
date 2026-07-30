using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Reconnects the recovered Pyramid scene's original HUD, pistol model and
    /// audio to a small but complete browser-playable match loop.
    /// </summary>
    public sealed class GenesisMatchController : MonoBehaviour
    {
        private const int MagazineSize = 12;
        private const int StartingReserve = 48;
        private const float PistolInterval = 0.25f;
        private const float KnifeInterval = 0.5f;
        private const float ReloadDuration = 1.45f;

        private Transform player;
        private Camera viewCamera;
        private GenesisNetworkClient network;
        private PlayerMovement movement;
        private Music1 jumping;
        private MouseLook mouseLook;
        private RawImage knifeOverlay;
        private RawImage crosshair;
        private GameObject pistol;
        private Transform pistolMuzzle;
        private Vector3 pistolRestPosition;
        private AudioSource weaponAudio;
        private AudioClip fireAudio;
        private AudioClip reloadAudio;
        private AudioClip deployAudio;
        private AudioClip slashAudio;
        private AudioClip hitAudio;
        private Text healthText;
        private Text ammoText;
        private Text scoreText;
        private Text statusText;
        private Text controlsText;
        private int magazine = MagazineSize;
        private int reserve = StartingReserve;
        private int health = 100;
        private int kills;
        private int deaths;
        private bool alive = true;
        private bool roundOver;
        private bool reloading;
        private bool pistolSelected = true;
        private float nextAttackAt;
        private float recoil;
        private float hitMarkerUntil;
        private float roundSeconds = 180f;
        private float returnAt;
        private float browserPitch;
        private Vector3 previousBrowserMousePosition;
        private bool hasBrowserMousePosition;

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

            movement = player.GetComponent<PlayerMovement>();
            jumping = player.GetComponent<Music1>();
            mouseLook = viewCamera == null
                ? null
                : viewCamera.GetComponent<MouseLook>();
            if (viewCamera != null)
            {
                browserPitch = NormalizeAngle(viewCamera.transform.localEulerAngles.x);
                Rigidbody cameraBody = viewCamera.GetComponent<Rigidbody>();
                if (cameraBody != null)
                {
                    // A dynamic rigidbody on a child camera receives tiny physics
                    // corrections while the player controller moves, which appears
                    // as first-person vertical shaking.
                    cameraBody.isKinematic = true;
                    cameraBody.detectCollisions = false;
                    cameraBody.velocity = Vector3.zero;
                    cameraBody.angularVelocity = Vector3.zero;
                }
            }
            previousBrowserMousePosition = Input.mousePosition;
            hasBrowserMousePosition = true;

            DisableRecoveredConflicts();
            FindRecoveredHud();
            CreateDynamicHudText();
            CreateRecoveredPistol();
            CreateAudio();
            SubscribeNetwork();
            SetWeapon(true, false);
            SetPlayerControl(true);
            UpdateHud();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            Debug.Log("[GenesisMatch] 金字塔单图对战闭环已初始化。");
        }

        private void OnDestroy()
        {
            if (network == null)
                return;
            network.ConnectionChanged -= OnConnectionChanged;
            network.LocalStateChanged -= OnLocalStateChanged;
            network.HitConfirmed -= OnHitConfirmed;
        }

        private void Update()
        {
            if (healthText == null)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Time.timeScale = 1f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                SceneManager.LoadScene("Ziyou1");
                return;
            }

            if (Input.GetMouseButtonDown(0)
                && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                return;
            }

            roundSeconds = Mathf.Max(0f, roundSeconds - Time.unscaledDeltaTime);
            if (!roundOver && roundSeconds <= 0f)
                FinishRound();
            if (roundOver && returnAt > 0f && Time.unscaledTime >= returnAt)
            {
                SceneManager.LoadScene("Ziyou1");
                return;
            }

            if (!roundOver && alive)
            {
                UpdateBrowserMouseLook();
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    SetWeapon(true, true);
                if (Input.GetKeyDown(KeyCode.Alpha3))
                    SetWeapon(false, true);
                if (pistolSelected && Input.GetKeyDown(KeyCode.R))
                    BeginReload();
                if (Input.GetMouseButton(0) && Time.time >= nextAttackAt)
                    Attack();
            }

            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 5f);
            if (pistol != null)
            {
                pistol.transform.localPosition = pistolRestPosition
                    + new Vector3(0f, 0f, -recoil * 0.09f);
                pistol.transform.localRotation = Quaternion.Euler(
                    -recoil * 8f, 0f, 0f);
            }

            if (crosshair != null)
                crosshair.color = Time.unscaledTime < hitMarkerUntil
                    ? new Color(1f, 0.2f, 0.1f, 1f)
                    : Color.white;
            UpdateHud();
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
                    else if (textureName.Contains("小刀"))
                        knifeOverlay = rawImage;
                }
            }
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

            healthText = CreateText(
                canvas.transform, "GenesisHealth", new Vector2(90f, 25f),
                new Vector2(180f, 35f), 22, TextAnchor.MiddleLeft);
            ammoText = CreateText(
                canvas.transform, "GenesisAmmo", new Vector2(-95f, 25f),
                new Vector2(190f, 35f), 22, TextAnchor.MiddleRight);
            scoreText = CreateText(
                canvas.transform, "GenesisScore", new Vector2(0f, -24f),
                new Vector2(360f, 40f), 23, TextAnchor.MiddleCenter);
            statusText = CreateText(
                canvas.transform, "GenesisStatus", Vector2.zero,
                new Vector2(560f, 90f), 28, TextAnchor.MiddleCenter);
            controlsText = CreateText(
                canvas.transform, "GenesisControls", new Vector2(0f, 18f),
                new Vector2(520f, 28f), 15, TextAnchor.MiddleCenter);

            SetAnchor(healthText.rectTransform, new Vector2(0f, 0f));
            SetAnchor(ammoText.rectTransform, new Vector2(1f, 0f));
            SetAnchor(scoreText.rectTransform, new Vector2(0.5f, 1f));
            SetAnchor(statusText.rectTransform, new Vector2(0.5f, 0.5f));
            SetAnchor(controlsText.rectTransform, new Vector2(0.5f, 0f));
            controlsText.text = "1 手枪   3 匕首   R 换弹   WASD 移动   ESC 返回大厅";
            controlsText.color = new Color(0.88f, 0.9f, 0.82f, 0.9f);
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
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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

        private static void SetAnchor(RectTransform rect, Vector2 anchor)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
        }

        private void CreateRecoveredPistol()
        {
            if (viewCamera == null)
                return;
            var prefab = Resources.Load<GameObject>("OriginalGame/Pistol");
            if (prefab == null)
            {
                Debug.LogWarning("[GenesisMatch] 恢复手枪资源尚未生成。");
                return;
            }
            pistol = Instantiate(prefab, viewCamera.transform);
            pistol.name = "Recovered_Pistol";
            // This recovered mesh is authored as a world prop with a long +Z
            // barrel. Keep the rear clear of the camera near plane and cant it
            // slightly so the original side profile remains visible.
            pistol.transform.localPosition = new Vector3(0.32f, -0.3f, 1.15f);
            pistol.transform.localRotation = Quaternion.Euler(-8f, -10f, 0f);
            pistol.transform.localScale = Vector3.one * 1.35f;
            pistolRestPosition = pistol.transform.localPosition;
            pistolMuzzle = pistol.transform.Find("Muzzle");
            foreach (var collider in pistol.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        private void CreateAudio()
        {
            weaponAudio = gameObject.AddComponent<AudioSource>();
            weaponAudio.spatialBlend = 0f;
            weaponAudio.playOnAwake = false;
            fireAudio = Resources.Load<AudioClip>("music/fire");
            reloadAudio = Resources.Load<AudioClip>("music/reload");
            deployAudio = Resources.Load<AudioClip>("music/deploy");
            slashAudio = Resources.Load<AudioClip>("music/slash");
            hitAudio = Resources.Load<AudioClip>("music/headshot");
        }

        private void SubscribeNetwork()
        {
            if (network == null)
                return;
            network.ConnectionChanged += OnConnectionChanged;
            network.LocalStateChanged += OnLocalStateChanged;
            network.HitConfirmed += OnHitConfirmed;
        }

        private void SetWeapon(bool usePistol, bool playSound)
        {
            pistolSelected = usePistol;
            reloading = false;
            if (pistol != null)
                pistol.SetActive(usePistol && alive && !roundOver);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(!usePistol && alive && !roundOver);
            if (playSound && weaponAudio != null && deployAudio != null)
                weaponAudio.PlayOneShot(deployAudio);
        }

        private void Attack()
        {
            if (reloading)
                return;
            if (pistolSelected)
            {
                if (magazine <= 0)
                {
                    BeginReload();
                    return;
                }
                magazine -= 1;
                nextAttackAt = Time.time + PistolInterval;
                recoil = 1f;
                if (weaponAudio != null && fireAudio != null)
                    weaponAudio.PlayOneShot(fireAudio);
                PlayMuzzleFlash();
                if (network != null)
                    network.RequestShoot("pistol");
            }
            else
            {
                nextAttackAt = Time.time + KnifeInterval;
                if (weaponAudio != null && slashAudio != null)
                    weaponAudio.PlayOneShot(slashAudio);
                if (network != null)
                    network.RequestShoot("knife");
            }
        }

        private void PlayMuzzleFlash()
        {
            if (pistolMuzzle == null)
                return;
            var prefab = Resources.Load<GameObject>("OriginalGame/PistolMuzzleFlash");
            if (prefab == null)
                return;
            var flash = Instantiate(prefab, pistolMuzzle.position, pistolMuzzle.rotation);
            Destroy(flash, 1f);
        }

        private void BeginReload()
        {
            if (reloading || magazine >= MagazineSize || reserve <= 0)
                return;
            StartCoroutine(Reload());
        }

        private IEnumerator Reload()
        {
            reloading = true;
            if (weaponAudio != null && reloadAudio != null)
                weaponAudio.PlayOneShot(reloadAudio);
            yield return new WaitForSeconds(ReloadDuration);
            var needed = MagazineSize - magazine;
            var loaded = Mathf.Min(needed, reserve);
            magazine += loaded;
            reserve -= loaded;
            reloading = false;
        }

        private void OnConnectionChanged(bool connected)
        {
            UpdateHud();
        }

        private void OnLocalStateChanged(GenesisLocalState state)
        {
            var wasAlive = alive;
            health = state.health;
            kills = state.kills;
            deaths = state.deaths;
            alive = state.alive;
            if (state.roundEndsAt > state.serverTime)
                roundSeconds = (state.roundEndsAt - state.serverTime) / 1000f;
            if (state.roundState == "ended" && !roundOver)
                FinishRound();

            if (wasAlive != alive)
            {
                SetPlayerControl(alive && !roundOver);
                if (pistol != null)
                    pistol.SetActive(alive && pistolSelected && !roundOver);
                if (knifeOverlay != null)
                    knifeOverlay.gameObject.SetActive(
                        alive && !pistolSelected && !roundOver);
                if (alive)
                {
                    magazine = MagazineSize;
                    if (weaponAudio != null && deployAudio != null)
                        weaponAudio.PlayOneShot(deployAudio);
                }
            }
        }

        private void OnHitConfirmed(bool wasLocalShooter)
        {
            if (!wasLocalShooter)
                return;
            hitMarkerUntil = Time.unscaledTime + 0.16f;
            if (weaponAudio != null && hitAudio != null)
                weaponAudio.PlayOneShot(hitAudio, 0.45f);
        }

        private void SetPlayerControl(bool enabled)
        {
            if (movement != null)
                movement.enabled = enabled;
            if (jumping != null)
                jumping.enabled = enabled;
            if (mouseLook != null)
#if UNITY_WEBGL && !UNITY_EDITOR
                mouseLook.enabled = false;
#else
                mouseLook.enabled = enabled;
#endif
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

            const float sensitivity = 0.13f;
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
            SetPlayerControl(false);
            if (pistol != null)
                pistol.SetActive(false);
            if (knifeOverlay != null)
                knifeOverlay.gameObject.SetActive(false);
            returnAt = Time.unscaledTime + 8f;
        }

        private void UpdateHud()
        {
            if (healthText == null)
                return;
            healthText.text = "生命 " + health;
            ammoText.text = pistolSelected
                ? (reloading ? "换弹中" : magazine + " / " + reserve)
                : "匕首";
            var minutes = Mathf.FloorToInt(roundSeconds / 60f);
            var seconds = Mathf.FloorToInt(roundSeconds % 60f);
            scoreText.text = string.Format(
                "雷霆战警  {0} : {1}  烈火联盟     {2:00}:{3:00}",
                kills, deaths, minutes, seconds);

            if (roundOver)
            {
                statusText.text = "本局结束\n即将返回大厅";
            }
            else if (!alive)
            {
                statusText.text = "已阵亡\n等待复活";
            }
            else if (network != null && !network.IsConnected)
            {
                statusText.text = "正在连接对战服务器…";
            }
            else
            {
                statusText.text = string.Empty;
            }
        }
    }
}
