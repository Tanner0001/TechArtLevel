using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physics driven third person controller that is compatible with Unity's Input System.
/// Uses Rigidbody.linearVelocity to satisfy Unity 6 requirements.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerInput))]
public class ActionRPGCharacterController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4.5f;
    [SerializeField, Min(0f)] private float sprintMultiplier = 1.6f;
    [SerializeField, Min(0f)] private float acceleration = 20f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;
    [SerializeField, Min(0f)] private float rotationSpeed = 12f;
    [SerializeField, Min(0f)] private float groundedDrag = 5f;
    [SerializeField, Min(0f)] private float airborneDrag = 0.25f;

    [Header("Jumping")]
    [SerializeField, Min(0f)] private float jumpImpulse = 5f;
    [SerializeField] private Transform groundCheck;
    [SerializeField, Min(0f)] private float groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Orientation")]
    [Tooltip("Transform that defines the planar forward for movement. Defaults to the main camera if left empty.")]
    [SerializeField] private Transform orientationSource;

    [Header("Input")] 
    [SerializeField] private InputActionProperty moveAction;
    [SerializeField] private InputActionProperty lookAction;
    [SerializeField] private InputActionProperty sprintAction;
    [SerializeField] private InputActionProperty jumpAction;

    private Rigidbody body;
    private PlayerInput playerInput;

    private InputAction moveInputAction;
    private InputAction lookInputAction;
    private InputAction sprintInputAction;
    private InputAction jumpInputAction;

    private Vector2 moveInput;
    private bool isSprinting;
    private bool jumpRequested;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();

        if (groundCheck == null)
        {
            groundCheck = transform;
        }

        if (orientationSource == null && Camera.main != null)
        {
            orientationSource = Camera.main.transform;
        }

        // Prevent the rigidbody from falling over when receiving forces.
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        moveInputAction = ResolveAction(moveAction, "Move");
        lookInputAction = ResolveAction(lookAction, "Look");
        sprintInputAction = ResolveAction(sprintAction, "Sprint");
        jumpInputAction = ResolveAction(jumpAction, "Jump");
    }

    private void OnEnable()
    {
        EnableAction(moveInputAction);
        EnableAction(lookInputAction);
        EnableAction(sprintInputAction);
        EnableAction(jumpInputAction);
    }

    private void OnDisable()
    {
        DisableAction(moveInputAction);
        DisableAction(lookInputAction);
        DisableAction(sprintInputAction);
        DisableAction(jumpInputAction);
    }

    private void Update()
    {
        moveInput = moveInputAction != null ? moveInputAction.ReadValue<Vector2>() : Vector2.zero;
        isSprinting = sprintInputAction != null && sprintInputAction.IsPressed();

        if (jumpInputAction != null && jumpInputAction.triggered)
        {
            jumpRequested = true;
        }
    }

    private void FixedUpdate()
    {
        bool grounded = CheckGrounded();
        UpdateDrag(grounded);
        ApplyHorizontalMovement(grounded);
        HandleJump(grounded);
    }

    private void ApplyHorizontalMovement(bool grounded)
    {
        if (orientationSource == null)
        {
            orientationSource = Camera.main != null ? Camera.main.transform : transform;
        }

        Vector3 forward = GetPlanarDirection(orientationSource.forward);
        Vector3 right = GetPlanarDirection(orientationSource.right);

        Vector3 desiredDirection = forward * moveInput.y + right * moveInput.x;
        if (desiredDirection.sqrMagnitude > 1f)
        {
            desiredDirection.Normalize();
        }

        float targetSpeed = walkSpeed * (isSprinting ? sprintMultiplier : 1f);
        Vector3 desiredVelocity = desiredDirection * targetSpeed;

        Vector3 currentVelocity = body.linearVelocity;
        Vector3 currentHorizontal = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

        float control = grounded ? 1f : airControl;
        Vector3 newHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            desiredVelocity,
            acceleration * control * Time.fixedDeltaTime);

        currentVelocity.x = newHorizontal.x;
        currentVelocity.z = newHorizontal.z;
        body.linearVelocity = currentVelocity;

        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }

    private void HandleJump(bool grounded)
    {
        if (!jumpRequested)
        {
            return;
        }

        jumpRequested = false;

        if (!grounded)
        {
            return;
        }

        Vector3 velocity = body.linearVelocity;
        velocity.y = 0f;
        body.linearVelocity = velocity;
        body.AddForce(Vector3.up * jumpImpulse, ForceMode.VelocityChange);
    }

    private void UpdateDrag(bool grounded)
    {
        body.drag = grounded ? groundedDrag : airborneDrag;
    }

    private bool CheckGrounded()
    {
        return Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private static Vector3 GetPlanarDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0f ? direction.normalized : Vector3.zero;
    }

    private void EnableAction(InputAction action)
    {
        if (action == null)
        {
            return;
        }

        if (!action.enabled)
        {
            action.Enable();
        }
    }

    private void DisableAction(InputAction action)
    {
        if (action == null)
        {
            return;
        }

        if (action.enabled)
        {
            action.Disable();
        }
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
}
