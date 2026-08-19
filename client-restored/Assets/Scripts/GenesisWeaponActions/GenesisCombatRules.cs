using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    public struct GenesisWeaponProfile
    {
        public string Id;
        public int MagazineSize;
        public int StartingReserve;
        public int BodyDamage;
        public float FireInterval;
        public float ReloadDuration;
        public float BaseSpreadDegrees;
        public float MovementSpreadDegrees;
        public float RecoilSpreadDegrees;
        public float RecoilKick;
        public float Range;
    }

    /// <summary>
    /// Canonical client combat values. Presentation, training damage and live
    /// shot direction all consume these rules so HUD spread cannot diverge
    /// from the ray sent to the authoritative server.
    /// </summary>
    public static class GenesisCombatRules
    {
        public const float HeadshotMultiplier = 2f;

        public static GenesisWeaponProfile Profile(string weapon)
        {
            switch (weapon)
            {
                case "m16":
                    return Make(weapon, 30, 90, 27, 0.085f, 2.1f,
                        0.22f, 0.9f, 0.48f, 0.72f, 110f);
                case "ak74m":
                    return Make(weapon, 30, 90, 33, 0.095f, 2.1f,
                        0.26f, 1f, 0.62f, 0.86f, 105f);
                case "an94":
                    // Original props.xml: 30-round magazine, 9.34 rounds/sec
                    // and the dedicated AN94 reload/sound family.
                    return Make(weapon, 30, 90, 31, 0.107f, 2.51f,
                        0.2f, 0.9f, 0.52f, 0.76f, 110f);
                case "m249":
                    // Original props.xml id=706: 100-round box, 11.7
                    // rounds/sec, 3.45-second reload and jtjl=28.
                    return Make(weapon, 100, 100, 28, 1f / 11.7f, 3.45f,
                        0.34f, 1.2f, 0.72f, 0.92f, 105f);
                case "famas":
                    // Original weapon id=311: djrl=25, zdl=75, shesu=11
                    // and hdjsd=2.7. Keep damage in the restored assault-rifle
                    // band because the source's gjl display value is not the
                    // network hit-point value used by this runtime.
                    return Make(weapon, 25, 75, 29, 1f / 11f, 2.7f,
                        0.23f, 0.92f, 0.54f, 0.78f, 105f);
                case "microgalil_baxi":
                    // Original props.xml id=212: Micro Galil Brazil uses a
                    // 35-round magazine, 105 reserve, 13.33 rounds/sec,
                    // 2.23-second reload and jtjl=18.
                    return Make(weapon, 35, 105, 18, 1f / 13.33f, 2.23f,
                        0.3f, 1.05f, 0.58f, 0.72f, 92f);
                case "gatling":
                    // Original props.xml id=702: 150-round magazine/reserve,
                    // 12.5 rounds/sec, 3.69-second reload and jtjl=32.
                    return Make(weapon, 150, 150, 32, 1f / 12.5f, 3.69f,
                        0.38f, 1.3f, 0.68f, 0.62f, 105f);
                case "auga1":
                    // Original props.xml id=306: 30-round magazine, 60 reserve,
                    // 10.34 rounds/sec and 3.96-second reload. The archived
                    // jtjl display field is not the runtime hit-point value.
                    return Make(weapon, 30, 60, 27, 1f / 10.34f, 3.96f,
                        0.2f, 0.86f, 0.5f, 0.74f, 110f);
                case "ak47_bingzuan":
                    // Original props.xml id=326: 30/60 ammunition, 9.55
                    // rounds/sec and 2.42-second reload for Ice Diamond AK47.
                    return Make(weapon, 30, 60, 34, 1f / 9.55f, 2.42f,
                        0.27f, 1.02f, 0.62f, 0.88f, 105f);
                case "shotgun01":
                    return Make(weapon, 8, 32, 10, 0.85f, 2.8f,
                        0f, 0f, 0f, 1.1f, 55f);
                case "awp":
                    return Make(weapon, 10, 20, 85, 1.25f, 3.03f,
                        0.03f, 0.7f, 0.2f, 1.35f, 160f);
                case "pistol":
                    return Make(weapon, 12, 48, 34, 0.25f, 1.45f,
                        0.32f, 1.15f, 0.7f, 0.82f, 90f);
                case "knife":
                    return Make(weapon, 0, 0, 50, 0.5f, 0f,
                        0f, 0f, 0f, 1f, 2.2f);
                default:
                    return Make("m4a1", 30, 90, 30, 0.1f, 2.1f,
                        0.2f, 0.95f, 0.55f, 0.78f, 100f);
            }
        }

        public static int Damage(string weapon, bool headshot)
        {
            var damage = Profile(weapon).BodyDamage;
            return headshot && weapon != "knife"
                ? Mathf.RoundToInt(damage * HeadshotMultiplier)
                : damage;
        }

        public static int ReloadTransfer(
            int magazine, int reserve, int magazineSize)
        {
            return Mathf.Min(Mathf.Max(0, magazineSize - magazine),
                Mathf.Max(0, reserve));
        }

        public static float SpreadDegrees(
            string weapon, float movement, float recoil, bool scoped)
        {
            var profile = Profile(weapon);
            if (weapon == "shotgun01" || weapon == "knife")
                return 0f;
            var baseSpread = weapon == "awp" && !scoped
                ? 1.6f
                : weapon == "auga1" && scoped
                    ? profile.BaseSpreadDegrees * 0.35f
                    : profile.BaseSpreadDegrees;
            return baseSpread
                + Mathf.Clamp01(movement) * profile.MovementSpreadDegrees
                + Mathf.Clamp(recoil, 0f, 2.5f)
                    * profile.RecoilSpreadDegrees;
        }

        public static Vector3 ShotDirection(
            Vector3 forward,
            Vector3 up,
            string weapon,
            int shotSequence,
            float movement,
            float recoil,
            bool scoped)
        {
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            var spread = SpreadDegrees(weapon, movement, recoil, scoped);
            if (spread <= 0f)
                return forward;

            var right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude <= 0.0001f)
                right = Vector3.Cross(Vector3.right, forward);
            right.Normalize();
            var correctedUp = Vector3.Cross(forward, right).normalized;

            // A stable low-discrepancy pattern gives reproducible regression
            // tests while still distributing consecutive shots across a cone.
            var phase = Mathf.Repeat(shotSequence * 0.61803398875f, 1f);
            var radius = Mathf.Sqrt(
                Mathf.Repeat(shotSequence * 0.754877666f, 1f));
            var angle = phase * Mathf.PI * 2f;
            var tangent = Mathf.Tan(spread * Mathf.Deg2Rad) * radius;
            return (forward + (right * Mathf.Cos(angle)
                + correctedUp * Mathf.Sin(angle)) * tangent).normalized;
        }

        private static GenesisWeaponProfile Make(
            string id,
            int magazine,
            int reserve,
            int damage,
            float interval,
            float reload,
            float baseSpread,
            float movementSpread,
            float recoilSpread,
            float recoilKick,
            float range)
        {
            return new GenesisWeaponProfile
            {
                Id = id,
                MagazineSize = magazine,
                StartingReserve = reserve,
                BodyDamage = damage,
                FireInterval = interval,
                ReloadDuration = reload,
                BaseSpreadDegrees = baseSpread,
                MovementSpreadDegrees = movementSpread,
                RecoilSpreadDegrees = recoilSpread,
                RecoilKick = recoilKick,
                Range = range,
            };
        }
    }
}
