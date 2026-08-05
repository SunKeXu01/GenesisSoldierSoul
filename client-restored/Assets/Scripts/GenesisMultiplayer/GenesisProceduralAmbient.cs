using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Generates a small deterministic ambience loop at runtime. This avoids
    /// introducing an unlicensed audio asset while keeping recovered maps from
    /// sounding completely silent.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GenesisProceduralAmbient : MonoBehaviour
    {
        public enum AmbientProfile
        {
            ConstructionWind,
            TownNight,
            IndustrialHum,
            ColdWind,
        }

        private const int SampleRate = 22050;
        private const int DurationSeconds = 8;

        [SerializeField]
        [Range(0f, 0.35f)]
        private float volume = 0.11f;

        [SerializeField]
        private AmbientProfile profile = AmbientProfile.ConstructionWind;

        private AudioClip generatedClip;

        private void Awake()
        {
            Initialize(true);
        }

        private void Initialize(bool play)
        {
            var source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.priority = 192;
            source.dopplerLevel = 0f;

            generatedClip = CreateAmbient(profile);
            source.clip = generatedClip;
            if (play && Application.isPlaying)
                source.Play();
        }

        private void OnDestroy()
        {
            if (generatedClip != null)
                Destroy(generatedClip);
        }

#if UNITY_EDITOR
        public AudioClip GenerateForEditorAudit()
        {
            Initialize(false);
            return generatedClip;
        }

        public AmbientProfile CurrentProfile
        {
            get { return profile; }
        }

        public void Configure(AmbientProfile newProfile, float newVolume)
        {
            profile = newProfile;
            volume = Mathf.Clamp(newVolume, 0f, 0.35f);
            var source = GetComponent<AudioSource>();
            source.volume = volume;
        }

        public void ReleaseEditorAuditClip()
        {
            if (generatedClip == null)
                return;
            GetComponent<AudioSource>().clip = null;
            DestroyImmediate(generatedClip);
            generatedClip = null;
        }
#endif

        private static AudioClip CreateAmbient(AmbientProfile selectedProfile)
        {
            var sampleCount = SampleRate * DurationSeconds;
            var samples = new float[sampleCount];
            // Every oscillator completes an integer number of cycles in the
            // eight-second window, so looping introduces no discontinuity.
            var cycles = selectedProfile == AmbientProfile.TownNight
                ? new[] { 3, 5, 9, 13, 19, 37, 61, 83 }
                : selectedProfile == AmbientProfile.IndustrialHum
                    ? new[] { 2, 4, 8, 14, 22, 34, 52, 74 }
                    : selectedProfile == AmbientProfile.ColdWind
                        ? new[] { 4, 6, 10, 16, 26, 42, 68, 94 }
                    : new[] { 5, 7, 11, 17, 23, 31, 43, 59 };
            var phases = new[]
            {
                0.3f, 1.7f, 2.4f, 0.9f, 2.9f, 1.1f, 2.1f, 0.5f,
            };
            for (var index = 0; index < sampleCount; index++)
            {
                var normalizedTime = index / (float)sampleCount;
                var wind = 0f;
                for (var oscillator = 0; oscillator < cycles.Length; oscillator++)
                {
                    wind += Mathf.Sin(
                        2f * Mathf.PI * cycles[oscillator] * normalizedTime
                        + phases[oscillator]) / (oscillator + 2f);
                }
                var gust = 0.55f + 0.45f * Mathf.Sin(
                    2f * Mathf.PI * 2f * normalizedTime + 0.8f);
                var distantMachinery = selectedProfile == AmbientProfile.TownNight
                    ? 0.018f * Mathf.Sin(
                        2f * Mathf.PI * 211f * normalizedTime)
                        * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(
                            2f * Mathf.PI * 3f * normalizedTime)), 6f)
                    : selectedProfile == AmbientProfile.IndustrialHum
                        ? 0.045f * Mathf.Sin(
                            2f * Mathf.PI * 53f * normalizedTime)
                            + 0.022f * Mathf.Sin(
                                2f * Mathf.PI * 97f * normalizedTime)
                        : selectedProfile == AmbientProfile.ColdWind
                            ? 0.026f * Mathf.Sin(
                                2f * Mathf.PI * 131f * normalizedTime)
                                * (0.6f + 0.4f * Mathf.Sin(
                                    2f * Mathf.PI * 4f * normalizedTime))
                        : 0.08f * Mathf.Sin(
                            2f * Mathf.PI * 29f * normalizedTime);
                samples[index] = Mathf.Clamp(
                    wind * gust * 0.055f + distantMachinery,
                    -0.24f,
                    0.24f);
            }

            var clip = AudioClip.Create(
                selectedProfile == AmbientProfile.TownNight
                    ? "GeneratedTownNight"
                    : selectedProfile == AmbientProfile.IndustrialHum
                        ? "GeneratedIndustrialHum"
                        : selectedProfile == AmbientProfile.ColdWind
                            ? "GeneratedColdWind"
                        : "GeneratedConstructionWind",
                sampleCount,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
