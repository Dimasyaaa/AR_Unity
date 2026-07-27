//using System.Collections;
//using System.IO;
//using UnityEngine;
//using Siccity.GLTFUtility;

//public class QRInventoryManager : MonoBehaviour
//{
//    [SerializeField] private float modelScale = 0.1f;
//    private GameObject _currentModel;

//    void Start()
//    {
//        if (QRScanner.Instance != null)
//            QRScanner.Instance.OnQRScanned += OnQRScanned;
//    }

//    void OnDestroy()
//    {
//        if (QRScanner.Instance != null)
//            QRScanner.Instance.OnQRScanned -= OnQRScanned;
//    }

//    private void OnQRScanned(string qrData)
//    {
//        Debug.Log($"[QR] Scanned: {qrData}");
//        StartCoroutine(LoadAndPlaceModel(qrData));
//    }

//    private IEnumerator LoadAndPlaceModel(string qrData)
//    {
//        string cacheDir = Path.Combine(Application.persistentDataPath, "ARModels");
//        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

//        string localPath = Path.Combine(cacheDir, $"{qrData.GetHashCode()}.glb");

//        // Для теста используем фиктивную модель
//        string testUrl = "https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Models/master/2.0/Duck/glTF-Binary/Duck.glb";

//        if (!File.Exists(localPath))
//        {
//            using var request = UnityWebRequest.Get(testUrl);
//            yield return request.SendWebRequest();
//            if (request.result == UnityWebRequest.Result.Success)
//                File.WriteAllBytes(localPath, request.downloadHandler.data);
//        }

//        var model = Importer.LoadFromFile(localPath);
//        if (model != null)
//        {
//            _currentModel = model;
//            _currentModel.transform.localScale = Vector3.one * modelScale;
//            _currentModel.transform.position = Vector3.down * 1.5f; // Размещаем перед камерой
//        }
//    }
//}