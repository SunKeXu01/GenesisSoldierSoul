using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    public enum GenesisViewmodelKind
    {
        M4A1,
        M16,
        Rifle,
        AK74M,
        AN94,
        M249,
        FAMAS,
        MicroGalilBaxi,
        Gatling,
        AUGA1,
        AK47Ice,
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
                        new Vector3(0.18f, -0.08f, 0.14f),
                        new Vector3(2f, -4f, 1f),
                        0.38f,
                        48f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.M16:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.115f, -0.07f, 0.14f),
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
                case GenesisViewmodelKind.AN94:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.18f, -0.07f, 0.18f),
                        new Vector3(2f, -18f, 1f),
                        0.42f,
                        49f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.M249:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.18f, -0.09f, 0.18f),
                        new Vector3(2f, -16f, 1f),
                        0.42f,
                        50f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.FAMAS:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.17f, -0.08f, 0.18f),
                        new Vector3(2f, -15f, 1f),
                        0.42f,
                        49f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.MicroGalilBaxi:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.27f, -0.078f, 0.18f),
                        new Vector3(2f, -15f, 1f),
                        0.38f,
                        49f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.Gatling:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.18f, -0.045f, 0.2f),
                        new Vector3(2f, -12f, 0f),
                        0.24f,
                        51f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.AUGA1:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.24f, -0.06f, 0.18f),
                        new Vector3(2f, -15f, 1f),
                        0.38f,
                        49f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.AK47Ice:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.17f, -0.075f, 0.18f),
                        new Vector3(2f, -15f, 1f),
                        0.42f,
                        49f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.AWP:
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.1f, -0.07f, 0.14f),
                        new Vector3(1f, -6f, 0f),
                        0.44f,
                        47f,
                        0.01f);
                    break;
                case GenesisViewmodelKind.Shotgun:
                    // Shotgun01's obsolete editor-world root offset is removed
                    // at runtime, so the recovered rig now uses normal metre-
                    // scale framing instead of the old compensating 0.085.
                    pose = new GenesisViewmodelPose(
                        new Vector3(0.26f, -0.2f, 0.12f),
                        Vector3.zero,
                        1f,
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
                        new Vector3(0.12f, -0.12f, 0.12f),
                        Vector3.zero,
                        0.52f,
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
            if (kind == GenesisViewmodelKind.AN94
                || kind == GenesisViewmodelKind.M249
                || kind == GenesisViewmodelKind.FAMAS
                || kind == GenesisViewmodelKind.MicroGalilBaxi
                || kind == GenesisViewmodelKind.Gatling
                || kind == GenesisViewmodelKind.AUGA1
                || kind == GenesisViewmodelKind.AK47Ice)
            {
                // The recovered 3DS AN94 is substantially longer across the
                // view than the archived shared rifle. Give it a dedicated
                // narrow-aspect correction so 4:3 does not crop the stock.
                correctedPosition.x -= 0.09f * narrowness;
                return new GenesisViewmodelPose(
                    correctedPosition,
                    pose.EulerAngles,
                    pose.Scale * (1f - 0.35f * narrowness),
                    pose.FieldOfView,
                    pose.NearClip);
            }
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
