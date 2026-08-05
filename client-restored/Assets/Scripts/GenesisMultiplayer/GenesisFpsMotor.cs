using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// One collision-aware motor for the recovered first-person rig. The
    /// archived project split horizontal movement and gravity across two
    /// components whose ground masks were not recovered, so jumping could never
    /// become grounded reliably.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class GenesisFpsMotor : MonoBehaviour
    {
        // Values recovered from the archived FPSController prefab. Keeping the
        // original 5/10 movement split also makes the restored run cycle line up
        // with the character animation instead of looking like a slow slide.
        private const float WalkSpeed = 5f;
        private const float SprintSpeed = 10f;
        private const float Acceleration = 24f;
        private const float AirControl = 8f;
        private const float Gravity = -19.62f;
        private const float JumpSpeed = 10f;
        private const float StickToGroundSpeed = -10f;
        private const float CoyoteTime = 0.12f;
        private const float JumpBufferTime = 0.14f;

        private CharacterController controller;
        private AudioSource movementAudio;
        private AudioClip walkClip;
        private AudioClip jumpClip;
        private AudioClip landClip;
        private readonly HashSet<AudioClip> pendingAudio =
            new HashSet<AudioClip>();
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float lastGroundedAt = -10f;
        private float jumpBufferedUntil = -10f;
        private float nextFootstepAt;
        private float landingKick;
        private bool grounded;
        private bool wasGrounded;

        public float NormalizedSpeed { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public bool IsSprinting { get; private set; }
        public float VerticalVelocity
        {
            get { return verticalVelocity; }
        }
        public bool IsGrounded
        {
            get { return grounded; }
        }
        public float LandingKick
        {
            get { return landingKick; }
        }
        public float AirborneBlend
        {
            get
            {
                return grounded
                    ? 0f
                    : Mathf.Clamp(verticalVelocity / JumpSpeed, -1f, 1f);
            }
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            walkClip = Resources.Load<AudioClip>("music/walk");
            jumpClip = Resources.Load<AudioClip>("music/jump");
            landClip = Resources.Load<AudioClip>("music/step");
            WarmClip(walkClip);
            WarmClip(jumpClip);
            WarmClip(landClip);
            movementAudio = gameObject.AddComponent<AudioSource>();
            movementAudio.playOnAwake = false;
            movementAudio.spatialBlend = 0f;
            movementAudio.volume = 0.42f;
        }

        private void OnEnable()
        {
            verticalVelocity = -2f;
            planarVelocity = Vector3.zero;
            NormalizedSpeed = 0f;
            MoveInput = Vector2.zero;
            IsSprinting = false;
            landingKick = 0f;
            wasGrounded = false;
        }

        private void OnDisable()
        {
            planarVelocity = Vector3.zero;
            NormalizedSpeed = 0f;
            MoveInput = Vector2.zero;
            IsSprinting = false;
            landingKick = 0f;
        }

        private void Update()
        {
            if (controller == null || !controller.enabled)
                return;

            grounded = controller.isGrounded || ProbeGround();
            if (grounded)
            {
                lastGroundedAt = Time.time;
                if (verticalVelocity < 0f)
                {
                    if (!wasGrounded && verticalVelocity < -4f)
                    {
                        landingKick = Mathf.Clamp01(-verticalVelocity / 16f);
                        TryPlay(landClip, Mathf.Lerp(0.34f, 0.68f, landingKick));
                    }
                    verticalVelocity = StickToGroundSpeed;
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
                jumpBufferedUntil = Time.time + JumpBufferTime;

            var input = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
            input = Vector2.ClampMagnitude(input, 1f);
            var sprinting = Input.GetKey(KeyCode.LeftShift)
                || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.CapsLock);
            MoveInput = input;
            IsSprinting = sprinting && input.sqrMagnitude > 0.02f;
            var targetSpeed = sprinting ? SprintSpeed : WalkSpeed;
            var desired = transform.TransformDirection(
                new Vector3(input.x, 0f, input.y)) * targetSpeed;
            var response = grounded ? Acceleration : AirControl;
            planarVelocity = Vector3.MoveTowards(
                planarVelocity, desired, response * Time.deltaTime);

            if (jumpBufferedUntil >= Time.time
                && Time.time - lastGroundedAt <= CoyoteTime)
            {
                jumpBufferedUntil = -10f;
                lastGroundedAt = -10f;
                grounded = false;
                verticalVelocity = JumpSpeed;
                TryPlay(jumpClip, 0.72f);
            }

            verticalVelocity += Gravity * Time.deltaTime;
            controller.Move(
                (planarVelocity + Vector3.up * verticalVelocity)
                * Time.deltaTime);

            NormalizedSpeed = Mathf.Clamp01(
                new Vector3(planarVelocity.x, 0f, planarVelocity.z).magnitude
                / SprintSpeed);
            landingKick = Mathf.MoveTowards(
                landingKick, 0f, Time.deltaTime / 0.2f);
            wasGrounded = grounded;
            UpdateFootsteps(input.sqrMagnitude, sprinting);
        }

        private bool ProbeGround()
        {
            var scale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.z));
            var radius = Mathf.Max(0.08f, controller.radius * scale * 0.82f);
            var center = transform.TransformPoint(controller.center);
            var halfHeight = Mathf.Max(
                radius,
                controller.height * Mathf.Abs(transform.lossyScale.y) * 0.5f);
            var origin = center + Vector3.down * (halfHeight - radius);
            return Physics.SphereCast(
                origin + Vector3.up * 0.06f,
                radius,
                Vector3.down,
                out _,
                0.16f,
                ~0,
                QueryTriggerInteraction.Ignore);
        }

        private void UpdateFootsteps(float inputAmount, bool sprinting)
        {
            if (!grounded || inputAmount < 0.02f || walkClip == null)
                return;
            if (Time.time < nextFootstepAt)
                return;

            nextFootstepAt = Time.time + (sprinting ? 0.3f : 0.4f);
            movementAudio.pitch = Random.Range(0.94f, 1.06f);
            TryPlay(walkClip, sprinting ? 0.5f : 0.36f);
        }

        private static void WarmClip(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }

        private void TryPlay(AudioClip clip, float volume)
        {
            if (clip == null || movementAudio == null)
                return;
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                movementAudio.PlayOneShot(clip, volume);
                return;
            }
            if (pendingAudio.Add(clip))
                StartCoroutine(PlayWhenLoaded(clip, volume));
        }

        private IEnumerator PlayWhenLoaded(AudioClip clip, float volume)
        {
            var timeout = Time.realtimeSinceStartup + 8f;
            while (clip != null
                && clip.loadState == AudioDataLoadState.Loading
                && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            pendingAudio.Remove(clip);
            if (clip != null
                && movementAudio != null
                && movementAudio.enabled
                && movementAudio.gameObject.activeInHierarchy
                && clip.loadState == AudioDataLoadState.Loaded)
            {
                movementAudio.PlayOneShot(clip, volume);
            }
        }
    }
}
