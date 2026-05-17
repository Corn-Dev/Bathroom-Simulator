using UnityEngine;

public class DontDestroyNetworkManager : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}