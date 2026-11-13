using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

[Serializable]
public class InputData
{
    public float externalSteerInput;   // 现在与发送端字段一致
}

public class UdpReceiver : MonoBehaviour
{
    [Header("网络设置")]
    public int port = 8888;

    private UdpClient client;
    private Thread receiveThread;
    private volatile bool isRunning;

    private SkiController skiController;   // 引用滑雪控制器

    void Start()
    {
        // 获取同一物体（或子物体）上的 SkiController
        skiController = GetComponent<SkiController>();
        if (skiController == null)
        {
            Debug.LogError("UdpReceiver 没有找到 SkiController，请放在同一物体上或手动拖拽赋值。");
        }

        try { client = new UdpClient(port); }
        catch (SocketException)
        {
            Debug.LogWarning("外部传感器端口 " + port + " 已被使用，本实例继续使用键盘控制。");
            return;
        }
        isRunning = true;
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("UDP 接收端已启动，监听端口: " + port);
    }

    void Update()
    {
        // 不再直接旋转物体，完全交给 SkiController 处理
    }

    private void ReceiveData()
    {
        try
        {
            IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                if (client.Available > 0)
                {
                    byte[] data = client.Receive(ref anyIP);
                    string json = System.Text.Encoding.UTF8.GetString(data);

                    InputData receivedData = JsonUtility.FromJson<InputData>(json);
                    // 将值写入 SkiController 的外部输入口
                    if (skiController != null)
                    {
                        skiController.externalSteerInput = receivedData.externalSteerInput;
                    }
                }
                Thread.Sleep(1);
            }
        }
        catch (Exception e)
        {
            if (isRunning) Debug.LogWarning("外部传感器接收已停止：" + e.Message);
        }
        finally
        {
            if (client != null) client.Close();
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (client != null) client.Close();
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(200);
        receiveThread = null;
        client = null;
    }

    void OnDestroy() { OnApplicationQuit(); }
}
