using System;
using UnityEngine;

namespace GenesisSoldierSoul.Settings
{
    public struct GenesisSettingsSnapshot : IEquatable<GenesisSettingsSnapshot>
    {
        public float MasterVolume;
        public float MusicVolume;
        public float EffectsVolume;
        public float MouseSensitivity;
        public int QualityLevel;
        public int TargetFrameRate;

        public bool Equals(GenesisSettingsSnapshot other)
        {
            return Mathf.Abs(MasterVolume - other.MasterVolume) < 0.0001f
                && Mathf.Abs(MusicVolume - other.MusicVolume) < 0.0001f
                && Mathf.Abs(EffectsVolume - other.EffectsVolume) < 0.0001f
                && Mathf.Abs(MouseSensitivity - other.MouseSensitivity) < 0.0001f
                && QualityLevel == other.QualityLevel
                && TargetFrameRate == other.TargetFrameRate;
        }

        public override bool Equals(object value)
        {
            return value is GenesisSettingsSnapshot
                && Equals((GenesisSettingsSnapshot)value);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = MasterVolume.GetHashCode();
                hash = hash * 397 ^ MouseSensitivity.GetHashCode();
                hash = hash * 397 ^ QualityLevel;
                hash = hash * 397 ^ TargetFrameRate;
                return hash;
            }
        }
    }

    public static class GenesisUserSettings
    {
        private const string Prefix = "Genesis.Settings.";
        private static GenesisSettingsSnapshot current;
        private static bool loaded;

        public static GenesisSettingsSnapshot Defaults
        {
            get
            {
                return new GenesisSettingsSnapshot
                {
                    MasterVolume = 0.9f,
                    MusicVolume = 0.65f,
                    EffectsVolume = 0.85f,
                    MouseSensitivity = 100f,
                    QualityLevel = DefaultQualityLevel(),
                    TargetFrameRate = 60,
                };
            }
        }

        public static GenesisSettingsSnapshot Current
        {
            get
            {
                if (!loaded)
                    Reload();
                return current;
            }
        }

        public static void Reload()
        {
            var defaults = Defaults;
            current = Sanitize(new GenesisSettingsSnapshot
            {
                MasterVolume = PlayerPrefs.GetFloat(
                    Prefix + "MasterVolume", defaults.MasterVolume),
                MusicVolume = PlayerPrefs.GetFloat(
                    Prefix + "MusicVolume", defaults.MusicVolume),
                EffectsVolume = PlayerPrefs.GetFloat(
                    Prefix + "EffectsVolume", defaults.EffectsVolume),
                MouseSensitivity = PlayerPrefs.GetFloat(
                    Prefix + "MouseSensitivity", defaults.MouseSensitivity),
                QualityLevel = PlayerPrefs.GetInt(
                    Prefix + "QualityLevel", defaults.QualityLevel),
                TargetFrameRate = PlayerPrefs.GetInt(
                    Prefix + "TargetFrameRate", defaults.TargetFrameRate),
            });
            loaded = true;
        }

        public static void Save(GenesisSettingsSnapshot value)
        {
            current = Sanitize(value);
            loaded = true;
            PlayerPrefs.SetFloat(Prefix + "MasterVolume", current.MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "MusicVolume", current.MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "EffectsVolume", current.EffectsVolume);
            PlayerPrefs.SetFloat(
                Prefix + "MouseSensitivity", current.MouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "QualityLevel", current.QualityLevel);
            PlayerPrefs.SetInt(
                Prefix + "TargetFrameRate", current.TargetFrameRate);
            PlayerPrefs.Save();
            Apply(current);
        }

        public static GenesisSettingsSnapshot Sanitize(GenesisSettingsSnapshot value)
        {
            var defaults = Defaults;
            value.MasterVolume = ClampFinite(
                value.MasterVolume, 0f, 1f, defaults.MasterVolume);
            value.MusicVolume = ClampFinite(
                value.MusicVolume, 0f, 1f, defaults.MusicVolume);
            value.EffectsVolume = ClampFinite(
                value.EffectsVolume, 0f, 1f, defaults.EffectsVolume);
            value.MouseSensitivity = ClampFinite(
                value.MouseSensitivity, 25f, 250f, defaults.MouseSensitivity);
            value.QualityLevel = Mathf.Clamp(
                value.QualityLevel, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            value.TargetFrameRate = NearestFrameRate(value.TargetFrameRate);
            return value;
        }

        public static void ApplyCurrent()
        {
            Apply(Current);
        }

        public static void Apply(GenesisSettingsSnapshot value)
        {
            value = Sanitize(value);
            AudioListener.volume = value.MasterVolume;
            Application.targetFrameRate = value.TargetFrameRate;
            if (QualitySettings.names.Length > 0
                && QualitySettings.GetQualityLevel() != value.QualityLevel)
                QualitySettings.SetQualityLevel(value.QualityLevel, true);
        }

        private static int DefaultQualityLevel()
        {
            return Mathf.Clamp(
                QualitySettings.GetQualityLevel(),
                0,
                Mathf.Max(0, QualitySettings.names.Length - 1));
        }

        private static int NearestFrameRate(int requested)
        {
            var allowed = new[] { 30, 45, 60 };
            var nearest = allowed[0];
            var distance = Mathf.Abs(requested - nearest);
            for (var index = 1; index < allowed.Length; index += 1)
            {
                var candidateDistance = Mathf.Abs(requested - allowed[index]);
                if (candidateDistance < distance)
                {
                    nearest = allowed[index];
                    distance = candidateDistance;
                }
            }
            return nearest;
        }

        private static float ClampFinite(
            float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return Mathf.Clamp(value, minimum, maximum);
        }
    }
}
