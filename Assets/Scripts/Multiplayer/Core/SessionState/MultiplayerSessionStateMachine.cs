using System;
using System.Collections.Generic;

public sealed class MultiplayerSessionStateMachine
{
    private static readonly Dictionary<MultiplayerSessionState, HashSet<MultiplayerSessionState>> AllowedTransitions =
        new Dictionary<MultiplayerSessionState, HashSet<MultiplayerSessionState>>
        {
            { MultiplayerSessionState.Idle, new HashSet<MultiplayerSessionState> { MultiplayerSessionState.Discovering, MultiplayerSessionState.Joining } },
            { MultiplayerSessionState.Discovering, new HashSet<MultiplayerSessionState> { MultiplayerSessionState.Joining, MultiplayerSessionState.Idle } },
            { MultiplayerSessionState.Joining, new HashSet<MultiplayerSessionState> { MultiplayerSessionState.SyncingPlayerData, MultiplayerSessionState.Idle } },
            { MultiplayerSessionState.SyncingPlayerData, new HashSet<MultiplayerSessionState> { MultiplayerSessionState.InSession, MultiplayerSessionState.Idle } },
            { MultiplayerSessionState.InSession, new HashSet<MultiplayerSessionState> { MultiplayerSessionState.Leaving } },
            { MultiplayerSessionState.Leaving, new HashSet<MultiplayerSessionState> { MultiplayerSessionState.Idle } },
        };

    public MultiplayerSessionState CurrentState { get; private set; } = MultiplayerSessionState.Idle;
    public event Action<MultiplayerSessionState, MultiplayerSessionState> OnStateChanged;

    public bool TryTransition(MultiplayerSessionState nextState)
    {
        if (CurrentState == nextState)
        {
            return true;
        }

        if (!AllowedTransitions.TryGetValue(CurrentState, out var allowed) || !allowed.Contains(nextState))
        {
            return false;
        }

        var previous = CurrentState;
        CurrentState = nextState;
        OnStateChanged?.Invoke(previous, nextState);
        return true;
    }

    public void ForceState(MultiplayerSessionState state)
    {
        var previous = CurrentState;
        CurrentState = state;
        OnStateChanged?.Invoke(previous, state);
    }
}
