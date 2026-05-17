using NUnit.Framework;

public class MultiplayerSessionStateMachineTests
{
    [Test]
    public void ValidTransitionPath_ReachesInSession()
    {
        var machine = new MultiplayerSessionStateMachine();

        Assert.IsTrue(machine.TryTransition(MultiplayerSessionState.Discovering));
        Assert.IsTrue(machine.TryTransition(MultiplayerSessionState.Joining));
        Assert.IsTrue(machine.TryTransition(MultiplayerSessionState.SyncingPlayerData));
        Assert.IsTrue(machine.TryTransition(MultiplayerSessionState.InSession));
        Assert.AreEqual(MultiplayerSessionState.InSession, machine.CurrentState);
    }

    [Test]
    public void InvalidTransition_IsRejected()
    {
        var machine = new MultiplayerSessionStateMachine();

        Assert.IsFalse(machine.TryTransition(MultiplayerSessionState.InSession));
        Assert.AreEqual(MultiplayerSessionState.Idle, machine.CurrentState);
    }

    [Test]
    public void LeaveFlow_ReturnsToIdle()
    {
        var machine = new MultiplayerSessionStateMachine();

        machine.TryTransition(MultiplayerSessionState.Discovering);
        machine.TryTransition(MultiplayerSessionState.Joining);
        machine.TryTransition(MultiplayerSessionState.SyncingPlayerData);
        machine.TryTransition(MultiplayerSessionState.InSession);

        Assert.IsTrue(machine.TryTransition(MultiplayerSessionState.Leaving));
        Assert.IsTrue(machine.TryTransition(MultiplayerSessionState.Idle));
        Assert.AreEqual(MultiplayerSessionState.Idle, machine.CurrentState);
    }
}
