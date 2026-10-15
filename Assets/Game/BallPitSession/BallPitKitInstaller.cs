// Menu-scene component that registers the ball-pit adapter with the kit before the bootstrap initializes, wires the
// Steam readiness probe, and hosts the adapter's coroutines (client sync watchdog). Put it alone on a root GameObject
// in the menu scene; it survives scene loads. Design: 06 §1 (adapter registration, SteamManager probe), 06 G2,
// 02 §1 step 2, 04 §8. The only kit-adjacent file that names SteamManager (Assembly-CSharp, Steamworks.NET script).

using MultiplayerKit.Ngo;
using MultiplayerKit.Steam;
using UnityEngine;

namespace BallPitSession
{
    [DefaultExecutionOrder(-1000)] // before MultiplayerKitBootstrap (-500), which consumes SetAdapter in its Awake
    public sealed class BallPitKitInstaller : MonoBehaviour
    {
        [Tooltip("NGO gameplay scene; must be in Build Settings.")]
        [SerializeField] private string gameplaySceneName = "BallPitGame";

        [Tooltip("Menu scene the kit returns to after a session.")]
        [SerializeField] private string menuSceneName = "MainMenu";

        /// <summary>The live installer; null before the first Awake.</summary>
        public static BallPitKitInstaller Instance { get; private set; }

        /// <summary>The adapter registered with the kit (null on a destroyed duplicate).</summary>
        public BallPitGameAdapter Adapter { get; private set; }

        private void Awake()
        {
            // The menu scene loads again after every session: its copy removes itself before touching anything.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Kit asmdefs cannot see SteamManager (a loose Assembly-CSharp script), so it is handed over here.
            SteamPlatform.ReadyProbe = () => SteamManager.Initialized;
            Adapter = new BallPitGameAdapter(this, gameplaySceneName, menuSceneName);
            MultiplayerKitBootstrap.SetAdapter(Adapter);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
