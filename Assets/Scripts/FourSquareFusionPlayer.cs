using Fusion;
using UnityEngine;
using Unity.XR.CoreUtils;
#if UNITY_EDITOR
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#endif

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

    private void Awake()
    {
        if (xrOrigin == null) xrOrigin = GetComponent<XROrigin>();
        if (head == null && xrOrigin != null && xrOrigin.Camera != null) head = xrOrigin.Camera.transform;
        if (leftHand == null) leftHand = FindChild(transform, "Left Hand");
        if (rightHand == null) rightHand = FindChild(transform, "Right Hand");
        CreateSquareHead();
        if (avatarRenderers == null || avatarRenderers.Length == 0)
        {
            avatarRenderers = GetComponentsInChildren<Renderer>(true);
        }

        rigCamera = head != null ? head.GetComponent<Camera>() : GetComponentInChildren<Camera>(true);
        handMarkers = GetComponentsInChildren<BallHandMarker>(true);
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
        if (!HasPlayerInputAuthority)
        {
            Debug.LogError(
                "FourSquareFusionPlayer is attached to a scene NetworkObject with no player input authority. " +
                "Remove it from the scene rig; the Fusion player must be spawned from the VR Player prefab.",
                this);
            return;
        }

        if (Object.HasStateAuthority)
        {
            NetworkRole = (int)SquareRole.Spectator;
            CaptureLocalPose();
        }

        ConfigureRigForAuthority();
        GameStateManager.Instance?.RegisterNetworkPlayer(this);
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
        UpdateHandRoles();

        if (Object.HasInputAuthority) return;

        foreach (BallHandMarker handMarker in handMarkers)
        {
            if (handMarker != null) handMarker.SetTrackingActive(NetworkHasXRTracking);
        }

        transform.SetPositionAndRotation(NetworkRootPosition, NetworkRootRotation);
        ReadPose(head, NetworkHeadPosition, NetworkHeadRotation);
        ReadPose(leftHand, NetworkLeftHandPosition, NetworkLeftHandRotation);
        ReadPose(rightHand, NetworkRightHandPosition, NetworkRightHandRotation);
    }

    public void RequestRole(SquareRole role)
    {
        if (!System.Enum.IsDefined(typeof(SquareRole), role) || role == SquareRole.Spectator) return;

        bool hasStateAuthority = Object != null && Object.IsValid && Object.HasStateAuthority;
        Debug.Log(
            $"[RoleTrace] RequestRole playerId='{PlayerId}', current={PlayerRole}, requested={role}, " +
            $"stateAuthority={hasStateAuthority}.",
            this);

        if (Object == null || !Object.IsValid)
        {
            NetworkRole = (int)role;
            Debug.Log($"[RoleTrace] Applied local role playerId='{PlayerId}', role={PlayerRole}.", this);
            return;
        }

        if (hasStateAuthority)
        {
            NetworkRole = (int)role;
            ApplyRoleToLocalRig(role);
            Debug.Log($"[RoleTrace] Applied network role playerId='{PlayerId}', role={PlayerRole}.", this);
        }
        else
        {
            RPC_SetRole((int)role);
            Debug.Log($"[RoleTrace] Sent role request playerId='{PlayerId}', requested={role}.", this);
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
            SquareRole oldRole = PlayerRole;
            NetworkRole = role;
            ApplyRoleToLocalRig((SquareRole)role);
            Debug.Log(
                $"[RoleTrace] RPC applied playerId='{PlayerId}', oldRole={oldRole}, newRole={PlayerRole}.",
                this);
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
            avatarRenderer.enabled = (isHandVisual && showTrackedHands) || renderAvatar;
        }

        foreach (BallHandMarker handMarker in handMarkers)
        {
            if (handMarker != null) handMarker.SetTrackingActive(showTrackedHands);
        }

        if (squareHeadRenderer != null)
        {
            squareHeadRenderer.enabled = !isLocal;
        }
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;

        bool showTrackedHands = HasXRTracking();
        foreach (BallHandMarker handMarker in handMarkers)
        {
            if (handMarker != null) handMarker.SetTrackingActive(showTrackedHands);
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

        ApplyRoleToLocalRig(role);
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
