using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Responsive third person follow camera that reads look input from Unity's Input System
/// and keeps a cinematic offset behind the target.
/// </summary>
[DefaultExecutionOrder(100)]
public class ThirdPersonFollowCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);

    [Header("Camera Placement")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 3.5f, -6f);
    [SerializeField, Min(0f)] private float followSmoothing = 12f;

    [Header("Look Settings")]
    [SerializeField, Min(0f)] private float lookSensitivity = 120f;
    [SerializeField] private float minPitch = -40f;
    [SerializeField] private float maxPitch = 75f;
    [SerializeField] private bool lockCursor = true;

    [Header("Input")]
    [SerializeField] private InputActionProperty lookAction;

    private PlayerInput playerInput;
    private InputAction lookInputAction;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        if (followTarget == null)
        {
            followTarget = transform.parent;
        }

        if (followTarget != null)
        {
            playerInput = followTarget.GetComponent<PlayerInput>();
        }

        lookInputAction = ResolveAction(lookAction, "Look");
        AlignAnglesToTarget();
    }

    private void OnEnable()
    {
        EnableAction(lookInputAction);

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        DisableAction(lookInputAction);

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void LateUpdate()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector2 lookDelta = lookInputAction != null ? lookInputAction.ReadValue<Vector2>() : Vector2.zero;
        yaw += lookDelta.x * lookSensitivity * Time.deltaTime;
        pitch -= lookDelta.y * lookSensitivity * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = followTarget.position + targetOffset + orbitRotation * cameraOffset;
        float lerpFactor = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, lerpFactor);

        Vector3 lookTarget = followTarget.position + targetOffset;
        Quaternion lookRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, lerpFactor);
    }

    public void SetFollowTarget(Transform target)
    {
        followTarget = target;
        playerInput = target != null ? target.GetComponent<PlayerInput>() : null;
        lookInputAction = ResolveAction(lookAction, "Look");
        AlignAnglesToTarget();
    }

    private void AlignAnglesToTarget()
    {
        if (followTarget == null)
        {
            return;
        }

        Vector3 forward = followTarget.forward;
        yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        pitch = 0f;
    }

    private InputAction ResolveAction(InputActionProperty property, string actionName)
    {
        if (property.reference != null)
        {
            return property.reference.action;
        }

        if (property.action != null)
        {
            return property.action;
        }

        return playerInput != null ? playerInput.actions?.FindAction(actionName) : null;
    }

    private void EnableAction(InputAction action)
    {
        if (action != null && !action.enabled)
        {
            action.Enable();
        }
    }

    private void DisableAction(InputAction action)
    {
        if (action != null && action.enabled)
        {
            action.Disable();
        }
    }
}
