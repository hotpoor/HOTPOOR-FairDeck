using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FairDeck;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupDesk {
    public static void CreateScene() {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("SeatedEyeLevelCamera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 1.2f, -1.6f);
        camera.transform.rotation = Quaternion.Euler(12, 0, 0); camera.fieldOfView = 60;
        camera.backgroundColor = new Color(.035f,.055f,.075f); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.gameObject.AddComponent<AudioListener>();
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube); table.name = "PokerTable";
        table.transform.position = new Vector3(0,.72f,.6f); table.transform.localScale = new Vector3(2.6f,.12f,2.4f);
        var mat = new Material(Shader.Find("Standard")); mat.color = new Color(.04f,.25f,.18f);
        Directory.CreateDirectory("Assets/Scenes"); AssetDatabase.CreateAsset(mat, "Assets/Scenes/Table.mat"); table.GetComponent<Renderer>().sharedMaterial = mat;
        var light = new GameObject("RoomLight").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50,-25,0); light.intensity = 1.2f;
        new GameObject("WalletDesk").AddComponent<WalletDesk>();
        PlayerSettings.companyName = "HOTPOOR"; PlayerSettings.productName = "HOTPOOR FairDeck";
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/WalletDesk.unity");
        EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene("Assets/Scenes/WalletDesk.unity",true)};
        AssetDatabase.SaveAssets();
        ValidateWallet();
        Debug.Log("FAIRDECK_SETUP_OK");
    }
    [Serializable] class ProofFixture { public string address, modulus, exponent, message, signature; }
    public static void ValidateWallet() {
        string temp = Path.Combine(Path.GetTempPath(), "fairdeck-wallet-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var wallet = LocalWallet.Create(temp, "Temporary test password 123!");
            var file = Path.Combine(temp,wallet.address + ".json");
            var loaded = LocalWallet.Read(file);
            string message = "FairDeck interoperability fixture";
            string signature = LocalWallet.Sign(loaded, "Temporary test password 123!", message);
            using (var rsa = new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp = false;
                rsa.ImportParameters(new RSAParameters {Modulus=Convert.FromBase64String(loaded.modulus),Exponent=Convert.FromBase64String(loaded.exponent)});
                if (!rsa.VerifyData(Encoding.UTF8.GetBytes(message),CryptoConfig.MapNameToOID("SHA256"),Convert.FromBase64String(signature))) throw new Exception("Signature failed");
            }
            bool rejected = false;
            try {LocalWallet.Sign(loaded,"wrong password",message);} catch (CryptographicException) {rejected=true;}
            if (!rejected) throw new Exception("Wrong password accepted");
            loaded.ciphertext = Convert.ToBase64String(new byte[32]); rejected=false;
            try {LocalWallet.Sign(loaded,"Temporary test password 123!",message);} catch (CryptographicException) {rejected=true;}
            if (!rejected) throw new Exception("Tampering accepted");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/wallet-proof.json",JsonUtility.ToJson(new ProofFixture {address=wallet.address,modulus=wallet.modulus,exponent=wallet.exponent,message=message,signature=signature}));
            Debug.Log("FAIRDECK_WALLET_TESTS_OK");
        } finally {
            foreach (var file in Directory.GetFiles(temp)) File.Delete(file);
            Directory.Delete(temp);
        }
    }
}
