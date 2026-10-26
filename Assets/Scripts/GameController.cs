using UnityEngine;

[RequireComponent(typeof(Camera))]
public class GameController : MonoBehaviour
{
    [Header("场景引用")]
    public CharacterController player;
    public Transform model;

    [Header("第三人称")]
    public float moveSpeed = 3.5f;
    public float fastMoveSpeed = 9f;
    public float turnSpeed = 12f;
    public float mouseSensitivity = 2f;
    public float cameraDistance = 6f;
    public float focusHeight = 1.5f;

    [Header("跳跃")]
    [Min(0.1f)] public float jumpHeight = 3.5f;
    private const float Gravity = -9.81f;

    [Header("第三人称缩放")]
    public float thirdPersonZoomSpeed = 8f;
    public float minCameraDistance = 2.5f;
    public float maxCameraDistance = 15f;

    [Header("鸟瞰")]
    public float birdHeight = 30f;
    public float birdViewSize = 20f;
    public float birdPanSpeed = 15f;
    public float fastBirdPanSpeed = 28f;

    [Header("鸟瞰缩放")]
    public float birdZoomSpeed = 40f;
    public float minBirdViewSize = 8f;
    public float maxBirdViewSize = 30f;

    private Camera sceneCamera;
    private bool birdMode;
    private bool fastMode;
    private float yaw;
    private float pitch = 25f;
    private float verticalSpeed;
    private Vector3 birdCenter;
    private Quaternion modelRotationOffset;

    public bool IsBirdMode => birdMode;

    private void Start()
    {
        sceneCamera = GetComponent<Camera>();
        player.detectCollisions = true;
        modelRotationOffset = model.localRotation;
        birdCenter = player.transform.position;
        cameraDistance = Mathf.Clamp(cameraDistance, minCameraDistance, maxCameraDistance);
        birdViewSize = Mathf.Clamp(birdViewSize, minBirdViewSize, maxBirdViewSize);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (FactoryTutorial.BlocksGameplay || FactoryLevelSession.BlocksInput || RawMaterialSupply.BlocksGameplay || FactoryStatistics.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!birdMode && Input.GetMouseButtonDown(0) &&
            !FactoryLocalAssistant.PointerOverButton(Input.mousePosition) && !FactoryPlanLibrary.PointerOverButton(Input.mousePosition) && !FactoryStatistics.PointerOverStatisticsButton(Input.mousePosition) && !FactoryPlaybackControl.PointerOverButton(Input.mousePosition) && !FactoryTutorial.PointerOverHelp(Input.mousePosition) &&
            !FactoryLevelSession.PointerOverHUD(Input.mousePosition))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Input.GetKeyDown(KeyCode.CapsLock))
        {
            birdMode = !birdMode;

            if (birdMode)
            {
                birdCenter = player.transform.position;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            fastMode = !fastMode;

        if (birdMode || Cursor.lockState == CursorLockMode.Locked)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                if (birdMode)
                    birdViewSize = Mathf.Clamp(
                        birdViewSize - scroll * birdZoomSpeed,
                        minBirdViewSize, maxBirdViewSize);
                else
                    cameraDistance = Mathf.Clamp(
                        cameraDistance - scroll * thirdPersonZoomSpeed,
                        minCameraDistance, maxCameraDistance);
            }
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(horizontal, 0f, vertical).normalized;

        if (birdMode)
        {
            // 只平移鸟瞰画面，不移动玩家。
            float panSpeed = fastMode ? fastBirdPanSpeed : birdPanSpeed;
            birdCenter += input * panSpeed * Time.unscaledDeltaTime;
            // 空中切换视角时继续下落，避免奶蛋悬停。
            MovePlayer(Vector3.zero, false);
            return;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, 10f, 70f);
        }

        // WASD 始终相对于当前视角移动。
        Vector3 moveDirection = Quaternion.Euler(0f, yaw, 0f) * input;

        float currentSpeed = fastMode ? fastMoveSpeed : moveSpeed;
        MovePlayer(moveDirection * currentSpeed,
            Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.Space));

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(moveDirection) * modelRotationOffset;

            model.rotation = Quaternion.Slerp(
                model.rotation,
                targetRotation,
                turnSpeed * Time.unscaledDeltaTime
            );
        }
    }

    private void MovePlayer(Vector3 horizontalVelocity, bool jumpRequested)
    {
        // Synchronize the original animations before the character collision sweep.
        Physics.SyncTransforms();
        if (player.isGrounded)
        {
            if (verticalSpeed < 0f)
                verticalSpeed = -2f;

            // 只允许落地后起跳，长按空格不会自动连跳。
            if (jumpRequested)
                verticalSpeed = Mathf.Sqrt(-2f * Gravity * jumpHeight);
        }

        verticalSpeed += Gravity * Time.unscaledDeltaTime;
        horizontalVelocity.y = verticalSpeed;
        CollisionFlags collision = player.Move(horizontalVelocity * Time.unscaledDeltaTime);
        if ((collision & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            verticalSpeed = 0f;
    }

    private void LateUpdate()
    {
        if (birdMode)
        {
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = birdViewSize;

            transform.SetPositionAndRotation(
                birdCenter + Vector3.up * birdHeight,
                Quaternion.Euler(90f, 0f, 0f)
            );
        }
        else
        {
            sceneCamera.orthographic = false;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focus = player.transform.position + Vector3.up * focusHeight;

            transform.SetPositionAndRotation(
                focus - rotation * Vector3.forward * cameraDistance,
                rotation
            );
        }
    }
}
