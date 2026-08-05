using GenesisSoldierSoul.WeaponActions;
using NUnit.Framework;

public sealed class GenesisWeaponActionTrackerTests
{
    [Test]
    public void ReloadCanBeCancelledByWeaponSwitch()
    {
        var tracker = new GenesisWeaponActionTracker();
        var reloadRevision = tracker.Begin(
            GenesisWeaponActionState.Reloading);

        tracker.Begin(GenesisWeaponActionState.Holstering);

        Assert.That(tracker.State,
            Is.EqualTo(GenesisWeaponActionState.Holstering));
        Assert.That(tracker.IsCurrent(reloadRevision), Is.False);
        Assert.That(tracker.TryComplete(
            reloadRevision,
            GenesisWeaponActionState.Reloading), Is.False);
    }

    [Test]
    public void OldCompletionCannotOverwriteNewAction()
    {
        var tracker = new GenesisWeaponActionTracker();
        var oldRevision = tracker.Begin(GenesisWeaponActionState.Firing);
        tracker.Begin(GenesisWeaponActionState.Reloading);

        var completed = tracker.TryComplete(
            oldRevision,
            GenesisWeaponActionState.Firing);

        Assert.That(completed, Is.False);
        Assert.That(tracker.State,
            Is.EqualTo(GenesisWeaponActionState.Reloading));
    }

    [Test]
    public void RapidFireWaitsForNewestShotBeforeReturningToReady()
    {
        var tracker = new GenesisWeaponActionTracker();
        var firstShot = tracker.Begin(GenesisWeaponActionState.Firing);
        var newestShot = tracker.Begin(GenesisWeaponActionState.Firing);

        Assert.That(tracker.TryComplete(
            firstShot,
            GenesisWeaponActionState.Firing), Is.False);
        Assert.That(tracker.State,
            Is.EqualTo(GenesisWeaponActionState.Firing));
        Assert.That(tracker.TryComplete(
            newestShot,
            GenesisWeaponActionState.Firing), Is.True);
        Assert.That(tracker.State,
            Is.EqualTo(GenesisWeaponActionState.Ready));
    }

    [Test]
    public void ResetInvalidatesActionsForDeathAndRoundEnd()
    {
        var tracker = new GenesisWeaponActionTracker();
        var reloadRevision = tracker.Begin(
            GenesisWeaponActionState.Reloading);

        tracker.Reset();

        Assert.That(tracker.State,
            Is.EqualTo(GenesisWeaponActionState.Ready));
        Assert.That(tracker.IsCurrent(reloadRevision), Is.False);
    }

    [Test]
    public void RapidWeaponSwitchKeepsOnlyNewestTransition()
    {
        var tracker = new GenesisWeaponActionTracker();
        var pistolSwitch = tracker.Begin(
            GenesisWeaponActionState.Holstering);
        var knifeSwitch = tracker.Begin(
            GenesisWeaponActionState.Holstering);

        Assert.That(tracker.TryComplete(
            pistolSwitch,
            GenesisWeaponActionState.Holstering), Is.False);
        tracker.Transition(GenesisWeaponActionState.Deploying);
        Assert.That(tracker.TryComplete(
            knifeSwitch,
            GenesisWeaponActionState.Deploying), Is.True);
    }

    [Test]
    public void GrenadeReturnsToPreviouslySelectedWeapon()
    {
        var selection = new GenesisWeaponSelectionTracker();
        selection.Select(WeaponSlot.Pistol);
        selection.Select(WeaponSlot.Grenade);

        Assert.That(selection.FallbackAfterGrenade(),
            Is.EqualTo(WeaponSlot.Pistol));

        selection.Select(selection.FallbackAfterGrenade());
        Assert.That(selection.Current, Is.EqualTo(WeaponSlot.Pistol));
    }

    [Test]
    public void DisconnectResetInvalidatesThrowAndDeployCallbacks()
    {
        var tracker = new GenesisWeaponActionTracker();
        var throwRevision = tracker.Begin(
            GenesisWeaponActionState.Throwing);
        tracker.Transition(GenesisWeaponActionState.Deploying);

        tracker.Reset();

        Assert.That(tracker.State,
            Is.EqualTo(GenesisWeaponActionState.Ready));
        Assert.That(tracker.IsCurrent(throwRevision), Is.False);
        Assert.That(tracker.TryComplete(
            throwRevision,
            GenesisWeaponActionState.Deploying), Is.False);
    }

    [TestCase("rifle", "rifle")]
    [TestCase("m4a1", "rifle")]
    [TestCase("m16", "rifle")]
    [TestCase("ak74m", "rifle")]
    [TestCase("awp", "rifle")]
    [TestCase("shotgun", "shotgun")]
    [TestCase("shotgun01", "shotgun")]
    [TestCase("pistol", "pistol")]
    [TestCase("knife", "knife")]
    [TestCase("grenade", "grenade")]
    public void WeaponAliasesUseOnePresentationFamily(
        string weapon, string expectedPresentation)
    {
        GenesisCombatActionCommand command;
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            weapon, weapon == "grenade" ? "throw" : "equip", out command),
            Is.True);
        Assert.That(command.PresentationWeapon,
            Is.EqualTo(expectedPresentation));
    }

    [TestCase("rifle", GenesisWeaponActionState.Firing, "fire")]
    [TestCase("shotgun01", GenesisWeaponActionState.Reloading, "reload")]
    [TestCase("pistol", GenesisWeaponActionState.Deploying, "equip")]
    [TestCase("knife", GenesisWeaponActionState.Melee, "fire")]
    [TestCase("grenade", GenesisWeaponActionState.Throwing, "throw")]
    public void LocalStateAndRemoteWireActionProduceIdenticalSemantics(
        string weapon,
        GenesisWeaponActionState state,
        string wireAction)
    {
        GenesisCombatActionCommand localMirror;
        GenesisCombatActionCommand remoteReplica;
        Assert.That(GenesisCombatActionSemantics.TryFromState(
            weapon, state, out localMirror), Is.True);
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            weapon, wireAction, out remoteReplica), Is.True);
        Assert.That(localMirror, Is.EqualTo(remoteReplica));
    }

    [Test]
    public void InvalidActionsCannotMutatePresentationState()
    {
        GenesisCombatActionCommand ignored;
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            "knife", "reload", out ignored), Is.False);
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            "rifle", "throw", out ignored), Is.False);
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            "pistol", "unknown", out ignored), Is.False);
        Assert.That(GenesisCombatActionSemantics.TryFromState(
            "rifle", GenesisWeaponActionState.Melee, out ignored), Is.False);
        Assert.That(GenesisCombatActionSemantics.TryFromState(
            "knife", GenesisWeaponActionState.Firing, out ignored), Is.False);
    }

    [Test]
    public void GrenadeEquipAndThrowRemainDistinctCommands()
    {
        GenesisCombatActionCommand equip;
        GenesisCombatActionCommand throwing;
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            "grenade", "equip", out equip), Is.True);
        Assert.That(GenesisCombatActionSemantics.TryCreate(
            "grenade", "throw", out throwing), Is.True);
        Assert.That(equip.Kind, Is.EqualTo(GenesisCombatActionKind.Equip));
        Assert.That(equip.State,
            Is.EqualTo(GenesisWeaponActionState.Deploying));
        Assert.That(throwing.Kind, Is.EqualTo(GenesisCombatActionKind.Throw));
        Assert.That(throwing.State,
            Is.EqualTo(GenesisWeaponActionState.Throwing));
    }
}
