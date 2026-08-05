using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    public static class GenesisShotgunSpread
    {
        public const int PelletCount = 8;
        public const int PelletDamage = 10;
        public const float InnerSpreadTangent = 0.015f;
        public const float OuterSpreadTangent = 0.04f;

        public static Vector3[] Directions(Vector3 forward)
        {
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            var reference = Mathf.Abs(forward.y) > 0.92f
                ? Vector3.right
                : Vector3.up;
            var right = Vector3.Cross(reference, forward).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            var directions = new Vector3[PelletCount];
            directions[0] = forward;
            for (var index = 0; index < PelletCount - 1; index += 1)
            {
                var inner = index < 4;
                var ringIndex = inner ? index : index - 4;
                var ringCount = inner ? 4 : 3;
                var angle = ringIndex / (float)ringCount * Mathf.PI * 2f
                    + (inner ? 0f : Mathf.PI / 4f);
                var spreadTangent = inner
                    ? InnerSpreadTangent
                    : OuterSpreadTangent;
                directions[index + 1] = (forward
                    + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle))
                        * spreadTangent).normalized;
            }
            return directions;
        }
    }
}
