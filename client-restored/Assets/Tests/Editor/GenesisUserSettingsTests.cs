using GenesisSoldierSoul.Settings;
using NUnit.Framework;

public sealed class GenesisUserSettingsTests
{
    [Test]
    public void InvalidValuesAreSanitizedToSupportedRuntimeRanges()
    {
        var sanitized = GenesisUserSettings.Sanitize(new GenesisSettingsSnapshot
        {
            MasterVolume = 4f,
            MusicVolume = -2f,
            EffectsVolume = float.NaN,
            MouseSensitivity = float.PositiveInfinity,
            QualityLevel = 999,
            TargetFrameRate = 52,
        });

        Assert.That(sanitized.MasterVolume, Is.EqualTo(1f));
        Assert.That(sanitized.MusicVolume, Is.EqualTo(0f));
        Assert.That(sanitized.EffectsVolume,
            Is.EqualTo(GenesisUserSettings.Defaults.EffectsVolume));
        Assert.That(sanitized.MouseSensitivity,
            Is.EqualTo(GenesisUserSettings.Defaults.MouseSensitivity));
        Assert.That(sanitized.QualityLevel,
            Is.InRange(0, UnityEngine.QualitySettings.names.Length - 1));
        Assert.That(sanitized.TargetFrameRate, Is.EqualTo(45));
    }

    [TestCase(1, 30)]
    [TestCase(38, 45)]
    [TestCase(59, 60)]
    [TestCase(200, 60)]
    public void FrameRateUsesOnlyRecoveredMenuChoices(
        int requested, int expected)
    {
        var value = GenesisUserSettings.Defaults;
        value.TargetFrameRate = requested;
        Assert.That(
            GenesisUserSettings.Sanitize(value).TargetFrameRate,
            Is.EqualTo(expected));
    }

    [Test]
    public void DefaultsAreImmediatelyRunnable()
    {
        var defaults = GenesisUserSettings.Defaults;
        Assert.That(GenesisUserSettings.Sanitize(defaults), Is.EqualTo(defaults));
        Assert.That(defaults.MasterVolume, Is.InRange(0f, 1f));
        Assert.That(defaults.MouseSensitivity, Is.InRange(25f, 250f));
        Assert.That(defaults.TargetFrameRate, Is.EqualTo(60));
    }
}
