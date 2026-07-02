using UnityEngine;
using TMPro;

/// <summary>
/// Displays the build version in the corner of the screen.
/// Reads from Project Settings → Player → Version.
/// Update that field before each build to keep it current.
/// </summary>
public class VersionDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI versionText;

    private void Start()
    {
        if (versionText != null)
            versionText.text = $"v{Application.version}";
    }
}
