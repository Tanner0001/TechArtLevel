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

    [Header("Debug")]
    [Tooltip("When enabled the controller will periodically print useful state information to the console.")]
    [SerializeField] private bool enableDebugLogging = false;
    [Tooltip("Minimum number of rendered frames between automatic debug logs.")]
    [SerializeField, Min(1)] private int debugLogFrameInterval = 10;
    [Tooltip("Draw gizmos that visualise the ground check and desired movement direction.")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private Color groundedGizmoColor = new Color(0.2f, 0.8f, 0.2f, 0.35f);
    [SerializeField] private Color airborneGizmoColor = new Color(0.9f, 0.2f, 0.2f, 0.35f);
    [SerializeField, Min(0f)] private float desiredDirectionGizmoLength = 2f;

    private Rigidbody body;
    private PlayerInput playerInput;

    private InputAction moveInputAction;
    private InputAction lookInputAction;
    private InputAction sprintInputAction;
    private InputAction jumpInputAction;

    private Vector2 moveInput;
    private bool isSprinting;
    private bool jumpRequested;
    private bool lastGroundedState;
    private bool lastJumpAttempted;
    private bool lastJumpPerformed;
    private Vector3 lastDesiredDirection = Vector3.zero;
    private Vector3 lastDesiredVelocity = Vector3.zero;
    private Vector3 lastHorizontalVelocity = Vector3.zero;
    private Vector3 lastBodyVelocity = Vector3.zero;
    private Vector3 lastGroundCheckPosition = Vector3.zero;
    private float lastGroundCheckRadius;
    private int lastDebugFrame = -1;
    private int lastMovementWarningFrame = -1;

    private const string DebugPrefix = "[ActionRPGCharacterController]";

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

        lastGroundCheckRadius = groundCheckRadius;
        lastGroundCheckPosition = groundCheck != null ? groundCheck.position : transform.position;
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

            if (enableDebugLogging)
            {
                Debug.Log($"{DebugPrefix} Jump input triggered on frame {Time.frameCount}.", this);
            }
        }
    }

    private void FixedUpdate()
    {
        bool previousGrounded = lastGroundedState;

        bool grounded = CheckGrounded();
        UpdateDrag(grounded);

        if (enableDebugLogging && previousGrounded != grounded)
        {
            Debug.Log($"{DebugPrefix} Grounded state changed from {previousGrounded} to {grounded} at frame {Time.frameCount}.", this);
        }

        ApplyHorizontalMovement(
            grounded,
            out Vector3 desiredDirection,
            out Vector3 desiredVelocity,
            out Vector3 resultingHorizontalVelocity);

        if (enableDebugLogging
            && grounded
            && desiredDirection.sqrMagnitude > 0.0001f
            && resultingHorizontalVelocity.sqrMagnitude < 0.0001f
            && (lastMovementWarningFrame < 0 || Time.frameCount - lastMovementWarningFrame >= debugLogFrameInterval))
        {
            lastMovementWarningFrame = Time.frameCount;
            Debug.LogWarning($"{DebugPrefix} Movement input detected but horizontal velocity is nearly zero. Desired velocity: {desiredVelocity}, resulting horizontal: {resultingHorizontalVelocity}.", this);
        }

        bool jumpedThisFrame = HandleJump(grounded);

        lastGroundedState = grounded;
        lastDesiredDirection = desiredDirection;
        lastDesiredVelocity = desiredVelocity;
        lastHorizontalVelocity = resultingHorizontalVelocity;
        lastBodyVelocity = body.linearVelocity;
        lastJumpPerformed = jumpedThisFrame;

        LogControllerState();
    }

    private void ApplyHorizontalMovement(
        bool grounded,
        out Vector3 desiredDirection,
        out Vector3 desiredVelocity,
        out Vector3 resultingHorizontalVelocity)
    {
        if (orientationSource == null)
        {
            orientationSource = Camera.main != null ? Camera.main.transform : transform;
        }

        Vector3 forward = GetPlanarDirection(orientationSource.forward);
        Vector3 right = GetPlanarDirection(orientationSource.right);

        desiredDirection = forward * moveInput.y + right * moveInput.x;
        if (desiredDirection.sqrMagnitude > 1f)
        {
            desiredDirection.Normalize();
        }

        float targetSpeed = walkSpeed * (isSprinting ? sprintMultiplier : 1f);
        desiredVelocity = desiredDirection * targetSpeed;

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

        resultingHorizontalVelocity = newHorizontal;

        if (drawDebugGizmos && desiredDirection.sqrMagnitude > 0.0001f)
        {
            Debug.DrawRay(transform.position, desiredDirection.normalized * desiredDirectionGizmoLength, Color.cyan, Time.fixedDeltaTime, false);
        }

        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }

    private bool HandleJump(bool grounded)
    {
        if (!jumpRequested)
        {
            lastJumpAttempted = false;
            return false;
        }

        jumpRequested = false;
        lastJumpAttempted = true;

        if (!grounded)
        {
            if (enableDebugLogging)
            {
                Debug.LogWarning($"{DebugPrefix} Jump was requested while not grounded. Move input: {moveInput}.", this);
            }

            return false;
        }

        Vector3 velocity = body.linearVelocity;
        velocity.y = 0f;
        body.linearVelocity = velocity;
        body.AddForce(Vector3.up * jumpImpulse, ForceMode.VelocityChange);

        if (enableDebugLogging)
        {
            Debug.Log($"{DebugPrefix} Jump executed. Horizontal velocity before jump: {lastHorizontalVelocity}.", this);
        }

        return true;
    }

    private void UpdateDrag(bool grounded)
    {
        body.drag = grounded ? groundedDrag : airborneDrag;
    }

    private bool CheckGrounded()
    {
        Vector3 checkPosition = groundCheck != null ? groundCheck.position : transform.position;
        bool grounded = Physics.CheckSphere(checkPosition, groundCheckRadius, groundLayers, QueryTriggerInteraction.Ignore);

        lastGroundCheckPosition = checkPosition;
        lastGroundCheckRadius = groundCheckRadius;

        if (drawDebugGizmos)
        {
            Color gizmoColor = grounded ? Color.green : Color.red;
            Debug.DrawLine(checkPosition, checkPosition + Vector3.down * 0.15f, gizmoColor, Time.fixedDeltaTime, false);
        }

        return grounded;
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

    private void LogControllerState()
    {
        if (!enableDebugLogging)
        {
            return;
        }

        if (lastDebugFrame >= 0 && Time.frameCount - lastDebugFrame < debugLogFrameInterval)
        {
            return;
        }

        lastDebugFrame = Time.frameCount;

        string desiredInfo = lastDesiredDirection.sqrMagnitude > 0.0001f
            ? $"{lastDesiredDirection.normalized} (speed {lastDesiredVelocity.magnitude:F2})"
            : "None";

        Debug.Log(
            $"{DebugPrefix} Frame {Time.frameCount} | Grounded: {lastGroundedState} | Move Input: {moveInput} | Sprinting: {isSprinting} | " +
            $"Desired Direction: {desiredInfo} | Horizontal Velocity: {lastHorizontalVelocity} | Body Velocity: {lastBodyVelocity} | Drag: {body.drag} | " +
            $"Jump Attempted: {lastJumpAttempted} | Jump Performed: {lastJumpPerformed} | Ground Check Pos: {lastGroundCheckPosition} (r={lastGroundCheckRadius})",
            this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Vector3 checkPosition = Application.isPlaying ? lastGroundCheckPosition : (groundCheck != null ? groundCheck.position : transform.position);
        float radius = Application.isPlaying ? lastGroundCheckRadius : (groundCheckRadius > 0f ? groundCheckRadius : 0.01f);

        Color gizmoColor = Application.isPlaying && lastGroundedState ? groundedGizmoColor : airborneGizmoColor;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(checkPosition, radius);

        if (Application.isPlaying)
        {
            Gizmos.DrawSphere(checkPosition, radius);

            if (lastDesiredDirection.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, transform.position + lastDesiredDirection.normalized * desiredDirectionGizmoLength);
            }
        }
    }
#endif
}
