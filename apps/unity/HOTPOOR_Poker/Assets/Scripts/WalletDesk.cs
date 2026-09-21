using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FairDeck {
    [Serializable] public class ApiConfig { public string apiBase = "https://blockchain.xialiwei.com"; }
    [Serializable] class ChallengeRequest { public string address, modulus, exponent; }
    [Serializable] class Challenge { public string challenge_id, message, address; }
    [Serializable] class VerifyRequest { public string challenge_id, signature; }
    [Serializable] class Session { public string address, token; public int expires_in; }

    public class WalletDesk : MonoBehaviour {
        string[] files = new string[0]; int selected = -1;
        string password = "", confirmation = "", status = "请创建钱包，或选择一个已有钱包。";
        string directory; string activeAddress, sessionToken; DateTime expires;
        ApiConfig config; bool busy; Vector2 scroll;
        public bool IsOpen { get; private set; }
        public void OpenPanel() { IsOpen=true; }
        public void ClosePanel() { if(busy) return; password=confirmation=""; IsOpen=false; }
        void Start() {
            directory = LocalWallet.DirectoryPath;
            var asset = Resources.Load<TextAsset>("client-config");
            config = asset ? JsonUtility.FromJson<ApiConfig>(asset.text) : new ApiConfig();
            try { RefreshFiles(); } catch(Exception) { status="钱包目录不可访问，请检查游戏目录权限。"; }
        }
        void RefreshFiles() {
            Directory.CreateDirectory(directory); files = Directory.GetFiles(directory, "*.json");
            Array.Sort(files); if (selected >= files.Length) selected = -1;
        }
        void OnGUI() {
            if (!IsOpen) return;
            GUI.skin.label.fontSize = 17; GUI.skin.button.fontSize = 16;
            float width = Mathf.Min(540, Screen.width - 30);
            GUILayout.BeginArea(new Rect(Screen.width-width-24, 24, width, Screen.height - 48), GUI.skin.box);
            GUILayout.Label("你的钱包 · 你的牌桌身份");
            GUI.enabled=!busy;
            if(GUILayout.Button("放回桌面 · Esc")) ClosePanel();
            GUI.enabled=true;
            GUILayout.Space(12);
            if (sessionToken != null && DateTime.UtcNow >= expires) { sessionToken = null; activeAddress = null; status = "会话已过期，请重新验证。"; }
            GUI.enabled = !busy && sessionToken == null;
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(135));
            if(files.Length==0) GUILayout.Label("桌上还没有钱包。\n在下方设置密码，创建你的第一个钱包。");
            for (int i = 0; i < files.Length; i++) {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                if (GUILayout.Toggle(selected == i, name, GUI.skin.button)) selected = i;
            }
            GUILayout.EndScrollView();
            GUILayout.Label("钱包密码（至少 12 字符，只在本机使用）");
            password = GUILayout.PasswordField(password, '*', 256);
            GUILayout.Label("创建时再次输入密码");
            confirmation = GUILayout.PasswordField(confirmation, '*', 256);
            if (GUILayout.Button("创建新钱包")) Create();
            if (GUILayout.Button("刷新钱包列表")) { try { RefreshFiles(); } catch(Exception) { status="无法读取钱包目录。"; } }
            GUI.enabled = !busy && sessionToken == null && selected >= 0;
            if (GUILayout.Button("使用所选钱包并在线验证")) StartCoroutine(Login());
            GUI.enabled = !busy;
            if (sessionToken != null) {
                GUILayout.Label("当前唯一身份：" + activeAddress);
                if (GUILayout.Button("结束测试会话")) { sessionToken = null; activeAddress = null; status = "测试会话已结束。"; }
            }
            if (GUILayout.Button("复制钱包目录路径")) GUIUtility.systemCopyBuffer = directory;
            GUI.enabled = true;
            GUILayout.Space(12); GUILayout.Label(status);
            GUILayout.Label("备份：复制 wallet 中的 JSON 文件，并牢记密码。私钥和密码不会上传。\n当前是身份原型，尚无扑克对局、共识或终局解密。");
            GUILayout.EndArea();
        }
        async void Create() {
            if (password != confirmation) { status = "两次密码不一致。"; return; }
            busy = true; status = "正在本机生成并加密钱包…";
            string secret = password; password = confirmation = "";
            try { var wallet = await Task.Run(() => LocalWallet.Create(directory, secret)); RefreshFiles(); selected = Array.FindIndex(files, f => Path.GetFileNameWithoutExtension(f) == wallet.address); status = "钱包已保存。重新输入密码以在线验证。"; }
            catch (Exception) { status = "创建失败：请检查密码长度、目录写入权限或现有文件冲突。"; }
            finally { secret = null; busy = false; }
        }
        UnityWebRequest Post(string path, object value) {
            var uri = new Uri(config.apiBase.TrimEnd('/') + path);
            if (uri.Scheme != "https") throw new InvalidOperationException("HTTPS required");
            var request = new UnityWebRequest(uri, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(value)));
            request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 20;
            request.SetRequestHeader("Content-Type", "application/json"); return request;
        }
        IEnumerator Login() {
            busy = true; status = "正在获取线上挑战…";
            WalletFile wallet = null; UnityWebRequest request = null;
            try { wallet = LocalWallet.Read(files[selected]); request = Post("/v1/wallets/challenge", new ChallengeRequest { address = wallet.address, modulus = wallet.modulus, exponent = wallet.exponent }); }
            catch (Exception) { status = "钱包文件或服务地址无效。"; busy = false; yield break; }
            using (request) {
                yield return request.SendWebRequest();
                if (request.responseCode == 409) { status = "地址冲突：请创建新钱包；旧文件保持不变。"; busy = false; yield break; }
                if (request.result != UnityWebRequest.Result.Success) { status = "线上服务尚不可用或请求被拒绝，本地钱包仍已保存。"; busy = false; yield break; }
                Challenge challenge = null;
                try {
                    challenge = JsonUtility.FromJson<Challenge>(request.downloadHandler.text);
                    var audience = new Uri(config.apiBase).Host;
                    if (challenge.address != wallet.address || challenge.message.Length > 2048 ||
                        !challenge.message.StartsWith("FairDeck login\nnetwork=fairdeck-devnet-v1\naudience=" + audience + "\naddress=" + wallet.address + "\nid=" + challenge.challenge_id + "\nnonce=")) throw new Exception();
                } catch (Exception) { status = "挑战内容不匹配，已拒绝签名。"; busy = false; yield break; }
                status = "在本机解锁并签名…";
                string secret = password; password = confirmation = "";
                var signing = Task.Run(() => LocalWallet.Sign(wallet, secret, challenge.message));
                while (!signing.IsCompleted) yield return null;
                secret = null;
                if (signing.IsFaulted) { status = "密码错误或钱包文件已被修改。"; busy = false; yield break; }
                using (var verifying = Post("/v1/wallets/verify", new VerifyRequest { challenge_id = challenge.challenge_id, signature = signing.Result })) {
                    yield return verifying.SendWebRequest();
                    if (verifying.responseCode == 409) { status = "地址冲突：请创建新钱包。"; busy = false; yield break; }
                    if (verifying.result != UnityWebRequest.Result.Success) { status = "验证失败，请重新获取挑战。"; busy = false; yield break; }
                    try { var session = JsonUtility.FromJson<Session>(verifying.downloadHandler.text); if (session.address != wallet.address || String.IsNullOrEmpty(session.token)) throw new Exception(); activeAddress = session.address; sessionToken = session.token; expires = DateTime.UtcNow.AddSeconds(session.expires_in); status = "验证成功。本次测试会话只使用这一个钱包。"; }
                    catch (Exception) { status = "服务响应无效。"; }
                }
            }
            busy = false;
        }
    }
}
