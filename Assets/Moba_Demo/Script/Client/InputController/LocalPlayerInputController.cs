using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// 클릭 → NavMesh 이동. NetworkPlayerController가 로컬일 때만 enabled
public class LocalPlayerInputController : MonoBehaviour
{
    private PlayerControls controls;
    private NavMeshAgent agent;
    private void Awake()
    {
        controls = new PlayerControls();
        agent = GetComponent<NavMeshAgent>();

        // 액션 연결
        controls.Player.Move.performed += ctx => OnMove();
        controls.Player.Confilm.performed += ctx => OnConFlim();
        controls.Player.QSkill.performed += ctx => OnSkill(0);
        controls.Player.WSkill.performed += ctx => OnSkill(1);
        controls.Player.ESkill.performed += ctx => OnSkill(2);
        controls.Player.Rskill.performed += ctx => OnSkill(3);
        controls.Player.DSkill.performed += ctx => OnSkill(4);
        controls.Player.FSkill.performed += ctx => OnSkill(5);
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();
    
    private void OnMove()
    {
        if (agent == null || Camera.main == null || Mouse.current == null)
            return;

        Vector3 pos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(pos);

        if (!Physics.Raycast(ray, out RaycastHit hit))
            return;

        agent.SetDestination(hit.point);
    }

    private void OnConFlim()
    {
    }

    private void OnSkill(int index)
    {
    }

    public Vector2 CameraMoveIntent { get; private set; }
    public float CameraZoomIntent { get; private set; }

    // New Input System Callback
    public void OnCameraMove(InputAction.CallbackContext ctx)
    {
        CameraMoveIntent = ctx.ReadValue<Vector2>();
    }

    public void OnCameraZoom(InputAction.CallbackContext ctx)
    {
        CameraZoomIntent = ctx.ReadValue<float>();
    }

    public void ClearFrameInput()
    {
        CameraZoomIntent = 0f; // Zoom은 1프레임성
    }
}
