using UnityEngine;

public class VoiceBootstrap : MonoBehaviour
{
    private void Start()
    {
#if UNITY_ANDROID
        RealWearVoiceController.Instance.StartListening();
#else
        Debug.Log("[RWVoice] Голос работает только на Android-устройстве.");
#endif
    }
}