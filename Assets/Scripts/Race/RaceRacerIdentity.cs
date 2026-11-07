using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 挂在选手根物体上，用来把碰撞体映射回比赛 id。
    /// 网络接入后由适配器写入 PlayerRef 对应的 id，比赛核心不需要认识 Fusion。
    /// </summary>
    public sealed class RaceRacerIdentity : MonoBehaviour
    {
        public int racerId;
        public string displayName;
        public bool isLocalPlayer;
    }
}
