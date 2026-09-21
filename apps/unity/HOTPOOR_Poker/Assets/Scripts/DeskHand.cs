using System;
using UnityEngine;

namespace FairDeck {
    // Procedural blockout: a visible palm, wrist and articulated fingers.
    public class DeskHand : MonoBehaviour {
        public WalletDesk wallet;
        public Transform card;
        Transform hand;
        Transform[] joints = new Transform[10];
        Quaternion[] resting = new Quaternion[10];
        Vector3 cardHome; Quaternion cardRotation;
        Material cardMaterial;
        bool hovered, held; float grip, lift;
        public bool Holding { get { return held; } }
        public bool Hovered { get { return hovered; } }

        void Start() {
            cardHome = card.position; cardRotation = card.rotation;
            cardMaterial = card.GetComponent<Renderer>().material;
            hand = new GameObject("MouseControlledRightHand").transform;
            var skin = new Material(Shader.Find("Standard")); skin.color = new Color(.69f,.43f,.29f);
            skin.SetFloat("_Glossiness",.27f);
            var cuff = new Material(Shader.Find("Standard")); cuff.color = new Color(.065f,.095f,.12f);
            Part("Palm", hand, new Vector3(0,0,0), new Vector3(.15f,.065f,.19f), skin);
            Part("Wrist", hand, new Vector3(0,-.008f,-.15f), new Vector3(.115f,.075f,.19f), skin);
            Part("Sleeve", hand, new Vector3(0,-.01f,-.33f), new Vector3(.16f,.12f,.26f), cuff);
            for (int finger=0; finger<4; finger++) {
                float length = new float[]{.068f,.079f,.073f,.057f}[finger];
                Transform root = new GameObject("Finger"+finger).transform;
                root.SetParent(hand,false); root.localPosition = new Vector3(-.055f+finger*.037f,0,.071f);
                joints[finger*2]=root; resting[finger*2]=root.localRotation;
                Part("Proximal",root,new Vector3(0,0,length*.5f),new Vector3(.034f,.043f,length+.017f),skin);
                Transform tip = new GameObject("Knuckle").transform;
                tip.SetParent(root,false); tip.localPosition=new Vector3(0,0,length);
                joints[finger*2+1]=tip; resting[finger*2+1]=tip.localRotation;
                Part("Fingertip",tip,new Vector3(0,0,length*.43f),new Vector3(.03f,.037f,length),skin);
            }
            Transform thumb = new GameObject("Thumb").transform;
            thumb.SetParent(hand,false); thumb.localPosition=new Vector3(-.069f,-.015f,-.01f);
            thumb.localRotation=Quaternion.Euler(0,-55,0); joints[8]=thumb; resting[8]=thumb.localRotation;
            Part("ThumbBase",thumb,new Vector3(0,0,.033f),new Vector3(.046f,.046f,.09f),skin);
            Transform end=new GameObject("ThumbJoint").transform; end.SetParent(thumb,false); end.localPosition=new Vector3(0,0,.068f);
            joints[9]=end; resting[9]=end.localRotation;
            Part("ThumbTip",end,new Vector3(0,0,.028f),new Vector3(.039f,.042f,.068f),skin);
            hand.position=new Vector3(.48f,.88f,-.45f);
        }
        static void Part(string name,Transform parent,Vector3 position,Vector3 scale,Material material) {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere); obj.name=name;
            obj.transform.SetParent(parent,false); obj.transform.localPosition=position; obj.transform.localScale=scale;
            obj.GetComponent<Renderer>().sharedMaterial=material; Destroy(obj.GetComponent<Collider>());
        }
        void Update() {
            if (!hand || !card || !wallet) return;
            if (held && !wallet.IsOpen && lift>=.99f) held=false;
            if (Input.GetKeyDown(KeyCode.Escape) && wallet.IsOpen) {
                wallet.ClosePanel();
                if(!wallet.IsOpen) held=false;
            }
            var plane=new Plane(Vector3.up,new Vector3(0,.84f,0));
            var ray=Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 target=hand.position;
            hovered=false;
            if (!held && plane.Raycast(ray,out float distance)) {
                Vector3 point=ray.GetPoint(distance);
                hovered=Mathf.Abs(point.x-cardHome.x)<.21f && Mathf.Abs(point.z-cardHome.z)<.20f;
                target=new Vector3(Mathf.Clamp(point.x,-1.05f,1.05f),.88f,Mathf.Clamp(point.z-.15f,-.65f,1.35f));
                if (hovered && Input.GetMouseButtonDown(0)) BeginPickup();
            }
            if (held) target=Vector3.Lerp(new Vector3(cardHome.x,.88f,cardHome.z-.15f),new Vector3(-.38f,1.05f,-.37f),lift);
            hand.position=Vector3.Lerp(hand.position,target,1-Mathf.Exp(-12*Time.deltaTime));
            grip=Mathf.MoveTowards(grip,held?1:0,Time.deltaTime*4);
            lift=Mathf.MoveTowards(lift,held?1:0,Time.deltaTime*2.5f);
            for (int i=0;i<joints.Length;i++) joints[i].localRotation=resting[i]*Quaternion.Euler((i%2==0?28:65)*grip,0,0);
            if (held) {
                card.position=Vector3.Lerp(cardHome,hand.position+new Vector3(0,-.047f,.135f),grip);
                card.rotation=Quaternion.Slerp(cardRotation,Quaternion.Euler(-18,0,-7),lift);
                if (lift>=.99f && !wallet.IsOpen) wallet.OpenPanel();
            } else {
                card.position=Vector3.Lerp(card.position,cardHome,1-Mathf.Exp(-14*Time.deltaTime));
                card.rotation=Quaternion.Slerp(card.rotation,cardRotation,1-Mathf.Exp(-14*Time.deltaTime));
            }
            cardMaterial.color=hovered?new Color(.92f,.75f,.38f):new Color(.66f,.48f,.21f);
            Cursor.visible=wallet.IsOpen || !Application.isFocused;
        }
        public void BeginPickup() { if (held || wallet.IsOpen) return; held=true; lift=0; }
        void OnDisable() { Cursor.visible=true; }
        void OnGUI() {
            if (!wallet || wallet.IsOpen) return;
            var title=new GUIStyle(GUI.skin.label){fontSize=26,alignment=TextAnchor.MiddleCenter};
            title.normal.textColor=new Color(.9f,.86f,.7f);
            GUI.Label(new Rect(0,28,Screen.width,40),"HOTPOOR FairDeck",title);
            var hint=new GUIStyle(GUI.skin.box){fontSize=19,alignment=TextAnchor.MiddleCenter};
            GUI.Box(new Rect(Screen.width*.5f-285,Screen.height-88,570,54),held?"拿起钱包卡片…":hovered?"点击左键 · 拿起钱包卡片":"移动鼠标控制手 · 伸向桌上的金色钱包卡片",hint);
        }
    }
}
