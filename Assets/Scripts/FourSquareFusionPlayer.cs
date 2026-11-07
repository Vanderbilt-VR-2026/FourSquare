using Fusion;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Unity.XR.CoreUtils;
#if UNITY_EDITOR
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#endif

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(NetworkObject))]
public class FourSquareFusionPlayer : NetworkBehaviour
{
    [Header("XR Rig")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;

    [Header("Remote Rig")]
    [SerializeField] private Renderer[] avatarRenderers;
    [SerializeField] private bool showLocalAvatar;

    [Header("Mixamo Avatar & IK Setup")]
    [SerializeField] private Transform stickmanTransform;
    [SerializeField] private Transform ikLeftHandTarget;
    [SerializeField] private Transform ikRightHandTarget;
    [SerializeField] private Animator mixamoAnimator;
    [SerializeField] private Renderer[] headMeshRenderers;
    [SerializeField] private bool logIkDiagnostics;
    [SerializeField] private float ikDiagnosticInterval = 0.25f;

    [Networked] public Vector3 NetworkRootPosition { get; set; }
    [Networked] public Quaternion NetworkRootRotation { get; set; }
    [Networked] public Vector3 NetworkHeadPosition { get; set; }
    [Networked] public Quaternion NetworkHeadRotation { get; set; }
    [Networked] public Vector3 NetworkLeftHandPosition { get; set; }
    [Networked] public Quaternion NetworkLeftHandRotation { get; set; }
    [Networked] public Vector3 NetworkRightHandPosition { get; set; }
    [Networked] public Quaternion NetworkRightHandRotation { get; set; }
    [Networked] public bool NetworkHasXRTracking { get; set; }
    [Networked] public int NetworkRole { get; set; }

    public bool IsLocallyOwned => Object != null && Object.IsValid && Object.HasInputAuthority;
    public bool HasPlayerInputAuthority =>
        Object != null && Object.IsValid && Object.InputAuthority != PlayerRef.None;
    public bool IsLocalCameraReady =>
        Object != null
        && Object.IsValid
        && Object.HasInputAuthority
        && rigCamera != null
        && rigCamera.isActiveAndEnabled;
    public Transform HeadTransform => head;
    public int JoinOrder => Object != null && Object.IsValid ? Object.InputAuthority.PlayerId : int.MaxValue;
    public int ConnectedPlayerCount
    {
        get
        {
            if (Object == null || !Object.IsValid || Runner == null) return 0;
            int count = 0;
            foreach (PlayerRef _ in Runner.ActivePlayers) count++;
            return count;
        }
    }

    public SquareRole PlayerRole => System.Enum.IsDefined(typeof(SquareRole), NetworkRole)
        ? (SquareRole)NetworkRole
        : SquareRole.Spectator;
    public string PlayerId => Object != null && Object.IsValid ? Object.InputAuthority.ToString() : string.Empty;
    public string DisplayName => Object != null && Object.IsValid
        ? $"Player {Object.InputAuthority.PlayerId}"
        : "Unknown Player";

    private BallHandMarker[] handMarkers;
    private Camera rigCamera;
    private Renderer squareHeadRenderer;
    private Material squareHeadMaterial;
    private int lastAppliedRole = int.MinValue;
    private Vector3 lastRootPosition;
    private bool hasSpeedParameter;
    private RigBuilder rigBuilder;
    private float nextIkDiagnosticTime;

    private void Awake()
    {
        if (xrOrigin == null) xrOrigin = GetComponent<XROrigin>();
        if (head == null && xrOrigin != null && xrOrigin.Camera != null) head = xrOrigin.Camera.transform;
        if (leftHand == null) leftHand = FindChild(transform, "Left Hand");
        if (rightHand == null) rightHand = FindChild(transform, "Right Hand");

        // Auto-find Stickman components if not set in Inspector
        if (stickmanTransform == null)
        {
            Transform foundStickman = FindChild(transform, "Stickman");
            if (foundStickman != null) stickmanTransform = foundStickman;
        }

        if (stickmanTransform != null)
        {
            if (ikLeftHandTarget == null) ikLeftHandTarget = FindChild(stickmanTransform, "LeftHandTarget");
            if (ikRightHandTarget == null) ikRightHandTarget = FindChild(stickmanTransform, "RightHandTarget");
            if (mixamoAnimator == null) mixamoAnimator = stickmanTransform.GetComponent<Animator>();
        }

        if (leftHand == null) Debug.LogError("FourSquareFusionPlayer could not find the XR Left Hand transform.", this);
        if (rightHand == null) Debug.LogError("FourSquareFusionPlayer could not find the XR Right Hand transform.", this);
        if (ikLeftHandTarget == null) Debug.LogError("FourSquareFusionPlayer could not find Stickman/IK_Rig/LeftHandTarget.", this);
        if (ikRightHandTarget == null) Debug.LogError("FourSquareFusionPlayer could not find Stickman/IK_Rig/RightHandTarget.", this);

        if (stickmanTransform != null)
        {
            rigBuilder = stickmanTransform.GetComponent<RigBuilder>()
                ?? stickmanTransform.GetComponentInChildren<RigBuilder>(true);
            if (rigBuilder == null)
            {
                Debug.LogError("FourSquareFusionPlayer could not find a RigBuilder on Stickman.", this);
            }
            else
            {
                foreach (Rig rig in rigBuilder.GetComponentsInChildren<Rig>(true))
                {
                    rig.weight = 1f;
                }

                foreach (TwoBoneIKConstraint constraint in rigBuilder.GetComponentsInChildren<TwoBoneIKConstraint>(true))
                {
                    constraint.weight = 1f;
                    constraint.data.targetPositionWeight = 1f;
                    constraint.data.targetRotationWeight = 1f;
                }

                EnsureRigBuilt();
            }
        }

        CreateSquareHead();
        if (avatarRenderers == null || avatarRenderers.Length == 0 || System.Array.TrueForAll(avatarRenderers, renderer => renderer == null))
        {
            avatarRenderers = GetComponentsInChildren<Renderer>(true);
        }

        rigCamera = head != null ? head.GetComponent<Camera>() : GetComponentInChildren<Camera>(true);
        handMarkers = GetComponentsInChildren<BallHandMarker>(true);
        hasSpeedParameter = mixamoAnimator != null
            && System.Array.Exists(
                mixamoAnimator.parameters,
                parameter => parameter.name == "Speed"
                    && parameter.type == AnimatorControllerParameterType.Float);
    }

    private void CreateSquareHead()
    {
        if (head == null)
        {
            Debug.LogWarning("Cannot create the placeholder square head because the XR head transform is not assigned.", this);
            return;
        }

        GameObject headObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        headObject.name = "Placeholder Square Head";
        headObject.transform.SetParent(head, false);
        headObject.transform.localPosition = Vector3.zero;
        headObject.transform.localRotation = Quaternion.identity;
        headObject.transform.localScale = Vector3.one * 0.22f;

        Collider headCollider = headObject.GetComponent<Collider>();
        if (headCollider != null) Destroy(headCollider);

        squareHeadRenderer = headObject.GetComponent<Renderer>();
        squareHeadRenderer.enabled = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("Could not find a shader for the placeholder square head.", this);
            return;
        }

        squareHeadMaterial = new Material(shader)
        {
            color = new Color(0.95f, 0.75f, 0.35f, 1f)
        };
        squareHeadRenderer.sharedMaterial = squareHeadMaterial;
    }

