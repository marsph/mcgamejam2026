using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    public static PlayerMovement Instance { get; private set; }
    
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    
    [Header("Dash")]
    [SerializeField] private float dashSpeed = 5f;
    [SerializeField] private float dashDuration = 3f;
    [SerializeField] private float dashCooldown = 10f;
    [SerializeField] private float dashAttractDuration = 5f;
    [SerializeField] private KeyCode dashKey = KeyCode.Mouse0;

    [Header("Dash UI (optional)")]
    [Tooltip("Running man icon: shown when dash is charged, hidden when on cooldown.")]
    [SerializeField] private GameObject dashChargedIcon;
    [Tooltip("Left click hint icon: shown when dash is charged to remind player to press left click.")]
    [SerializeField] private GameObject leftClickHintIcon;

    [Header("Torch Rotation")]
    [SerializeField] private float mouseDeadzoneRadius = 0.5f;
    [SerializeField, Range(0f, 90f)] private float torchClampAngle = 45f;
    
    [Header("References")]
    [SerializeField] private Transform torch;
    [SerializeField] private Camera mainCamera;

    private Rigidbody2D rb;
    private Vector2 movementInput;
    private Vector2 mouseWorldPosition;
    private Vector2 mouseDirection = Vector2.down; // Direction from player to mouse
    
    // Dash state
    private bool isDashing;
    private float dashTimeRemaining;
    private float dashCooldownRemaining;
    private Vector3 dashStartPosition;

    // Animation properties - use these in your Animator or PlayerAnimator script
    public bool IsMoving => movementInput.sqrMagnitude > 0.01f;
    public float MoveX => movementInput.x;
    public float MoveY => movementInput.y;
    public float MouseX => mouseDirection.x;
    public float MouseY => mouseDirection.y;
    
    // Dash properties for UI
    public bool IsDashing => isDashing;
    public bool CanDash => dashCooldownRemaining <= 0f && !isDashing;
    public float DashCooldownRemaining => dashCooldownRemaining;
    public float DashCooldownTotal => dashCooldown;
    
    // Death state
    public bool IsDead { get; private set; }

    /// <summary>True while an enemy is attacking the player (player is frozen).</summary>
    public bool IsBeingAttacked { get; private set; }

    void Awake()
    {
        Instance = this;
        IsDead = false; // Reset death state on scene load
        
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        
        // Auto-assign main camera if not set in inspector
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    void OnDestroy()
    {
        // Clear instance when destroyed
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        if (IsDead) return;
        if (IsBeingAttacked)
        {
            movementInput = Vector2.zero;
            return;
        }
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
        {
            movementInput = Vector2.zero;
            return;
        }

        // Update dash cooldown
        if (dashCooldownRemaining > 0f)
        {
            dashCooldownRemaining -= Time.deltaTime;
        }

        // Update dash duration
        if (isDashing)
        {
            dashTimeRemaining -= Time.deltaTime;
            if (dashTimeRemaining <= 0f)
            {
                EndDash();
            }
        }

        // Check for dash input (don't dash when clicking on UI, e.g. note close button)
        if (Input.GetKeyDown(dashKey) && CanDash && !IsPointerOverUI())
        {
            StartDash();
        }

        UpdateDashUI();

        // Get WASD input
        movementInput.x = Input.GetAxisRaw("Horizontal");
        movementInput.y = Input.GetAxisRaw("Vertical");
        movementInput = movementInput.normalized;

        // Rotate torch towards mouse cursor and update mouse direction
        RotateTorchTowardsMouse();
    }

    void FixedUpdate()
    {
        if (IsDead)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }
        if (IsBeingAttacked)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Apply movement - use dash speed if dashing
        float currentSpeed = isDashing ? dashSpeed : moveSpeed;
        rb.linearVelocity = movementInput * currentSpeed;
    }

    /// <summary>Returns true if the mouse is over any UI that blocks raycasts (e.g. note panel, close button). Prevents dash when clicking UI.</summary>
    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        var eventData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        foreach (var r in results)
        {
            if (r.gameObject != null && r.module is GraphicRaycaster)
                return true;
        }
        return false;
    }

    private void UpdateDashUI()
    {
        if (IsDead || (GameManager.Instance != null && GameManager.Instance.IsPaused))
        {
            if (dashChargedIcon != null) dashChargedIcon.SetActive(false);
            if (leftClickHintIcon != null) leftClickHintIcon.SetActive(false);
            return;
        }
        bool charged = CanDash;
        if (dashChargedIcon != null)
            dashChargedIcon.SetActive(charged);
        if (leftClickHintIcon != null)
            leftClickHintIcon.SetActive(charged);
    }

    private void StartDash()
    {
        isDashing = true;
        dashTimeRemaining = dashDuration;
        dashStartPosition = transform.position;
        
        Debug.Log("[Player] Dash started!");
        
        // Attract monsters to dash start position
        if (TrapSoundManager.Instance != null)
        {
            TrapSoundManager.Instance.ActivateSound(dashStartPosition, dashAttractDuration);
            Debug.Log("[Player] Monsters attracted to dash position for " + dashAttractDuration + " seconds.");
        }
    }

    private void EndDash()
    {
        isDashing = false;
        dashCooldownRemaining = dashCooldown;
        
        Debug.Log("[Player] Dash ended. Cooldown: " + dashCooldown + " seconds.");
    }

    /// <summary>Called by Enemy when attack starts. Freezes player until death.</summary>
    public void SetBeingAttacked(bool value)
    {
        IsBeingAttacked = value;
        if (value)
        {
            movementInput = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }
    
    public void Die()
    {
        if (IsDead) return;

        IsDead = true;
        IsBeingAttacked = false;
        movementInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
        
        Debug.Log("Player died!");
        
        // Notify GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlayerDeath();
        }
    }

    private void RotateTorchTowardsMouse()
    {
        // Get mouse position in world space
        mouseWorldPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        
        // Calculate direction from player to mouse
        Vector2 direction = mouseWorldPosition - (Vector2)transform.position;
        
        // Only update if mouse is outside the deadzone
        if (direction.sqrMagnitude > mouseDeadzoneRadius * mouseDeadzoneRadius)
        {
            // Store normalized direction for animations
            mouseDirection = direction.normalized;
            
            // Calculate the angle in degrees (atan2 gives radians, convert to degrees)
            // Subtract 90 degrees because Unity's "up" is 0 degrees, but atan2 treats "right" as 0
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            
            // Clamp torch rotation based on movement direction
            if (IsMoving)
            {
                angle = ClampTorchAngle(angle);
            }
            
            // Apply rotation to torch only (not the player)
            if (torch != null)
            {
                torch.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
    }

    private float ClampTorchAngle(float angle)
    {
        // Calculate base angle from movement direction
        // Up = 0°, Right = -90°, Down = 180°, Left = 90°
        float baseAngle = Mathf.Atan2(movementInput.y, movementInput.x) * Mathf.Rad2Deg - 90f;
        
        // Calculate the difference between mouse angle and movement angle
        float angleDiff = Mathf.DeltaAngle(baseAngle, angle);
        
        // Clamp the difference within the allowed range
        angleDiff = Mathf.Clamp(angleDiff, -torchClampAngle, torchClampAngle);
        
        // Return the clamped angle
        return baseAngle + angleDiff;
    }
}