using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    /// <summary>
    /// Canonical world-to-character locomotion mapping shared by network
    /// players and training bots. Continuous 2D output preserves all eight
    /// cardinal/diagonal directions without snapping or increasing diagonal
    /// gait speed.
    /// </summary>
    public static class GenesisDirectionalLocomotion
    {
        public static Vector2 LocalBlend(
            Quaternion facing,
            Vector3 worldDirection,
            float amount)
        {
            var planar = new Vector3(
                worldDirection.x, 0f, worldDirection.z);
            if (planar.sqrMagnitude <= 0.000001f)
                return Vector2.zero;
            var local = Quaternion.Inverse(facing) * planar.normalized;
            var direction = new Vector2(local.x, local.z);
            if (direction.sqrMagnitude <= 0.000001f)
                return Vector2.zero;
            return direction.normalized * Mathf.Clamp01(amount);
        }
    }
}
