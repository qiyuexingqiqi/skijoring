using System.Collections.Generic;
using UnityEngine;

namespace SkiRace.Network
{
    public sealed class RaceRiderView : MonoBehaviour
    {
        private RiderPose target;
        private bool received;
        private Animator animator;
        private bool doubleBoard;
        private Transform model;
        private Transform spine;
        private Quaternion spinePose;
        private bool balanced;
        private TextMesh nameLabel;
        private readonly List<Material> materials = new List<Material>();

        public void Initialize(RoomMember member, RaceNetworkAssets assets)
        {
            doubleBoard = member.doubleBoard;
            name = "NetworkRider_" + member.id;
            // Keep instantiated components inactive until physics and local scripts are removed.
            var container = new GameObject("Visual");
            container.transform.SetParent(transform, false);
            container.SetActive(false);
            var prefab = doubleBoard ? assets.doubleBoardModel : assets.singleBoardModel;
            var visual = Instantiate(prefab, container.transform);
            model = visual.transform;
            foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var audio in visual.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            animator = visual.GetComponentInChildren<Animator>(true);
            if (animator != null) { animator.enabled = true; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            Color tint = Color.HSVToRGB((member.slot * .137f + .52f) % 1, .55f, .95f);
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer)) continue;
                var copies = renderer.materials;
                foreach (var material in copies)
                {
                    if (material.HasProperty("_Color")) material.color = Color.Lerp(material.color, tint, .42f);
                    materials.Add(material);
                }
            }
            foreach (var bone in visual.GetComponentsInChildren<Transform>(true))
                if (bone.name == (doubleBoard ? "spine_02" : "DEF-chest")) { spine = bone; break; }
            visual.SetActive(true);
            container.SetActive(true);
            var label = new GameObject("PlayerName");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = Vector3.up * 2.6f;
            nameLabel = label.AddComponent<TextMesh>();
            nameLabel.text = member.name;
            nameLabel.fontSize = 36;
            nameLabel.characterSize = .045f;
            nameLabel.anchor = TextAnchor.MiddleCenter;
            nameLabel.color = tint;
            nameLabel.font = RaceNetworkUI.ResolveFont(assets);
            label.GetComponent<MeshRenderer>().sharedMaterial = nameLabel.font.material;
        }

        public void Receive(RiderPose pose)
        {
            if (received && pose.round == target.round && pose.revision < target.revision) return;
            if (!received || pose.revision != target.revision || Vector3.Distance(transform.position, pose.position) > 30)
                transform.SetPositionAndRotation(pose.position, pose.rotation);
            target = pose;
            received = true;
            if (animator == null) return;
            if (!doubleBoard) animator.SetInteger("pose", pose.snowboardPose);
            if (pose.animation != 0 && animator.HasState(0, pose.animation))
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.fullPathHash != pose.animation || Mathf.Abs(state.normalizedTime - pose.animationTime) > .3f)
                    animator.Play(pose.animation, 0, pose.animationTime);
            }
        }

        private void Update()
        {
            if (balanced && spine != null) spine.localRotation = spinePose;
            balanced = false;
            if (!received) return;
            float blend = 1 - Mathf.Exp(-15 * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, target.position, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, target.rotation, blend);
            model.position = transform.position + target.visualOffset;
            model.rotation = Quaternion.Slerp(model.rotation, target.visualRotation, blend);
            if (nameLabel != null && Camera.main != null) nameLabel.transform.rotation = Camera.main.transform.rotation;
        }

        private void LateUpdate()
        {
            if (!received || spine == null) return;
            spinePose = spine.localRotation;
            balanced = true;
            float turn = target.braking ? 0 : target.turn;
            spine.rotation = Quaternion.AngleAxis(-turn * (doubleBoard ? 14 : 22), Vector3.up) * spine.rotation;
        }

        private void OnDestroy() { foreach (var material in materials) Destroy(material); }
    }

    public sealed class RaceLocalRider : MonoBehaviour
    {
        public SkiRaceNetworkManager owner;
        private CarvingController skis;
        private Snowboard.PlayerController snowboard;
        private Rigidbody body;
        private Animator animator;
        private readonly Dictionary<Rigidbody, bool> physicsDefaults = new Dictionary<Rigidbody, bool>();
        private double nextSend;
        private uint sequence;
        private bool locked;

        private void Awake()
        {
            skis = GetComponent<CarvingController>();
            snowboard = GetComponent<Snowboard.PlayerController>();
            body = GetComponent<Rigidbody>();
        }

        public void SetLocked(bool value)
        {
            if (skis != null) skis.RaceInputLocked = value;
            if (snowboard != null) snowboard.RaceInputLocked = value;
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                if (!physicsDefaults.ContainsKey(rb)) physicsDefaults.Add(rb, rb.isKinematic);
                if (value && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                rb.isKinematic = value || physicsDefaults[rb];
            }
            locked = value;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (skis != null) skis.ResetForRace(position, rotation);
            if (snowboard != null) snowboard.ResetForRace(position, rotation);
            Physics.SyncTransforms();
        }

        private void LateUpdate()
        {
            if (owner == null || !owner.InRoom || Time.realtimeSinceStartupAsDouble < nextSend) return;
            nextSend = Time.realtimeSinceStartupAsDouble + .05;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            var state = animator != null ? animator.GetCurrentAnimatorStateInfo(0) : default;
            var visual = animator != null ? animator.transform : transform;
            owner.SendPose(new RiderPose
            {
                id = owner.LocalId, round = owner.Round, revision = owner.LocalRevision, sequence = ++sequence,
                position = transform.position, rotation = transform.rotation,
                velocity = body != null && !locked ? body.velocity : Vector3.zero,
                visualOffset = visual.position - transform.position, visualRotation = visual.rotation,
                turn = skis != null ? skis.VisualTurnInput : (snowboard != null ? Mathf.Clamp(snowboard.FrameRotY / Mathf.Max(.001f, Time.deltaTime) / 60, -1, 1) : 0),
                braking = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow),
                animation = state.fullPathHash, animationTime = state.normalizedTime,
                snowboardPose = snowboard != null && animator != null ? animator.GetInteger("pose") : 0
            });
        }

        private void OnDestroy()
        {
            if (skis != null) skis.RaceInputLocked = false;
            if (snowboard != null) snowboard.RaceInputLocked = false;
            foreach (var pair in physicsDefaults) if (pair.Key != null) pair.Key.isKinematic = pair.Value;
        }
    }
}
