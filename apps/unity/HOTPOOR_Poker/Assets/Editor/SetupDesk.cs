using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FairDeck;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.Build.Reporting;

public static class SetupDesk {
    public static void CreateScene() {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("SeatedEyeLevelCamera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 1.2f, -1.6f);
        camera.transform.rotation = Quaternion.Euler(20, 0, 0); camera.fieldOfView = 60;
        camera.backgroundColor = new Color(.035f,.055f,.075f); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.gameObject.AddComponent<AudioListener>();
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube); table.name = "PokerTable";
        table.transform.position = new Vector3(0,.72f,.6f); table.transform.localScale = new Vector3(2.6f,.12f,2.4f);
        Directory.CreateDirectory("Assets/Scenes");
        table.GetComponent<Renderer>().sharedMaterial = Material("Table",new Color(.035f,.19f,.145f));
        Box("TableRim",new Vector3(0,.67f,.6f),new Vector3(2.8f,.12f,2.6f),Material("Wood",new Color(.105f,.063f,.045f)));
        Box("Floor",new Vector3(0,-.1f,0),new Vector3(12,.1f,12),Material("Room",new Color(.055f,.068f,.08f)));
        Box("BackWall",new Vector3(0,1.4f,3.2f),new Vector3(12,3,.12f),Material("Room",new Color(.055f,.068f,.08f)));
        var walletObject=Box("WalletCard",new Vector3(.10f,.805f,.25f),new Vector3(.32f,.035f,.24f),Material("WalletGold",new Color(.66f,.48f,.21f)));
        var label=new GameObject("WalletLabel").AddComponent<TextMesh>();
        label.transform.SetParent(walletObject.transform,false);
        // Parent dimensions would distort text; use inverse dimensions to keep lettering proportional.
        label.transform.localPosition=new Vector3(0,.53f,0); label.transform.localRotation=Quaternion.Euler(90,0,0);
        label.transform.localScale=new Vector3(1/.32f,1/.24f,1/.035f);
        label.text="WALLET"; label.fontSize=64; label.characterSize=.009f; label.anchor=TextAnchor.MiddleCenter;
        label.color=new Color(.16f,.105f,.04f);
        var light = new GameObject("RoomLight").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50,-25,0); light.intensity = 1.2f;
        var wallet=new GameObject("WalletDesk").AddComponent<WalletDesk>();
        var hand=new GameObject("DeskHandInteraction").AddComponent<DeskHand>(); hand.wallet=wallet; hand.card=walletObject.transform;
        new GameObject("DemoSmokeCapture").AddComponent<DemoSmokeCapture>();
        RenderSettings.ambientLight=new Color(.38f,.4f,.44f);
        PlayerSettings.companyName = "HOTPOOR"; PlayerSettings.productName = "HOTPOOR FairDeck";
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/WalletDesk.unity");
        EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene("Assets/Scenes/WalletDesk.unity",true)};
        AssetDatabase.SaveAssets();
        Debug.Log("FAIRDECK_SETUP_OK");
    }
    static Material Material(string name,Color color) {
        string path="Assets/Scenes/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat) { mat=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,path); }
        mat.color=color; mat.SetFloat("_Glossiness",.18f); return mat;
    }
    static GameObject Box(string name,Vector3 position,Vector3 scale,Material material) {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name=name;
        obj.transform.position=position; obj.transform.localScale=scale; obj.GetComponent<Renderer>().sharedMaterial=material; return obj;
    }
    public static void BuildDemo() {
        CreateScene();
        PlayerSettings.defaultScreenWidth=1280; PlayerSettings.defaultScreenHeight=800;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/WalletDesk.unity"}, locationPathName="Builds/Windows/HOTPOOR FairDeck.exe",
            target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development
        });
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Windows build failed");
        Debug.Log("FAIRDECK_WINDOWS_BUILD_OK");
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
