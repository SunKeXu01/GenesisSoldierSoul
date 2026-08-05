using System;

namespace GenesisSoldierSoul.WeaponActions
{
    public enum GenesisCombatActionKind
    {
        Equip,
        Fire,
        Reload,
        Throw,
    }

    /// <summary>
    /// One canonical action command shared by the local first-person path,
    /// remote third-person replicas and offline training bots.
    /// </summary>
    public struct GenesisCombatActionCommand : IEquatable<GenesisCombatActionCommand>
    {
        public string Weapon { get; private set; }
        public string PresentationWeapon { get; private set; }
        public string WireAction { get; private set; }
        public GenesisCombatActionKind Kind { get; private set; }
        public GenesisWeaponActionState State { get; private set; }
        public float PresentationDuration { get; private set; }

        internal GenesisCombatActionCommand(
            string weapon,
            string presentationWeapon,
            string wireAction,
            GenesisCombatActionKind kind,
            GenesisWeaponActionState state,
            float presentationDuration)
        {
            Weapon = weapon;
            PresentationWeapon = presentationWeapon;
            WireAction = wireAction;
            Kind = kind;
            State = state;
            PresentationDuration = presentationDuration;
        }

        public bool Equals(GenesisCombatActionCommand other)
        {
            return Weapon == other.Weapon
                && PresentationWeapon == other.PresentationWeapon
                && WireAction == other.WireAction
                && Kind == other.Kind
                && State == other.State
                && Math.Abs(PresentationDuration - other.PresentationDuration)
                    < 0.0001f;
        }

        public override bool Equals(object value)
        {
            return value is GenesisCombatActionCommand
                && Equals((GenesisCombatActionCommand)value);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Weapon == null ? 0 : Weapon.GetHashCode();
                hash = hash * 397 ^ (WireAction == null ? 0 : WireAction.GetHashCode());
                hash = hash * 397 ^ (int)Kind;
                return hash;
            }
        }
    }

    public static class GenesisCombatActionSemantics
    {
        public static bool TryCreate(
            string requestedWeapon,
            string requestedAction,
            out GenesisCombatActionCommand command)
        {
            var weapon = NormalizeWeapon(requestedWeapon);
            var presentationWeapon = PresentationWeapon(weapon);
            var action = string.IsNullOrEmpty(requestedAction)
                ? string.Empty
                : requestedAction.Trim().ToLowerInvariant();

            if (action == "equip")
            {
                command = Create(
                    weapon, presentationWeapon, "equip",
                    GenesisCombatActionKind.Equip,
                    GenesisWeaponActionState.Deploying, 0.38f);
                return true;
            }
            if (action == "reload"
                && presentationWeapon != "knife"
                && presentationWeapon != "grenade")
            {
                command = Create(
                    weapon, presentationWeapon, "reload",
                    GenesisCombatActionKind.Reload,
                    GenesisWeaponActionState.Reloading, 1.15f);
                return true;
            }
            if ((action == "throw" || action == "fire")
                && presentationWeapon == "grenade")
            {
                command = Create(
                    weapon, presentationWeapon, "throw",
                    GenesisCombatActionKind.Throw,
                    GenesisWeaponActionState.Throwing, 0.62f);
                return true;
            }
            if ((action == "fire" || action == "knife")
                && presentationWeapon != "grenade")
            {
                var melee = presentationWeapon == "knife";
                command = Create(
                    weapon, presentationWeapon, "fire",
                    GenesisCombatActionKind.Fire,
                    melee
                        ? GenesisWeaponActionState.Melee
                        : GenesisWeaponActionState.Firing,
                    melee ? 0.42f : 0.18f);
                return true;
            }

            command = default(GenesisCombatActionCommand);
            return false;
        }

        public static bool TryFromState(
            string weapon,
            GenesisWeaponActionState state,
            out GenesisCombatActionCommand command)
        {
            var created = false;
            if (state == GenesisWeaponActionState.Deploying)
                created = TryCreate(weapon, "equip", out command);
            else if (state == GenesisWeaponActionState.Reloading)
                created = TryCreate(weapon, "reload", out command);
            else if (state == GenesisWeaponActionState.Throwing)
                created = TryCreate(weapon, "throw", out command);
            else if (state == GenesisWeaponActionState.Firing
                || state == GenesisWeaponActionState.Melee)
                created = TryCreate(weapon, "fire", out command);
            else
                command = default(GenesisCombatActionCommand);
            if (created && command.State == state)
                return true;
            command = default(GenesisCombatActionCommand);
            return false;
        }

        public static string NormalizeWeapon(string requested)
        {
            var weapon = string.IsNullOrEmpty(requested)
                ? string.Empty
                : requested.Trim().ToLowerInvariant();
            if (weapon == "pistol" || weapon == "knife" || weapon == "grenade"
                || weapon == "m4a1" || weapon == "m16" || weapon == "ak74m"
                || weapon == "awp")
                return weapon;
            if (weapon == "shotgun" || weapon == "shotgun01")
                return "shotgun01";
            return "rifle";
        }

        public static string PresentationWeapon(string canonicalWeapon)
        {
            if (canonicalWeapon == "pistol" || canonicalWeapon == "knife"
                || canonicalWeapon == "grenade")
                return canonicalWeapon;
            return canonicalWeapon == "shotgun01" ? "shotgun" : "rifle";
        }

        private static GenesisCombatActionCommand Create(
            string weapon,
            string presentationWeapon,
            string action,
            GenesisCombatActionKind kind,
            GenesisWeaponActionState state,
            float duration)
        {
            return new GenesisCombatActionCommand(
                weapon, presentationWeapon, action, kind, state, duration);
        }
    }
}
