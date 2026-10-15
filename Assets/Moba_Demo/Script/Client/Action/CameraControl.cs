using UnityEngine;
using UnityEngine.InputSystem;

public class CameraControl : MonoBehaviour
{
    [Header("References")]
    [SerializeField] LocalPlayerInputController input;

    [Header("Follow")]
    [SerializeField] bool followLocalPlayer = true; // GameSessionBootstrap.BindLocalPlayer로 타겟 잡음
    [SerializeField] Vector3 followOffset = new(0f, 18f, -12f);
    [SerializeField] float followLerpSpeed = 6f;

    [Header("Move")]
    [SerializeField] float moveSpeed = 15f;
    [SerializeField] float edgeSize = 20f;

    [Header("Zoom")]
    [SerializeField] float zoomSpeed = 5f;
    [SerializeField] float minHeight = 10f;
    [SerializeField] float maxHeight = 40f;

    private Transform _followTarget;

    public void BindLocalPlayer(Transform followTarget, LocalPlayerInputController localInput)
    {
        _followTarget = followTarget;
        input = localInput;
    }

    void LateUpdate()
    {
        if (followLocalPlayer && _followTarget != null)
        {
            Vector3 desired = _followTarget.position + followOffset;
            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * followLerpSpeed);
        }

        if (input == null)
            return;

        MoveCamera(GetEdgeScrollMove());
        HandleZoom();
        input.ClearFrameInput();
    }

    Vector3 GetEdgeScrollMove()
    {
        if (Mouse.current == null)
            return Vector3.zero;

        Vector3 dir = Vector3.zero;
        Vector2 mousePos = Mouse.current.position.ReadValue();

        if (mousePos.x <= edgeSize)
            dir.x -= 1f;
        if (mousePos.x >= Screen.width - edgeSize)
            dir.x += 1f;
        if (mousePos.y <= edgeSize)
            dir.z -= 1f;
        if (mousePos.y >= Screen.height - edgeSize)
            dir.z += 1f;

        return dir;
    }

    void MoveCamera(Vector3 dir)
    {
        if (dir == Vector3.zero) return;
        transform.position += dir.normalized * moveSpeed * Time.deltaTime;
    }

    void HandleZoom()
    {
        float zoom = input.CameraZoomIntent;
        if (Mathf.Abs(zoom) < 0.01f) return;

        Vector3 pos = transform.position;
        pos.y -= zoom * zoomSpeed;
        pos.y = Mathf.Clamp(pos.y, minHeight, maxHeight);
        transform.position = pos;
    }
}
