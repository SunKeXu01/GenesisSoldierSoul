using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    /// <summary>
    /// Keeps the visual muzzle presentation converged on the same ray used by
    /// the centred crosshair, training hit test and authoritative server shot.
    /// The shot remains camera-authored so a close viewmodel cannot introduce
    /// parallax into gameplay.
    /// </summary>
    public static class GenesisAimAlignment
    {
        public static Ray ShotRay(Vector3 cameraPosition, Vector3 shotDirection)
        {
            var direction = shotDirection.sqrMagnitude > 0.0001f
                ? shotDirection.normalized
                : Vector3.forward;
            return new Ray(cameraPosition, direction);
        }

        public static Vector3 MuzzleDirection(
            Vector3 muzzlePosition,
            Ray shotRay,
            float convergenceDistance)
        {
            var distance = Mathf.Max(1f, convergenceDistance);
            var direction = shotRay.GetPoint(distance) - muzzlePosition;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : shotRay.direction.normalized;
        }

        public static Quaternion MuzzleRotation(
            Vector3 muzzlePosition,
            Ray shotRay,
            float convergenceDistance)
        {
            return Quaternion.LookRotation(
                MuzzleDirection(
                    muzzlePosition,
                    shotRay,
                    convergenceDistance),
                Vector3.up);
        }
    }
}
