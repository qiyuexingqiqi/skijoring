#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Editor validation input; feeds the real StandaloneInputModule without OS mouse automation.</summary>
public sealed class EquipmentSelectionPointerInput : BaseInput
{
    public Vector2 position;
    public bool down;
    public bool held;
    public bool up;
    public override bool mousePresent => true;
    public override Vector2 mousePosition => position;
    public override Vector2 mouseScrollDelta => Vector2.zero;
    public override bool touchSupported => false;
    public override int touchCount => 0;
    public override bool GetMouseButtonDown(int button) => button == 0 && down;
    public override bool GetMouseButtonUp(int button) => button == 0 && up;
    public override bool GetMouseButton(int button) => button == 0 && held;
    public override float GetAxisRaw(string axisName) => 0f;
    public override bool GetButtonDown(string buttonName) => false;
}
#endif
