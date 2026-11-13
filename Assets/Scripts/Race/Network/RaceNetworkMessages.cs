using Mirror;
using UnityEngine;

namespace SkiRace.Network
{
    public struct JoinRaceRoom : NetworkMessage
    {
        public int protocol;
        public string roomCode;
        public string displayName;
        public bool doubleBoard;
    }

    public struct RoomAction : NetworkMessage { public byte action; }
    public struct RoomRejected : NetworkMessage { public string reason; }

    [System.Serializable]
    public struct RoomMember
    {
        public int id;
        public string name;
        public bool doubleBoard;
        public bool ready;
        public int slot;
    }

    public struct RoomSnapshot : NetworkMessage
    {
        public string code;
        public int capacity;
        public int hostId;
        public int yourId;
        public RoomMember[] members;
    }

    // The room server owns phase, checkpoints, finish times and reset revisions.
    public struct RaceSnapshot : NetworkMessage
    {
        public int round;
        public RacePhase phase;
        public double time;
        public float countdown;
        public RaceRankRow[] rows;
    }

    public struct RiderPose : NetworkMessage
    {
        public int id;
        public int round;
        public int revision;
        public uint sequence;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 visualOffset;
        public Quaternion visualRotation;
        public float turn;
        public bool braking;
        public int animation;
        public float animationTime;
        public int snowboardPose;
    }

    public struct ResetRider : NetworkMessage
    {
        public int round;
        public int revision;
        public Vector3 position;
        public Quaternion rotation;
    }
}
