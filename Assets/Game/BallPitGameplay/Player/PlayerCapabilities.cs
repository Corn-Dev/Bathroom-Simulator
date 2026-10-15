// PlayerCapabilities: the per-player stats B's upgrades change (deliverable A, file plan P4; design c 14).
// Multipliers on the PlayerConfig base values (move speed, climb speed, reach, throw power) plus a carry capacity.
// Server-written NetworkVariables, so every peer reads the same values; A only reads them.
using Unity.Netcode;
using UnityEngine;

namespace BallPitGameplay
{
    /// <summary>What the motor and climber read.</summary>
    public interface IPlayerStats
    {
        float MoveSpeed { get; }
        float ClimbSpeed { get; }
    }

    public enum PlayerStat { MoveSpeed, ClimbSpeed, Reach, ThrowPower, CarryCapacity }

    public sealed class PlayerCapabilities : NetworkBehaviour, IPlayerStats
    {
        public const float MinMultiplier = 0.1f, MaxMultiplier = 10f;
        public const int MinCarry = 1, MaxCarry = 64;

        [Tooltip("Starting multipliers (1 = the PlayerConfig value). The server writes them at spawn.")]
        [SerializeField] private float baseMoveSpeed = 1f, baseClimbSpeed = 1f, baseReach = 1f, baseThrowPower = 1f;
        [Tooltip("Starting carry capacity (balls). A's arms carry one.")]
        [SerializeField] private int baseCarryCapacity = 1;

        private readonly NetworkVariable<float> moveSpeed = new NetworkVariable<float>(1f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> climbSpeed = new NetworkVariable<float>(1f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> reach = new NetworkVariable<float>(1f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> throwPower = new NetworkVariable<float>(1f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> carryCapacity = new NetworkVariable<int>(1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Raised on every peer when any stat changes.</summary>
        public event System.Action Changed;

        public float MoveSpeed => Multiplier(moveSpeed.Value);
        public float ClimbSpeed => Multiplier(climbSpeed.Value);
        public float Reach => Multiplier(reach.Value);
        public float ThrowPower => Multiplier(throwPower.Value);
        public int CarryCapacity => Mathf.Clamp(carryCapacity.Value, MinCarry, MaxCarry);

        public override void OnNetworkSpawn()
        {
            moveSpeed.OnValueChanged += OnFloatChanged;
            climbSpeed.OnValueChanged += OnFloatChanged;
            reach.OnValueChanged += OnFloatChanged;
            throwPower.OnValueChanged += OnFloatChanged;
            carryCapacity.OnValueChanged += OnIntChanged;
            if (IsServer)
            {
                moveSpeed.Value = Multiplier(baseMoveSpeed);
                climbSpeed.Value = Multiplier(baseClimbSpeed);
                reach.Value = Multiplier(baseReach);
                throwPower.Value = Multiplier(baseThrowPower);
                carryCapacity.Value = Mathf.Clamp(baseCarryCapacity, MinCarry, MaxCarry);
            }
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            moveSpeed.OnValueChanged -= OnFloatChanged;
            climbSpeed.OnValueChanged -= OnFloatChanged;
            reach.OnValueChanged -= OnFloatChanged;
            throwPower.OnValueChanged -= OnFloatChanged;
            carryCapacity.OnValueChanged -= OnIntChanged;
        }

        /// <summary>Host only (B's upgrade effects). Values are clamped: multipliers 0.1-10, carry 1-64. Elsewhere: Warning, ignored.</summary>
        public void ServerSet(PlayerStat stat, float value)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"[PlayerCapabilities] ServerSet({stat}) ignored: only the host writes player stats", this);
                return;
            }
            switch (stat)
            {
                case PlayerStat.MoveSpeed: moveSpeed.Value = Multiplier(value); break;
                case PlayerStat.ClimbSpeed: climbSpeed.Value = Multiplier(value); break;
                case PlayerStat.Reach: reach.Value = Multiplier(value); break;
                case PlayerStat.ThrowPower: throwPower.Value = Multiplier(value); break;
                case PlayerStat.CarryCapacity:
                    carryCapacity.Value = float.IsNaN(value) || float.IsInfinity(value)
                        ? MinCarry : Mathf.Clamp(Mathf.RoundToInt(value), MinCarry, MaxCarry);
                    break;
            }
        }

        private void OnFloatChanged(float previous, float current) => Changed?.Invoke();
        private void OnIntChanged(int previous, int current) => Changed?.Invoke();

        // A non-finite multiplier reads as 1; otherwise clamped to [MinMultiplier, MaxMultiplier].
        private static float Multiplier(float v) =>
            float.IsNaN(v) || float.IsInfinity(v) ? 1f : Mathf.Clamp(v, MinMultiplier, MaxMultiplier);
    }
}
