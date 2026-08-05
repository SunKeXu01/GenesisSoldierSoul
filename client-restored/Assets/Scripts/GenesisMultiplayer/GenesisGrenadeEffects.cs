using System;
using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    internal sealed class GenesisGrenadeProjectile : MonoBehaviour
    {
        private Action<string, Vector3> fuseCallback;
        private float detonateAt;
        private bool detonated;

        public string Id { get; private set; }

        public void Configure(
            string id,
            Vector3 velocity,
            float fuseSeconds,
            Action<string, Vector3> onFuse)
        {
            Id = id;
            fuseCallback = onFuse;
            detonateAt = Time.time + Mathf.Max(0.05f, fuseSeconds);
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.mass = 0.35f;
                body.drag = 0.12f;
                body.angularDrag = 0.08f;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousDynamic;
                body.velocity = velocity;
                body.angularVelocity = new Vector3(9f, 13f, 7f);
            }
        }

        private void Update()
        {
            if (detonated || Time.time < detonateAt)
                return;
            detonated = true;
            if (fuseCallback != null)
                fuseCallback(Id, transform.position);
            else
                Destroy(gameObject, 3f);
        }
    }

    internal sealed class GenesisExplosionEffect : MonoBehaviour
    {
        private float startedAt;
        private Light flash;

        public void Configure(float explosionRadius)
        {
            var radius = Mathf.Max(1f, explosionRadius);
            startedAt = Time.time;
            flash = GetComponentInChildren<Light>();
            CreateBurst(
                "Recovered Fireball",
                26,
                0.24f,
                0.52f,
                radius * 0.65f,
                new Color(1f, 0.16f, 0.015f, 1f),
                new Color(1f, 0.72f, 0.08f, 0f),
                true,
                0.22f);
            CreateBurst(
                "Recovered Smoke",
                22,
                0.72f,
                1.45f,
                radius * 0.23f,
                new Color(0.12f, 0.1f, 0.08f, 0.72f),
                new Color(0.04f, 0.04f, 0.04f, 0f),
                false,
                0.36f);
            CreateBurst(
                "Recovered Sparks",
                34,
                0.32f,
                0.74f,
                radius * 1.35f,
                new Color(1f, 0.82f, 0.18f, 1f),
                new Color(1f, 0.18f, 0.02f, 0f),
                true,
                0.055f);
        }

        private void Update()
        {
            var progress = Mathf.Clamp01((Time.time - startedAt) / 0.42f);
            if (flash != null)
                flash.intensity = Mathf.Lerp(11f, 0f, progress);
            if (Time.time - startedAt >= 1.7f)
                Destroy(gameObject);
        }

        private void CreateBurst(
            string objectName,
            short particleCount,
            float minLifetime,
            float maxLifetime,
            float speed,
            Color startColor,
            Color endColor,
            bool additive,
            float size)
        {
            var particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(transform, false);
            var particles = particleObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                minLifetime, maxLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                speed * 0.42f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * 0.55f, size * 1.45f);
            main.startColor = startColor;
            main.gravityModifier = objectName.Contains("Sparks") ? 1.4f : -0.08f;
            main.maxParticles = particleCount;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, particleCount)
            });
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = objectName.Contains("Smoke") ? 0.34f : 0.12f;

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(startColor, 0f),
                    new GradientColorKey(endColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(startColor.a, 0f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, additive ? 0.25f : 0.5f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(1f, additive ? 0.08f : 1.8f)));

            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader);
                material.name = objectName + " Material";
                material.color = startColor;
                particleRenderer.material = material;
            }
            particles.Play(true);
        }
    }
}
