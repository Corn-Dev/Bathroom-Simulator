// Optional adapter interface: host-side asynchronous world readiness. Design: 04 §9.3, 01 §4.8, 02 §2e step 4.
// SessionController (host, StartingGame) calls OnSessionLive, then polls this in Tick:
// IsHostWorldReady → publish phase inGame + k.server, SetJoinable, enter InGame (autosave clock starts).
// FailureMessage != null → Teardown(Error) and Fail(Unknown, FailureMessage).
// No result within KitTimeouts.WorldReadySeconds → Fail(Timeout, SessionErrorMessages.GameStartTimedOut).

namespace MultiplayerKit
{
    public interface IGameReadyGate
    {
        bool IsHostWorldReady { get; }

        /// <summary>Null while pending or ok; else a short user message that ends the session.</summary>
        string FailureMessage { get; }
    }
}
