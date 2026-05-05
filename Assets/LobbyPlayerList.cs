using System.Collections.Generic;
using UnityEngine;

public class LobbyPlayerList : MonoBehaviour
{
    [Header("Player Slots (Max 8)")]
    public PlayerSlotUI[] playerSlots = new PlayerSlotUI[8]; // assign slots in inspector

    /// <summary>
    /// Add a player to the first available slot.
    /// </summary>
    public void AddPlayer(string playerName, Sprite avatar = null)
    {
        foreach (var slot in playerSlots)
        {
            if (!slot.IsOccupied)
            {
                slot.SetPlayer(playerName, avatar);
                return;
            }
        }
        Debug.LogWarning("Lobby is full! Cannot add more players.");
    }

    /// <summary>
    /// Remove a player by name.
    /// </summary>
    public void RemovePlayer(string playerName)
    {
        foreach (var slot in playerSlots)
        {
            if (slot.PlayerName == playerName)
            {
                slot.ClearPlayer();
                return;
            }
        }
        Debug.LogWarning($"Player {playerName} not found in the list.");
    }

    public void RefreshUI(List<(string playerName, Sprite avatar)> players)
    {
        ClearList();

        foreach (var (name, avatar) in players)
        {
            AddPlayer(name, avatar);
        }
    }

    /// <summary>
    /// Clear all slots.
    /// </summary>
    public void ClearList()
    {
        foreach (var slot in playerSlots)
            slot.ClearPlayer();
    }
}
