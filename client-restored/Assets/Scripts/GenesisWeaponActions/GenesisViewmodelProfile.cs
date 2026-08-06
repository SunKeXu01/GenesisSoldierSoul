using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    public enum GenesisViewmodelKind
    {
        M4A1,
        Rifle,
        AK74M,
        AWP,
        Shotgun,
        M9,
        Pistol,
        Knife,
        Grenade,
    }

    public readonly struct GenesisViewmodelPose
    {
        public GenesisViewmodelPose(
            Vector3 position,
            Vector3 eulerAngles,
            float scale,
            float fieldOfView,
            float nearClip)
        {
            Position = position;
            EulerAngles = eulerAngles;
            Scale = scale;
            FieldOfView = fieldOfView;
            NearClip = nearClip;
        }

        public Vector3 Position { get; }
        public Vector3 EulerAngles { get; }
        public float Scale { get; }
        public float FieldOfView { get; }
        public float NearClip { get; }
    }

    /// <summary>
    /// Central viewmodel framing profiles. Unity cameras preserve vertical FOV,
    /// so narrow windows need a small left shift and scale reduction to keep the
    /// stock and hands inside the viewport.
    /// </summary>
    public static class GenesisViewmodelProfiles
    {
        public const float ReferenceAspect = 16f / 9f;
        public const float MinimumSupportedAspect = 4f / 3f;

        public static GenesisViewmodelPose Get(
            GenesisViewmodelKind kind,
            float aspect)
        {
            GenesisViewmodelPose pose;
            switch (kind)
            {
                case GenesisViewmodelKind.M4A1:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.11f, -0.19f, 0.14f),
                        new Vector3(2f, -4f, 1f),
                        0.38f,
                        48f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.AK74M:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.095f, -0.2f, 0.14f),
                        new Vector3(1.5f, -3f, 0.5f),
                        0.4f,
                        49f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.AWP:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.1f, -0.19f, 0.16f),
                        new Vector3(1f, -6f, 0f),
                        0.4f,
                        47f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.Shotgun:
                    // The recovered prefab keeps its original large hierarchy;
                    // GenesisMatchController recentres it after applying this
                    // proportional scale so it fits the isolated 3-unit camera.
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.08f, -0.31f, 0.12f),
                        Vector3.zero,
                        0.085f,
                        52f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.Pistol:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.08f, -0.14f, 0.12f),
                        Vector3.zero,
                        0.55f,
                        52f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.M9:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.09f, -0.16f, 0.12f),
                        Vector3.zero,
                        0.55f,
                        50f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.Knife:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.08f, -0.22f, 0.12f),
                        Vector3.zero,
                        0.55f,
                        52f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.Grenade:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.08f, -0.31f, 0.12f),
                        Vector3.zero,
                        0.55f,
                        52f,
                        0.01f);
                    break;
                default:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.08f, -0.31f, 0.12f),
                        Vector3.zero,
                        0.55f,
                        52f,
                        0.01f);
                    break;
            }

            var safeAspect = Mathf.Max(0.1f, aspect);
            var narrowness = Mathf.Clamp(
                (ReferenceAspect - safeAspect)
                    / (ReferenceAspect - MinimumSupportedAspect),
                -1f,
                1f);
            var correctedPosition = pose.Position;
            correctedPosition.x -= 0.04f * narrowness;
            return new GenesisViewmodelPose(
                correctedPosition,
                pose.EulerAngles,
                pose.Scale * (1f - 0.05f * narrowness),
                pose.FieldOfView,
                pose.NearClip);
        }
    }
}
