using System;
using System.Linq;
using UnityEngine;

namespace GenesisSoldierSoul.WeaponActions
{
    /// <summary>
    /// Spins the explicit six-barrel pivot recovered from the original MAX
    /// scene. RMB preheats the assembly and LMB keeps it at firing speed.
    /// Remote third-person fire events use short pulses of the same motor.
    /// </summary>
    public sealed class GenesisGatlingBarrelMotor : MonoBehaviour
    {
        public const float MaximumDegreesPerSecond = 1440f;
        public const float Acceleration = 2880f;
        public const float Deceleration = 1800f;

        private Transform barrelAssembly;
        private float angularSpeed;
        private bool localInput;
        private float pulseUntil;

        public float AngularSpeed { get { return angularSpeed; } }
        public Transform BarrelAssembly { get { return barrelAssembly; } }

        public bool Configure(Transform hierarchyRoot)
        {
            barrelAssembly = hierarchyRoot == null
                ? null
                : hierarchyRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => string.Equals(
                        item.name,
                        "GatlingBarrelAssembly",
                        StringComparison.OrdinalIgnoreCase));
            return barrelAssembly != null;
        }

        public void SetInput(bool active)
        {
            localInput = active;
        }

        public void Pulse(float duration = 0.3f)
        {
            pulseUntil = Mathf.Max(
                pulseUntil, Time.unscaledTime + Mathf.Max(0.02f, duration));
        }

        private void OnDisable()
        {
            localInput = false;
            angularSpeed = 0f;
        }

        private void Update()
        {
            if (barrelAssembly == null)
                return;
            var powered = localInput || Time.unscaledTime < pulseUntil;
            angularSpeed = Mathf.MoveTowards(
                angularSpeed,
                powered ? MaximumDegreesPerSecond : 0f,
                (powered ? Acceleration : Deceleration)
                    * Time.unscaledDeltaTime);
            if (angularSpeed > 0.01f)
            {
                barrelAssembly.Rotate(
                    Vector3.forward,
                    angularSpeed * Time.unscaledDeltaTime,
                    Space.Self);
            }
        }
    }
}