    public override void Spawned()
    {
        EnsureRigBuilt();

        if (!HasPlayerInputAuthority)
        {
            Debug.LogWarning(
                "FourSquareFusionPlayer has no input authority. It will remain a non-controllable network proxy. " +
                "Ensure this object is spawned from the VR Player prefab and is not placed directly in a scene.",
                this);
            ConfigureRigForAuthority();
            return;
        }

        if (Object.HasStateAuthority)
        {
            NetworkRole = (int)SquareRole.Spectator;
            CaptureLocalPose();
        }

        ConfigureRigForAuthority();
        GameStateManager.Instance?.RegisterNetworkPlayer(this);
        lastRootPosition = transform.position;
        if (stickmanTransform != null)
        {
            var rigBuilder = stickmanTransform.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            if (rigBuilder != null)
            {
                // Re-evaluate and rebuild IK hierarchy after network instantiation
                rigBuilder.Build();
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return;

        CaptureLocalPose();
    }

    private void CaptureLocalPose()
    {
        NetworkHasXRTracking = HasXRTracking();
        NetworkRootPosition = transform.position;
        NetworkRootRotation = transform.rotation;
        if (head != null)
        {
            NetworkHeadPosition = head.position;
            NetworkHeadRotation = head.rotation;
        }
        if (leftHand != null)
        {
            NetworkLeftHandPosition = leftHand.position;
            NetworkLeftHandRotation = leftHand.rotation;
        }
        if (rightHand != null)
        {
            NetworkRightHandPosition = rightHand.position;
            NetworkRightHandRotation = rightHand.rotation;
        }
    }

    public override void Render()
    {
        if (Object == null || !Object.IsValid)
        {
            return;
        }

        UpdateHandRoles();

        bool isLocal = Object.HasInputAuthority;
        if (squareHeadRenderer != null)
        {
            squareHeadRenderer.enabled = !isLocal;
        }

        if (isLocal)
        {
            if (head != null)
            {
                UpdateBodyPositionAndRotation(head.position, head.rotation);
            }

            ApplyLocalHandTargets();

            UpdateLegLocomotion();
            return;
        }

        foreach (BallHandMarker handMarker in handMarkers)
        {
            if (handMarker != null) handMarker.SetTrackingActive(true);
        }

        transform.SetPositionAndRotation(NetworkRootPosition, NetworkRootRotation);
        ReadPose(head, NetworkHeadPosition, NetworkHeadRotation);
        ReadPose(leftHand, NetworkLeftHandPosition, NetworkLeftHandRotation);
        ReadPose(rightHand, NetworkRightHandPosition, NetworkRightHandRotation);
        UpdateBodyPositionAndRotation(NetworkHeadPosition, NetworkHeadRotation);

        ApplyRemoteHandTargets();

        UpdateLegLocomotion();
    }

    private void LateUpdate()
    {
        if (Object == null || !Object.IsValid) return;

        LogIkDiagnostics();
    }

    private void ApplyLocalHandTargets()
    {
        if (leftHand != null && ikLeftHandTarget != null)
            ikLeftHandTarget.SetPositionAndRotation(leftHand.position, leftHand.rotation);

        if (rightHand != null && ikRightHandTarget != null)
            ikRightHandTarget.SetPositionAndRotation(rightHand.position, rightHand.rotation);
    }

    private void ApplyRemoteHandTargets()
    {
        if (ikLeftHandTarget != null)
            ikLeftHandTarget.SetPositionAndRotation(NetworkLeftHandPosition, NetworkLeftHandRotation);

        if (ikRightHandTarget != null)
            ikRightHandTarget.SetPositionAndRotation(NetworkRightHandPosition, NetworkRightHandRotation);
    }

    private void EnsureRigBuilt()
    {
        if (rigBuilder == null) return;

        if (mixamoAnimator != null && !mixamoAnimator.enabled)
        {
            mixamoAnimator.enabled = true;
        }

        if (!rigBuilder.enabled)
        {
            rigBuilder.enabled = true;
        }

        if (!rigBuilder.graph.IsValid() && !rigBuilder.Build())
        {
            Debug.LogError("FourSquareFusionPlayer could not build the Animation Rigging graph.", this);
        }
    }

    private void LogIkDiagnostics()
    {
        if (!logIkDiagnostics || Time.unscaledTime < nextIkDiagnosticTime) return;

        nextIkDiagnosticTime = Time.unscaledTime + Mathf.Max(0.01f, ikDiagnosticInterval);

        if (leftHand == null || ikLeftHandTarget == null || rightHand == null || ikRightHandTarget == null)
        {
            Debug.LogWarning("IK diagnostic skipped because a hand or IK target is not assigned.", this);
            return;
        }

        Debug.Log(
            $"IK hands | left source: {leftHand.position}, left target: {ikLeftHandTarget.position}, " +
            $"right source: {rightHand.position}, right target: {ikRightHandTarget.position}",
            this);
    }

    private void UpdateBodyPositionAndRotation(Vector3 headPos, Quaternion headRot)
    {
        if (stickmanTransform == null) return;

        // Position Stickman body base directly under the headset position at ground level
        Vector3 bodyPosition = headPos;
        bodyPosition.y = transform.position.y;
        stickmanTransform.position = bodyPosition;

        // Align body rotation with the head's horizontal yaw to prevent neck twisting
        Vector3 forward = headRot * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
        {
            stickmanTransform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }

    private void UpdateLegLocomotion()
    {
        if (mixamoAnimator == null || !hasSpeedParameter) return;

        Vector3 displacement = transform.position - lastRootPosition;
        displacement.y = 0f; // Ignore vertical height adjustments
        float speed = Time.deltaTime > 0f ? displacement.magnitude / Time.deltaTime : 0f;
        lastRootPosition = transform.position;

        mixamoAnimator.SetFloat("Speed", speed);
    }

    public void RequestRole(SquareRole role)
    {
        if (!System.Enum.IsDefined(typeof(SquareRole), role) || role == SquareRole.Spectator) return;

        if (Object == null || !Object.IsValid)
        {
            NetworkRole = (int)role;
            return;
        }

        if (Object.HasStateAuthority)
        {
            NetworkRole = (int)role;
            ApplyRoleToLocalRig(role);
        }
        else
        {
            RPC_SetRole((int)role);
        }
    }

    public void RequestGameStart()
    {
        if (Object == null || !Object.IsValid)
        {
            GameStateManager.Instance?.StartGame();
            return;
        }

        RPC_RequestGameStart();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RequestGameStart()
    {
        if (Runner != null && Runner.IsSharedModeMasterClient)
        {
            GameStateManager.Instance?.StartGameAsAuthority(Runner);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetRole(int role)
    {
        if (System.Enum.IsDefined(typeof(SquareRole), role) && role != (int)SquareRole.Spectator)
        {
            NetworkRole = role;
            ApplyRoleToLocalRig((SquareRole)role);
        }
    }

    private void ApplyRoleToLocalRig(SquareRole role)
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority && xrOrigin != null)
        {
            PlayerJoinCourt.MoveJoinedPlayerToRole(xrOrigin, role);
        }
    }

    private void ConfigureRigForAuthority()
    {
        if (Object == null || !Object.IsValid) return;

        bool isLocal = Object.HasInputAuthority;
        if (rigCamera != null) rigCamera.enabled = isLocal;

        foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = isLocal;
        }

        foreach (UnityEngine.Behaviour behaviour in GetComponentsInChildren<UnityEngine.Behaviour>(true))
        {
            if (behaviour == this || behaviour is XROrigin || behaviour is Camera || behaviour is AudioListener)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName == "TrackedPoseDriver"
                || typeName == "ContinuousMoveProvider" || typeName == "SnapTurnProvider"
                || typeName == "ContinuousTurnProvider" || typeName == "LocomotionMediator")
            {
                behaviour.enabled = isLocal;
            }
        }

        bool showTrackedHands = isLocal ? HasXRTracking() : NetworkHasXRTracking;
        bool renderAvatar = isLocal ? showLocalAvatar : true;
        foreach (Renderer avatarRenderer in avatarRenderers)
        {
            if (avatarRenderer == null) continue;

            bool isHandVisual = avatarRenderer.GetComponentInParent<BallHandMarker>() != null;
            avatarRenderer.enabled = !isHandVisual && renderAvatar;
        }

        foreach (BallHandMarker handMarker in handMarkers)
        {
            if (handMarker != null)
            {
                handMarker.SetVisualVisible(false);
                handMarker.SetTrackingActive(showTrackedHands);
            }
        }

        if (squareHeadRenderer != null)
        {
            squareHeadRenderer.enabled = !isLocal;
        }

        int localHeadLayer = LayerMask.NameToLayer("LocalHead");
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (localHeadLayer == -1)
        {
            Debug.LogError("FourSquareFusionPlayer requires a layer named 'LocalHead'.", this);
            localHeadLayer = defaultLayer;
        }

        if (headMeshRenderers != null)
        {
            foreach (Renderer headRenderer in headMeshRenderers)
            {
                if (headRenderer != null) headRenderer.gameObject.layer = isLocal ? localHeadLayer : defaultLayer;
            }
        }
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid) return;

        if (Object.HasInputAuthority)
        {
            ApplyLocalHandTargets();
        }
        else
        {
            ApplyRemoteHandTargets();
        }

        if (!Object.HasInputAuthority) return;

        bool showTrackedHands = HasXRTracking();
        foreach (BallHandMarker handMarker in handMarkers)
        {
            if (handMarker != null)
            {
                handMarker.SetVisualVisible(false);
                handMarker.SetTrackingActive(showTrackedHands);
            }
        }
    }

    private static bool HasXRTracking()
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive)
            return true;

#if UNITY_EDITOR
        XRInteractionSimulator interactionSimulator =
            FindAnyObjectByType<XRInteractionSimulator>();
        if (interactionSimulator != null && interactionSimulator.isActiveAndEnabled)
            return true;

        XRDeviceSimulator simulator =
            FindAnyObjectByType<XRDeviceSimulator>();
        return simulator != null && simulator.isActiveAndEnabled;
#else
        return false;
#endif
    }

    private void UpdateHandRoles()
    {
        if (lastAppliedRole == NetworkRole) return;
        lastAppliedRole = NetworkRole;

        SquareRole role = PlayerRole;
        foreach (BallHandMarker marker in handMarkers)
        {
            if (marker != null) marker.SetPlayerRole(role);
        }

        GameStateManager.Instance?.UpdateNetworkPlayerRole(this);
    }

    private static void ReadPose(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target != null) target.SetPositionAndRotation(position, rotation);
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName) return child;
        }

        return null;
    }

    private void OnDestroy()
    {
        if (squareHeadMaterial != null)
        {
            Destroy(squareHeadMaterial);
        }
    }
}