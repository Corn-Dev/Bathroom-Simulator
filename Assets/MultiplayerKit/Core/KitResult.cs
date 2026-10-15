// Error codes and result types shared by every Core interface, plus the SessionError shown by the UI.
// Design: 01 §3. KitError members are exactly the 01 §3 list (no additions). Their numeric values are a
// wire contract: kick chat messages and NGO disconnect reasons carry ((int)KitError) as decimal text
// (01 §7, 03 §10.3), so never reorder or renumber them.

namespace MultiplayerKit
{
    public enum KitError
    {
        None = 0,
        PlatformUnavailable = 1,
        Timeout = 2,
        Busy = 3,
        InvalidArgument = 4,
        LobbyCreateFailed = 5,
        LobbyNotFound = 6,
        LobbyFull = 7,
        LobbyAccessDenied = 8,
        LobbyNotJoinable = 9,
        IncompatibleVersion = 10,
        PasswordRequired = 11,
        WrongPassword = 12,
        Banned = 13,
        Kicked = 14,
        HostLeft = 15,
        ConnectionLost = 16,
        ApprovalRejected = 17,
        NetworkStartFailed = 18,
        SceneLoadFailed = 19,
        SaveIoFailed = 20,
        SaveCorrupt = 21,
        SaveNotFound = 22,
        Cancelled = 23,
        Unknown = 24
    }

    /// <summary>Outcome of an operation without a value. default(KitResult) is Success.</summary>
    public readonly struct KitResult
    {
        public readonly KitError Error;

        /// <summary>Log text; may be null. Never shown to the user.</summary>
        public readonly string Detail;

        private KitResult(KitError error, string detail)
        {
            Error = error;
            Detail = detail;
        }

        public bool Ok => Error == KitError.None;

        public static KitResult Success { get; } = default(KitResult);

        /// <summary>Failed result. KitError.None is stored as Unknown so a Fail can never read as Ok.</summary>
        public static KitResult Fail(KitError e, string detail = null)
        {
            return new KitResult(e == KitError.None ? KitError.Unknown : e, detail);
        }

        public override string ToString()
        {
            if (Ok) return "Ok";
            return Detail == null ? Error.ToString() : Error + ": " + Detail;
        }
    }

    /// <summary>Outcome of an operation that produces a value. Value is default(T) on failure.</summary>
    public readonly struct KitResult<T>
    {
        public readonly T Value;
        public readonly KitError Error;

        /// <summary>Log text; may be null. Never shown to the user.</summary>
        public readonly string Detail;

        private KitResult(T value, KitError error, string detail)
        {
            Value = value;
            Error = error;
            Detail = detail;
        }

        public bool Ok => Error == KitError.None;

        public static KitResult<T> From(T value)
        {
            return new KitResult<T>(value, KitError.None, null);
        }

        /// <summary>Failed result. KitError.None is stored as Unknown so a Fail can never read as Ok.</summary>
        public static KitResult<T> Fail(KitError e, string detail = null)
        {
            return new KitResult<T>(default(T), e == KitError.None ? KitError.Unknown : e, detail);
        }

        public override string ToString()
        {
            if (Ok) return "Ok";
            return Detail == null ? Error.ToString() : Error + ": " + Detail;
        }
    }

    /// <summary>
    /// Reason carried by SessionState.Error and shown in the Toast/Error popup.
    /// default(SessionError) means "no error" (Code None, UserMessage null).
    /// </summary>
    public readonly struct SessionError
    {
        public readonly KitError Code;

        /// <summary>Localized-ready, short; never contains ids or secrets.</summary>
        public readonly string UserMessage;

        /// <summary>Log-only; may contain ids. Never shown in the UI.</summary>
        public readonly string Detail;

        /// <summary>
        /// userMessage null or empty → <see cref="SessionErrorMessages.For"/>(code), which honors the
        /// game's <see cref="SessionErrorMessages.Resolver"/>. A non-empty userMessage is used as given;
        /// pass a <see cref="SessionErrorMessages"/> variant (for example HostClosedLobby) to keep it resolvable.
        /// </summary>
        public SessionError(KitError code, string userMessage = null, string detail = null)
        {
            Code = code;
            UserMessage = string.IsNullOrEmpty(userMessage) ? SessionErrorMessages.For(code) : userMessage;
            Detail = detail;
        }

        public override string ToString()
        {
            return Detail == null ? Code.ToString() : Code + ": " + Detail;
        }
    }
}
