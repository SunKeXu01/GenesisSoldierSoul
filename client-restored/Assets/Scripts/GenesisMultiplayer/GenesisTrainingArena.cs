using System;
using System.Collections;
using System.Collections.Generic;
using GenesisSoldierSoul.WeaponActions;
using UnityEngine;
using UnityEngine.AI;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Local combat loop for the recovered lobby's training button. Real rooms
    /// remain server authoritative; training deliberately runs offline so one
    /// player always has moving, shootable opponents.
    /// </summary>
    public sealed class GenesisTrainingArena : MonoBehaviour
    {
        private static readonly Vector3[] SpawnOffsets =
        {
            new Vector3(-8f, 0f, 8f),
            new Vector3(5f, 0f, 13f),
            new Vector3(-3f, 0f, 18f),
            new Vector3(15f, 0f, 16f),
            new Vector3(-17f, 0f, 19f),
            new Vector3(9f, 0f, 25f),
        };

        private readonly List<GenesisTrainingBot> bots =
            new List<GenesisTrainingBot>();
        private Transform localPlayer;
        private GameObject botPrefab;
        private GenesisMatchController match;
        private Vector3 arenaOrigin;
        private Vector3 openForward = Vector3.forward;
        private float playerProtectedUntil;

        public void Configure(
            Transform player,
            GameObject recoveredBotPrefab,
            GenesisMatchController matchController)
        {
            localPlayer = player;
            botPrefab = recoveredBotPrefab;
            match = matchController;
        }

        private IEnumerator Start()
        {
            yield return null;
            if (localPlayer == null || botPrefab == null || match == null)
                yield break;
            arenaOrigin = localPlayer.position;
            openForward = FindOpenDirection();
            ResetPlayerForTraining(7f);
            for (var index = 0; index < 4; index++)
                CreateBot(index);
            Debug.Log("[GenesisTraining] 训练机器人已部署: " + bots.Count);
        }

        public bool TryShoot(
            Ray ray,
            string weapon,
            out bool killed,
            out bool headshot,
            out string botName)
        {
            killed = false;
            headshot = false;
            botName = string.Empty;
            if (weapon == "shotgun01")
                return TryShootShotgun(
                    ray, out killed, out headshot, out botName);
            var range = GenesisCombatRules.Profile(weapon).Range;
            var hits = Physics.RaycastAll(
                ray, range, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, delegate(RaycastHit left, RaycastHit right)
            {
                return left.distance.CompareTo(right.distance);
            });
            foreach (var hit in hits)
            {
                if (hit.transform == localPlayer
                    || hit.transform.IsChildOf(localPlayer))
                    continue;
                var bot = hit.transform.GetComponentInParent<GenesisTrainingBot>();
                if (bot != null)
                {
                    if (!bot.Alive)
                        continue;
                    headshot = weapon != "knife"
                        && hit.point.y - bot.transform.position.y > 0.48f;
                    killed = bot.ApplyDamage(
                        GenesisCombatRules.Damage(weapon, headshot));
                    botName = bot.DisplayName;
                    return true;
                }
                // The first ordinary collider is map cover and blocks the shot.
                if (!hit.collider.isTrigger)
                    return false;
            }
            return false;
        }

        private bool TryShootShotgun(
            Ray ray,
            out bool killed,
            out bool headshot,
            out string botName)
        {
            const float range = 55f;
            const int pelletDamage = 10;
            var damageByBot = new Dictionary<GenesisTrainingBot, int>();
            var headshotByBot =
                new Dictionary<GenesisTrainingBot, bool>();
            foreach (var direction in GenesisShotgunSpread.Directions(
                ray.direction))
            {
                GenesisTrainingBot bot;
                bool pelletHeadshot;
                if (!TryFindTrainingBot(
                        new Ray(ray.origin, direction),
                        range,
                        out bot,
                        out pelletHeadshot))
                    continue;
                int currentDamage;
                damageByBot.TryGetValue(bot, out currentDamage);
                damageByBot[bot] = currentDamage
                    + (pelletHeadshot ? pelletDamage * 2 : pelletDamage);
                bool currentHeadshot;
                headshotByBot.TryGetValue(bot, out currentHeadshot);
                headshotByBot[bot] = currentHeadshot || pelletHeadshot;
            }

            killed = false;
            headshot = false;
            botName = string.Empty;
            foreach (var pair in damageByBot)
            {
                if (string.IsNullOrEmpty(botName))
                    botName = pair.Key.DisplayName;
                headshot = headshot || headshotByBot[pair.Key];
                killed = pair.Key.ApplyDamage(pair.Value) || killed;
            }
            return damageByBot.Count > 0;
        }

        private bool TryFindTrainingBot(
            Ray ray,
            float range,
            out GenesisTrainingBot bot,
            out bool headshot)
        {
            var hits = Physics.RaycastAll(
                ray, range, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, delegate(RaycastHit left, RaycastHit right)
            {
                return left.distance.CompareTo(right.distance);
            });
            foreach (var hit in hits)
            {
                if (hit.transform == localPlayer
                    || hit.transform.IsChildOf(localPlayer))
                    continue;
                bot = hit.transform.GetComponentInParent<GenesisTrainingBot>();
                if (bot != null)
                {
                    if (!bot.Alive)
                        continue;
                    headshot = hit.point.y - bot.transform.position.y > 0.48f;
                    return true;
                }
                if (!hit.collider.isTrigger)
                    break;
            }
            bot = null;
            headshot = false;
            return false;
        }

        public int Explode(
            Vector3 position,
            float radius,
            out string[] killedNames)
        {
            var killed = new List<string>();
            var hitCount = 0;
            foreach (var bot in bots)
            {
                if (bot == null || !bot.Alive)
                    continue;
                var center = bot.transform.position + Vector3.up * 0.45f;
                var distance = Vector3.Distance(position, center);
                if (distance > radius)
                    continue;
                var falloff = 1f - distance / Mathf.Max(0.01f, radius);
                var damage = Mathf.RoundToInt(Mathf.Lerp(25f, 100f, falloff));
                hitCount += 1;
                if (bot.ApplyDamage(damage))
                    killed.Add(bot.DisplayName);
            }
            killedNames = killed.ToArray();
            return hitCount;
        }

        internal Vector3 SpawnPoint(int botIndex)
        {
            var offset = SpawnOffsets[botIndex % SpawnOffsets.Length];
            var openRight = Vector3.Cross(Vector3.up, openForward);
            var preferred = arenaOrigin
                + openRight * offset.x
                + openForward * offset.z;
            return FindVisibleSpawnPoint(botIndex, preferred);
        }

        public void ResetPlayerForTraining(float protectionSeconds = 4f)
        {
            if (localPlayer == null)
                return;
            localPlayer.rotation = Quaternion.LookRotation(openForward);
            var camera = localPlayer.GetComponentInChildren<Camera>(true);
            if (camera != null)
                camera.transform.localRotation = Quaternion.identity;
            playerProtectedUntil = Mathf.Max(
                playerProtectedUntil,
                Time.unscaledTime + Mathf.Max(0f, protectionSeconds));
        }

        internal bool CanAttackPlayer
        {
            get { return Time.unscaledTime >= playerProtectedUntil; }
        }

        private Vector3 FindVisibleSpawnPoint(int botIndex, Vector3 preferred)
        {
            var preferredGround = SnapToGround(preferred);
            Vector3 reachablePoint;
            if (HasClearSight(preferredGround)
                && HasBotSpacing(preferredGround)
                && TryGetReachableNavigationPoint(
                    preferredGround, out reachablePoint))
                return BodyCenterOnNavigation(reachablePoint);

            var openRight = Vector3.Cross(Vector3.up, openForward);
            var side = botIndex % 2 == 0 ? -1f : 1f;
            // Search a fan in front of the player. Training targets should begin
            // in sight instead of spawning behind recovered map walls and
            // immediately shooting the player from an unreadable position.
            for (var ring = 0; ring < 8; ring++)
            {
                var forwardDistance = 9f + ring * 2.4f + botIndex * 0.45f;
                for (var lane = 0; lane < 11; lane++)
                {
                    var signedLane = lane == 0
                        ? 0f
                        : ((lane + 1) / 2) * (lane % 2 == 0 ? -side : side);
                    var candidate = arenaOrigin
                        + openForward * forwardDistance
                        + openRight * signedLane * 2.7f;
                    candidate = SnapToGround(candidate);
                    if (HasClearSight(candidate)
                        && HasBotSpacing(candidate)
                        && TryGetReachableNavigationPoint(
                            candidate, out reachablePoint))
                        return BodyCenterOnNavigation(reachablePoint);
                }
            }

            // A recovered map can have a very narrow spawn island. Never put a
            // training target on collision-only geometry: use the closest
            // reachable NavMesh point even when no fully visible fan candidate
            // exists, then let the bot movement/cover logic take over.
            NavMeshHit fallback;
            if (NavMesh.SamplePosition(
                    preferredGround,
                    out fallback,
                    12f,
                    NavMesh.AllAreas)
                && IsReachableNavigationPoint(fallback.position))
            {
                return BodyCenterOnNavigation(fallback.position);
            }
            return arenaOrigin;
        }

        private bool IsReachableNavigationPoint(Vector3 candidate)
        {
            Vector3 ignored;
            return TryGetReachableNavigationPoint(candidate, out ignored);
        }

        private static Vector3 BodyCenterOnNavigation(Vector3 navPosition)
        {
            return navPosition + Vector3.up * 0.92f;
        }

        private bool TryGetReachableNavigationPoint(
            Vector3 candidate, out Vector3 reachablePoint)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(
                    candidate, out hit, 2.5f, NavMesh.AllAreas))
            {
                reachablePoint = candidate;
                return false;
            }
            var path = new NavMeshPath();
            var reachable = NavMesh.CalculatePath(
                    arenaOrigin, hit.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
            reachablePoint = hit.position;
            return reachable;
        }

        private Vector3 SnapToGround(Vector3 candidate)
        {
            RaycastHit[] groundHits = Physics.RaycastAll(
                candidate + Vector3.up * 10f,
                Vector3.down,
                25f,
                ~0,
                QueryTriggerInteraction.Ignore);
            var bestY = float.NegativeInfinity;
            foreach (var hit in groundHits)
            {
                if (hit.normal.y < 0.55f)
                    continue;
                var bodyCenterY = hit.point.y + 0.92f;
                if (Mathf.Abs(bodyCenterY - arenaOrigin.y) > 4f)
                    continue;
                if (bodyCenterY > bestY)
                    bestY = bodyCenterY;
            }
            if (!float.IsNegativeInfinity(bestY))
                candidate.y = bestY;
            else
                candidate.y = arenaOrigin.y;
            return candidate;
        }

        private bool HasClearSight(Vector3 candidate)
        {
            var origin = arenaOrigin + Vector3.up * 0.55f;
            var destination = candidate + Vector3.up * 0.55f;
            var direction = destination - origin;
            var distance = direction.magnitude;
            if (distance < 3f)
                return false;
            var hits = Physics.RaycastAll(
                origin,
                direction / distance,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, delegate(RaycastHit left, RaycastHit right)
            {
                return left.distance.CompareTo(right.distance);
            });
            foreach (var hit in hits)
            {
                if (hit.transform == localPlayer
                    || hit.transform.IsChildOf(localPlayer))
                    continue;
                return false;
            }
            return true;
        }

        private bool HasBotSpacing(Vector3 candidate)
        {
            foreach (var bot in bots)
            {
                if (bot == null)
                    continue;
                var delta = bot.transform.position - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < 16f)
                    return false;
            }
            return true;
        }

        internal Vector3 SeparationFor(GenesisTrainingBot requester)
        {
            var separation = Vector3.zero;
            foreach (var other in bots)
            {
                if (other == null || other == requester || !other.Alive)
                    continue;
                var delta = requester.transform.position
                    - other.transform.position;
                delta.y = 0f;
                var distance = delta.magnitude;
                if (distance < 0.01f || distance >= 3.4f)
                    continue;
                separation += delta / distance * (1f - distance / 3.4f);
            }
            return Vector3.ClampMagnitude(separation, 1f);
        }

        private Vector3 FindOpenDirection()
        {
            var origin = arenaOrigin + Vector3.up * 0.35f;
            var bestDirection = localPlayer.forward;
            bestDirection.y = 0f;
            if (bestDirection.sqrMagnitude < 0.01f)
                bestDirection = Vector3.forward;
            bestDirection.Normalize();
            var bestDistance = 0f;
            for (var index = 0; index < 16; index++)
            {
                var direction = Quaternion.Euler(0f, index * 22.5f, 0f)
                    * Vector3.forward;
                RaycastHit hit;
                var distance = Physics.Raycast(
                    origin,
                    direction,
                    out hit,
                    28f,
                    ~0,
                    QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : 28f;
                if (distance <= bestDistance)
                    continue;
                bestDistance = distance;
                bestDirection = direction;
            }
            return bestDirection;
        }

        private void CreateBot(int index)
        {
            var anchor = new GameObject("TrainingBot_" + (index + 1));
            anchor.transform.position = SpawnPoint(index);
            var controller = anchor.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.36f;
            controller.center = Vector3.zero;
            controller.stepOffset = 0.34f;
            controller.skinWidth = 0.04f;

            var presentation = Instantiate(botPrefab, anchor.transform);
            presentation.name = "RecoveredTrainingSoldier";
            NormalizePresentation(presentation.transform, anchor.transform);

            var bot = anchor.AddComponent<GenesisTrainingBot>();
            bot.Configure(
                this,
                index,
                "TARGET-" + (index + 1).ToString("00"),
                localPlayer,
                presentation.transform,
                controller,
                match);
            bots.Add(bot);
        }

        private static void NormalizePresentation(
            Transform presentation, Transform anchor)
        {
            var renderers = presentation.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            if (bounds.size.y > 0.01f)
                presentation.localScale *= 1.8f / bounds.size.y;
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            presentation.position += anchor.position - bounds.center;
        }
    }

    internal sealed class GenesisTrainingBot : MonoBehaviour
    {
        private GenesisTrainingArena arena;
        private Transform target;
        private Transform presentation;
        private CharacterController controller;
        private GenesisMatchController match;
        private Transform leftThigh;
        private Transform rightThigh;
        private Transform leftArm;
        private Transform rightArm;
        private Animator animator;
        private Quaternion leftThighRest;
        private Quaternion rightThighRest;
        private Quaternion leftArmRest;
        private Quaternion rightArmRest;
        private Vector3 presentationRest;
        private Quaternion presentationRestRotation;
        private int botIndex;
        private int health = 100;
        private float gaitPhase;
        private float strafeDirection;
        private float nextDirectionAt;
        private float nextShotAt;
        private float respawnAt;
        private Transform nameplate;
        private GenesisThirdPersonActionDriver actionDriver;

        public string DisplayName { get; private set; }
        public bool Alive { get; private set; } = true;

        public void Configure(
            GenesisTrainingArena owner,
            int index,
            string displayName,
            Transform player,
            Transform visual,
            CharacterController body,
            GenesisMatchController matchController)
        {
            arena = owner;
            botIndex = index;
            DisplayName = displayName;
            target = player;
            presentation = visual;
            controller = body;
            match = matchController;
            presentationRest = visual.localPosition;
            presentationRestRotation = visual.localRotation;
            leftThigh = FindDeepChild(visual, "Marine_L_Thigh");
            rightThigh = FindDeepChild(visual, "Marine_R_Thigh");
            leftArm = FindDeepChild(visual, "Marine_L_UpperArm");
            rightArm = FindDeepChild(visual, "Marine_R_UpperArm");
            animator = visual.GetComponentInChildren<Animator>(true);
            var recoveredController = Resources.Load<RuntimeAnimatorController>(
                "OriginalGame/Character/RemotePlayer");
            if (animator != null && recoveredController != null)
            {
                animator.runtimeAnimatorController = recoveredController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.SetBool("Grounded", true);
                animator.SetFloat("Speed", 0f);
                animator.SetFloat("MoveX", 0f);
                animator.SetFloat("MoveZ", 0f);
            }
            leftThighRest = Rotation(leftThigh);
            rightThighRest = Rotation(rightThigh);
            leftArmRest = Rotation(leftArm);
            rightArmRest = Rotation(rightArm);
            actionDriver = new GenesisThirdPersonActionDriver(presentation);
            strafeDirection = index % 2 == 0 ? 1f : -1f;
            nextShotAt = Time.time + 2f + index * 0.35f;
            CreateNameplate();
        }

        private void Update()
        {
            if (!Alive)
            {
                if (Time.time >= respawnAt)
                    Respawn();
                return;
            }
            if (target == null || controller == null || !controller.enabled)
                return;

            var toPlayer = target.position - transform.position;
            toPlayer.y = 0f;
            var distance = toPlayer.magnitude;
            if (distance < 0.01f)
                return;
            var forward = toPlayer / distance;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(forward),
                1f - Mathf.Exp(-8f * Time.deltaTime));

            if (Time.time >= nextDirectionAt)
            {
                nextDirectionAt = Time.time + UnityEngine.Random.Range(1.2f, 2.4f);
                if (UnityEngine.Random.value > 0.45f)
                    strafeDirection *= -1f;
            }
            // Give each opponent its own preferred combat ring. The previous
            // fixed six-metre threshold made every bot converge on the same
            // point, overlapping both their bodies and TARGET nameplates.
            var preferredRange = 7.5f + botIndex * 1.75f;
            var move = Vector3.Cross(Vector3.up, forward) * strafeDirection;
            if (distance > preferredRange + 1.6f)
                move = (forward + move * 0.28f).normalized;
            else if (distance < preferredRange - 1.25f)
                move = -forward;
            move = Vector3.ClampMagnitude(
                move + arena.SeparationFor(this) * 1.35f, 1f);
            controller.Move(
                (move * 2.35f + Vector3.down * 4f) * Time.deltaTime);
            AnimateGait(move);
            TryAttack(distance);
        }

        private void LateUpdate()
        {
            if (nameplate != null && Camera.main != null)
            {
                nameplate.rotation = Quaternion.LookRotation(
                    nameplate.position - Camera.main.transform.position,
                    Vector3.up);
            }
            if (actionDriver != null)
                actionDriver.Apply();
        }

        public bool ApplyDamage(int damage)
        {
            if (!Alive)
                return false;
            health = Mathf.Max(0, health - damage);
            if (health > 0)
            {
                if (actionDriver != null)
                    actionDriver.PlayHit();
                return false;
            }
            Alive = false;
            respawnAt = Time.time + 3f;
            controller.enabled = false;
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
                animator.enabled = false;
            }
            if (nameplate != null)
                nameplate.gameObject.SetActive(false);
            if (actionDriver != null)
                actionDriver.PlayDeath();
            return true;
        }

        private void TryAttack(float distance)
        {
            if (distance > 17f
                || Time.time < nextShotAt
                || match == null
                || arena == null
                || !arena.CanAttackPlayer)
                return;
            nextShotAt = Time.time + UnityEngine.Random.Range(1.35f, 2f);
            if (actionDriver != null)
            {
                GenesisCombatActionCommand semantic;
                if (GenesisCombatActionSemantics.TryCreate(
                        "rifle", "fire", out semantic))
                    actionDriver.PlayAction(semantic);
            }
            var origin = transform.position + Vector3.up * 0.55f;
            var destination = target.position + Vector3.up * 0.35f;
            var direction = destination - origin;
            RaycastHit hit;
            if (!Physics.Raycast(
                    origin,
                    direction.normalized,
                    out hit,
                    direction.magnitude + 0.4f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
                return;
            if (hit.transform != target && !hit.transform.IsChildOf(target))
                return;
            if (UnityEngine.Random.value < 0.38f)
                match.ReceiveTrainingDamage(
                    UnityEngine.Random.Range(5, 9), DisplayName);
        }

        private void Respawn()
        {
            transform.position = arena.SpawnPoint(botIndex);
            health = 100;
            Alive = true;
            controller.enabled = true;
            presentation.localPosition = presentationRest;
            presentation.localRotation = presentationRestRotation;
            presentation.gameObject.SetActive(true);
            if (animator != null)
            {
                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);
                animator.SetBool("Grounded", true);
                animator.SetFloat("Speed", 0f);
                animator.SetFloat("MoveX", 0f);
                animator.SetFloat("MoveZ", 0f);
            }
            if (nameplate != null)
                nameplate.gameObject.SetActive(true);
            if (actionDriver != null)
                actionDriver.Respawn();
            nextShotAt = Time.time + 1.5f;
        }

        private void AnimateGait(Vector3 worldDirection)
        {
            var amount = Mathf.Clamp01(worldDirection.magnitude);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                // Training bots move at 2.35 m/s. The recovered walk clips were
                // authored for the archived 5 m/s walk speed, so preserve that
                // ratio instead of forcing every sidestep into the run cycle.
                var animationSpeed = amount * (2.35f / 5f);
                animator.SetBool("Grounded", true);
                animator.SetFloat(
                    "Speed",
                    animationSpeed,
                    0.1f,
                    Time.deltaTime);
                var blend = GenesisDirectionalLocomotion.LocalBlend(
                    transform.rotation, worldDirection, animationSpeed);
                animator.SetFloat(
                    "MoveX", blend.x, 0.1f, Time.deltaTime);
                animator.SetFloat(
                    "MoveZ", blend.y, 0.1f, Time.deltaTime);
                return;
            }
            gaitPhase += Time.deltaTime * 9f;
            var stride = Mathf.Sin(gaitPhase) * 25f * amount;
            if (leftThigh != null)
                leftThigh.localRotation = leftThighRest
                    * Quaternion.AngleAxis(stride, Vector3.forward);
            if (rightThigh != null)
                rightThigh.localRotation = rightThighRest
                    * Quaternion.AngleAxis(-stride, Vector3.forward);
            if (leftArm != null)
                leftArm.localRotation = leftArmRest
                    * Quaternion.AngleAxis(-stride * 0.35f, Vector3.forward);
            if (rightArm != null)
                rightArm.localRotation = rightArmRest
                    * Quaternion.AngleAxis(stride * 0.35f, Vector3.forward);
            presentation.localPosition = presentationRest
                + Vector3.up * Mathf.Abs(Mathf.Sin(gaitPhase * 2f)) * 0.018f;
        }

        private void CreateNameplate()
        {
            var labelObject = new GameObject("TargetName");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(
                0f, 1.16f + botIndex * 0.11f, 0f);
            nameplate = labelObject.transform;
            var label = labelObject.AddComponent<TextMesh>();
            label.text = DisplayName;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 42;
            label.characterSize = 0.035f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(1f, 0.24f, 0.16f, 1f);
        }

        private static Quaternion Rotation(Transform value)
        {
            return value == null ? Quaternion.identity : value.localRotation;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                var nested = FindDeepChild(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
