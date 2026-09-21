using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace FairDeck {
    // Runs only when explicitly launched with the development smoke-test switch.
    public class DemoSmokeCapture : MonoBehaviour {
        IEnumerator Start() {
            if(!Debug.isDebugBuild || Array.IndexOf(Environment.GetCommandLineArgs(),"--fairdeck-smoke")<0) yield break;
            Application.runInBackground=true;
            string folder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Screenshots");
            Directory.CreateDirectory(folder);
            yield return new WaitForSeconds(2);
            var wallet=FindFirstObjectByType<WalletDesk>(); var hand=FindFirstObjectByType<DeskHand>();
            if(wallet.IsOpen) { Debug.LogError("SMOKE: wallet should start closed"); Application.Quit(1); yield break; }
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(folder,"01-desk.png"));
            yield return new WaitForSeconds(1); hand.BeginPickup();
            yield return new WaitForSeconds(2);
            if(!hand.Holding || !wallet.IsOpen) { Debug.LogError("SMOKE: pickup did not open wallet"); Application.Quit(1); yield break; }
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(folder,"02-wallet.png"));
            yield return new WaitForSeconds(1); wallet.ClosePanel();
            yield return new WaitForSeconds(1);
            if(hand.Holding || wallet.IsOpen) { Debug.LogError("SMOKE: card did not return"); Application.Quit(1); yield break; }
            Debug.Log("FAIRDECK_HAND_SMOKE_OK"); Application.Quit(0);
        }
    }
}
