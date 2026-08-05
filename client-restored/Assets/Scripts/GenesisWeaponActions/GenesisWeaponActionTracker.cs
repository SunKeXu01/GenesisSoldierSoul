namespace GenesisSoldierSoul.WeaponActions
{
    public enum WeaponSlot
    {
        Rifle,
        Pistol,
        Knife,
        Grenade,
    }

    public enum GenesisWeaponActionState
    {
        Ready,
        Firing,
        Reloading,
        Holstering,
        Deploying,
        Melee,
        Throwing,
    }

    /// <summary>
    /// Issues monotonically increasing action tokens so delayed animation and
    /// reload completions cannot overwrite a newer weapon action.
    /// </summary>
    public sealed class GenesisWeaponActionTracker
    {
        public GenesisWeaponActionState State { get; private set; } =
            GenesisWeaponActionState.Ready;

        public int Revision { get; private set; }

        public int Begin(GenesisWeaponActionState state)
        {
            Revision += 1;
            State = state;
            return Revision;
        }

        public void Transition(GenesisWeaponActionState state)
        {
            State = state;
        }

        public void Reset()
        {
            Revision += 1;
            State = GenesisWeaponActionState.Ready;
        }

        public bool IsCurrent(int revision)
        {
            return revision == Revision;
        }

        public bool TryComplete(
            int revision,
            GenesisWeaponActionState expectedState)
        {
            if (!IsCurrent(revision) || State != expectedState)
                return false;
            State = GenesisWeaponActionState.Ready;
            return true;
        }
    }

    /// <summary>
    /// Remembers the last non-consumable weapon so a completed grenade throw
    /// returns to the weapon the player was actually holding.
    /// </summary>
    public sealed class GenesisWeaponSelectionTracker
    {
        public WeaponSlot Current { get; private set; } = WeaponSlot.Rifle;

        public WeaponSlot LastNonGrenade { get; private set; } =
            WeaponSlot.Rifle;

        public void Select(WeaponSlot weapon)
        {
            Current = weapon;
            if (weapon != WeaponSlot.Grenade)
                LastNonGrenade = weapon;
        }

        public WeaponSlot FallbackAfterGrenade()
        {
            return LastNonGrenade;
        }
    }
}
