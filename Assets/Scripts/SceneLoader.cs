using UnityEngine;

public class SceneLoader : MonoBehaviour
{
    public void Load(string sceneName) => UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
}