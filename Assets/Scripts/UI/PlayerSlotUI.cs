using UnityEngine;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class PlayerSlotUI : MonoBehaviour
{
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Image avatarImage;

    private CanvasGroup canvasGroup;

    public string PlayerName { get; private set; }
    public bool IsOccupied => !string.IsNullOrEmpty(PlayerName);

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;

        if (playerNameText != null) playerNameText.text = "";
        if (avatarImage != null) avatarImage.enabled = false;
    }

    public void SetPlayer(string playerName, Sprite avatar = null)
    {
        PlayerName = playerName;
        if (playerNameText != null) playerNameText.text = playerName;

        if (avatarImage != null && avatar != null)
        {
            avatarImage.sprite = avatar;
            avatarImage.enabled = true;
        }
        else if (avatarImage != null)
        {
            avatarImage.enabled = false;
        }

        canvasGroup.alpha = 1;
    }

    public void ClearPlayer()
    {
        PlayerName =  "";
        if (playerNameText != null)
            playerNameText.text = "";

        if (avatarImage != null)
            avatarImage.enabled = false;

        canvasGroup.alpha = 0;
    }
}
