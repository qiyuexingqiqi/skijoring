#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Snowboard;

public sealed class SnowboardIntegrationQA : MonoBehaviour
{
    public sealed class TestInput : PlayerInputDevice
    {
        public float x,y; public bool jump,grab;
        public override float AxisX=>x; public override float AxisY=>y;
        public override bool IsJump=>jump; public override bool IsSlide=>false; public override bool IsGrab=>grab;
        public override void Draw(){} public override void SaveCurrentAsCalibration(){}
    }
    readonly StringBuilder report=new StringBuilder(); int errors; bool sawTakeoff,sawAir; void Awake(){Application.runInBackground=true;Application.logMessageReceived+=Log;} void Update(){var p=FindObjectOfType<PlayerController>();if(p){sawTakeoff|=p.State==PlayerState.TakeOff;sawAir|=p.State==PlayerState.Air;}}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init(){if(Environment.GetCommandLineArgs().Contains("-snowboardQA"))new GameObject("IntegrationQA").AddComponent<SnowboardIntegrationQA>();}
    IEnumerator Start()
    {

        yield return null;yield return null;
        var player=FindObjectOfType<PlayerController>();
        var input=new TestInput();
        var field=typeof(PlayerInput).GetField("s_devises",BindingFlags.NonPublic|BindingFlags.Static);
        ((PlayerInputDevice[][])field.GetValue(null))[(int)player.m_playerIndex]=new PlayerInputDevice[]{input};
        Vector3 start=player.transform.position;
        var dir="D:/workspace/SnowboardIntegration/QA";Directory.CreateDirectory(dir);
        for(int i=0;i<14;i++)
        {
            input.y=i<5?1:(i<9?-1:0);input.x=i==3?.12f:(i==4?-.12f:0);
            input.jump=i==10;
            if(input.jump){yield return new WaitForSeconds(.12f);input.jump=false;}
            yield return new WaitForSeconds(1);
            var a=player?player.GetComponentInChildren<Animator>():null;
            report.AppendLine($"t={i+1} position={(player?player.transform.position.ToString():"destroyed")} speed={(player?player.Velocity.magnitude:0):F2} state={(player?player.State.ToString():"destroyed")} grounded={(player&&player.IsCollidingWithTerrain)} pose={(a?a.GetInteger("pose"):-1)} errors={errors}");
            if(i==0||i==4||i==8||i==10) {yield return new WaitForEndOfFrame(); Capture(dir+"/frame-"+(i+1)+".png");}
            if(!player)break;
        }
        report.AppendLine("sawTakeoff="+sawTakeoff+" sawAir="+sawAir); report.AppendLine("distance="+(player?Vector3.Distance(start,player.transform.position):0)+" errors="+errors);
        File.WriteAllText(dir+"/runtime-report.txt",report.ToString());
        yield return new WaitForSeconds(1);
        Application.Quit(errors==0?0:1);
    }
    static void Capture(string path){var cam=Camera.main;var rt=new RenderTexture(1280,720,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());cam.targetTexture=null;RenderTexture.active=null;Destroy(rt);Destroy(tex);}
    void Log(string msg,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;report.AppendLine(type+": "+msg+"\n"+trace);}}
}
#endif
