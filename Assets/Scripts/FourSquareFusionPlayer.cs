using System.Collections.Generic;
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

    [Header("Avatar Hand Rotation Offsets")]
    [Tooltip("Local rotation offset applied to the avatar's left hand target relative to the XR controller rotation.")]
    [SerializeField] private Vector3 leftHandRotationOffset = new Vector3(90f, 0f, 0f);

    [Tooltip("Local rotation offset applied to the avatar's right hand target relative to the XR controller rotation.")]
    [SerializeField] private Vector3 rightHandRotationOffset = new Vector3(90f, 0f, 0f);

    [Header("Floating Hands Settings")]
    [Tooltip("If true, removes arm geometry to create clean floating hands with no wrist distortion.")]
    [SerializeField] private bool useFloatingHands = true;

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
    private Transform leftHandBone;
    private Transform rightHandBone;
    private bool floatingMeshInitialized;

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
            if (mixamoAnimator == null) mixamoAnimator = stickmanTransform.GetComponent<Animator>();
            SetupIKConstraints();
            if (useFloatingHands)
            {
                SetupFloatingHandsMesh();
            }
        }

        if (leftHand == null) Debug.LogError("FourSquareFusionPlayer could not find the XR Left Hand transform.", this);
        if (rightHand == null) Debug.LogError("FourSquareFusionPlayer could not find the XR Right Hand transform.", this);
        if (ikLeftHandTarget == null) Debug.LogError("FourSquareFusionPlayer could not find Stickman/IK_Rig/LeftHandTarget.", this);
        if (ikRightHandTarget == null) Debug.LogError("FourSquareFusionPlayer could not find Stickman/IK_Rig/RightHandTarget.", this);

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

    private void Start()
    {
        bool isFusionRunning = Runner != null && Runner.IsRunning;
        if (!isFusionRunning && (Object == null || !Object.IsValid))
        {
            ConfigureRigForAuthority();
            EnsureRigBuilt();
        }
    }

    private void SetupIKConstraints()
    {
        if (stickmanTransform == null) return;

        rigBuilder = stickmanTransform.GetComponent<RigBuilder>()
            ?? stickmanTransform.GetComponentInChildren<RigBuilder>(true);
        if (rigBuilder == null)
        {
            rigBuilder = stickmanTransform.gameObject.AddComponent<RigBuilder>();
        }

        Transform ikRigTransform = FindChild(stickmanTransform, "IK_Rig");
        if (ikRigTransform == null)
        {
            GameObject ikRigObj = new GameObject("IK_Rig");
            ikRigTransform = ikRigObj.transform;
            ikRigTransform.SetParent(stickmanTransform, false);
        }

        Rig ikRig = ikRigTransform.GetComponent<Rig>();
        if (ikRig == null) ikRig = ikRigTransform.gameObject.AddComponent<Rig>();
        ikRig.weight = 1f;

        if (ikLeftHandTarget == null) ikLeftHandTarget = FindChild(stickmanTransform, "LeftHandTarget");
        if (ikLeftHandTarget == null)
        {
            GameObject leftTargetObj = new GameObject("LeftHandTarget");
            ikLeftHandTarget = leftTargetObj.transform;
            ikLeftHandTarget.SetParent(ikRigTransform, false);
        }
        else if (ikLeftHandTarget.parent != ikRigTransform)
        {
            ikLeftHandTarget.SetParent(ikRigTransform, true);
        }

        if (ikRightHandTarget == null) ikRightHandTarget = FindChild(stickmanTransform, "RightHandTarget");
        if (ikRightHandTarget == null)
        {
            GameObject rightTargetObj = new GameObject("RightHandTarget");
            ikRightHandTarget = rightTargetObj.transform;
            ikRightHandTarget.SetParent(ikRigTransform, false);
        }
        else if (ikRightHandTarget.parent != ikRigTransform)
        {
            ikRightHandTarget.SetParent(ikRigTransform, true);
        }

        // Locate bones on the skeleton
        Transform leftArm = FindChild(stickmanTransform, "mixamorig:LeftArm")
            ?? (mixamoAnimator != null ? mixamoAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null);
        Transform leftForeArm = FindChild(stickmanTransform, "mixamorig:LeftForeArm")
            ?? (mixamoAnimator != null ? mixamoAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null);
        leftHandBone = FindChild(stickmanTransform, "mixamorig:LeftHand")
            ?? (mixamoAnimator != null ? mixamoAnimator.GetBoneTransform(HumanBodyBones.LeftHand) : null);

        Transform rightArm = FindChild(stickmanTransform, "mixamorig:RightArm")
            ?? (mixamoAnimator != null ? mixamoAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null);
        Transform rightForeArm = FindChild(stickmanTransform, "mixamorig:RightForeArm")
            ?? (mixamoAnimator != null ? mixamoAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm) : null);
        rightHandBone = FindChild(stickmanTransform, "mixamorig:RightHand")
            ?? (mixamoAnimator != null ? mixamoAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null);

        float ikWeight = useFloatingHands ? 0f : 1f;

        // Configure Left Hand IK
        TwoBoneIKConstraint leftConstraint = ikLeftHandTarget.GetComponent<TwoBoneIKConstraint>();
        if (leftConstraint == null) leftConstraint = ikLeftHandTarget.gameObject.AddComponent<TwoBoneIKConstraint>();
        leftConstraint.weight = ikWeight;
        if (leftArm != null) leftConstraint.data.root = leftArm;
        if (leftForeArm != null) leftConstraint.data.mid = leftForeArm;
        if (leftHandBone != null) leftConstraint.data.tip = leftHandBone;
        leftConstraint.data.target = ikLeftHandTarget;
        leftConstraint.data.targetPositionWeight = 1f;
        leftConstraint.data.targetRotationWeight = 1f;

        // Configure Right Hand IK
        TwoBoneIKConstraint rightConstraint = ikRightHandTarget.GetComponent<TwoBoneIKConstraint>();
        if (rightConstraint == null) rightConstraint = ikRightHandTarget.gameObject.AddComponent<TwoBoneIKConstraint>();
        rightConstraint.weight = ikWeight;
        if (rightArm != null) rightConstraint.data.root = rightArm;
        if (rightForeArm != null) rightConstraint.data.mid = rightForeArm;
        if (rightHandBone != null) rightConstraint.data.tip = rightHandBone;
        rightConstraint.data.target = ikRightHandTarget;
        rightConstraint.data.targetPositionWeight = 1f;
        rightConstraint.data.targetRotationWeight = 1f;

        // Clean up any extraneous constraints on other objects
        foreach (TwoBoneIKConstraint constraint in stickmanTransform.GetComponentsInChildren<TwoBoneIKConstraint>(true))
        {
            if (constraint != leftConstraint && constraint != rightConstraint)
            {
                Destroy(constraint);
            }
        }

        // Ensure RigBuilder layer is configured
        bool hasRigLayer = false;
        for (int i = 0; i < rigBuilder.layers.Count; i++)
        {
            if (rigBuilder.layers[i].rig == ikRig)
            {
                hasRigLayer = true;
                break;
            }
        }
        if (!hasRigLayer)
        {
            rigBuilder.layers.Clear();
            rigBuilder.layers.Add(new RigLayer(ikRig, true));
        }

        EnsureRigBuilt();
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

        int localHeadLayer = LayerMask.NameToLayer("LocalHead");
        if (localHeadLayer != -1)
        {
            headObject.layer = localHeadLayer;
        }

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
        if (ikLeftHandTarget != null)
        {
            NetworkLeftHandPosition = ikLeftHandTarget.position;
            NetworkLeftHandRotation = ikLeftHandTarget.rotation;
        }
        else if (leftHand != null)
        {
            NetworkLeftHandPosition = leftHand.position;
            NetworkLeftHandRotation = leftHand.rotation * Quaternion.Euler(leftHandRotationOffset);
        }

        if (ikRightHandTarget != null)
        {
            NetworkRightHandPosition = ikRightHandTarget.position;
            NetworkRightHandRotation = ikRightHandTarget.rotation;
        }
        else if (rightHand != null)
        {
            NetworkRightHandPosition = rightHand.position;
            NetworkRightHandRotation = rightHand.rotation * Quaternion.Euler(rightHandRotationOffset);
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
        if (NetworkHeadPosition != Vector3.zero)
        {
            ReadPose(head, NetworkHeadPosition, NetworkHeadRotation);
            UpdateBodyPositionAndRotation(NetworkHeadPosition, NetworkHeadRotation);
        }
        if (NetworkLeftHandPosition != Vector3.zero)
        {
            ReadPose(leftHand, NetworkLeftHandPosition, NetworkLeftHandRotation);
        }
        if (NetworkRightHandPosition != Vector3.zero)
        {
            ReadPose(rightHand, NetworkRightHandPosition, NetworkRightHandRotation);
        }

        ApplyRemoteHandTargets();

        UpdateLegLocomotion();
    }

    private void LateUpdate()
    {
        if (useFloatingHands)
        {
            ApplyFloatingHandBones();
        }

        if (Object == null || !Object.IsValid) return;

        LogIkDiagnostics();
    }

    private void ApplyLocalHandTargets()
    {
        if (leftHand != null && ikLeftHandTarget != null)
        {
            Quaternion leftRot = leftHand.rotation * Quaternion.Euler(leftHandRotationOffset);
            ikLeftHandTarget.SetPositionAndRotation(leftHand.position, leftRot);
        }

        if (rightHand != null && ikRightHandTarget != null)
        {
            Quaternion rightRot = rightHand.rotation * Quaternion.Euler(rightHandRotationOffset);
            ikRightHandTarget.SetPositionAndRotation(rightHand.position, rightRot);
        }
    }

    private void ApplyRemoteHandTargets()
    {
        if (ikLeftHandTarget != null && NetworkLeftHandPosition != Vector3.zero)
            ikLeftHandTarget.SetPositionAndRotation(NetworkLeftHandPosition, NetworkLeftHandRotation);

        if (ikRightHandTarget != null && NetworkRightHandPosition != Vector3.zero)
            ikRightHandTarget.SetPositionAndRotation(NetworkRightHandPosition, NetworkRightHandRotation);
    }

    private void ApplyFloatingHandBones()
    {
        if (leftHandBone != null && ikLeftHandTarget != null)
        {
            leftHandBone.SetPositionAndRotation(ikLeftHandTarget.position, ikLeftHandTarget.rotation);
        }

        if (rightHandBone != null && ikRightHandTarget != null)
        {
            rightHandBone.SetPositionAndRotation(ikRightHandTarget.position, ikRightHandTarget.rotation);
        }
    }

    private void SetupFloatingHandsMesh()
    {
        if (!useFloatingHands || stickmanTransform == null || floatingMeshInitialized) return;

        try
        {
            SkinnedMeshRenderer bodySmr = null;
            foreach (var smr in stickmanTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.gameObject.name.ToLowerInvariant().Contains("body") ||
                    (smr.sharedMesh != null && smr.sharedMesh.name.ToLowerInvariant().Contains("mesh.002")))
                {
                    bodySmr = smr;
                    break;
                }
            }

            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                return;
            }

            Mesh originalMesh = bodySmr.sharedMesh;
            if (!originalMesh.isReadable)
            {
                Debug.LogWarning("FourSquareFusionPlayer: Stickman mesh is not readable on this platform. Preserving default avatar mesh.", this);
                return;
            }

            Mesh floatingMesh = Instantiate(originalMesh);
            floatingMesh.name = originalMesh.name + "_FloatingHands";

            Transform[] bones = bodySmr.bones;
            int boneCount = bones != null ? bones.Length : 0;
            if (boneCount == 0) return;

            // Bone categories: 0 = Body, 1 = Left Arm, 2 = Right Arm, 3 = Left Hand, 4 = Right Hand
            int[] boneCategory = new int[boneCount];
            int leftHandIndex = -1;
            int rightHandIndex = -1;
            int leftShoulderIndex = -1;
            int rightShoulderIndex = -1;

            for (int i = 0; i < boneCount; i++)
            {
                if (bones[i] == null) continue;
                string bName = bones[i].name;

                if (bName.Contains("LeftHand"))
                {
                    boneCategory[i] = 3;
                    if (bName == "mixamorig:LeftHand" || bName.EndsWith(":LeftHand") || bName == "LeftHand")
                        leftHandIndex = i;
                }
                else if (bName.Contains("RightHand"))
                {
                    boneCategory[i] = 4;
                    if (bName == "mixamorig:RightHand" || bName.EndsWith(":RightHand") || bName == "RightHand")
                        rightHandIndex = i;
                }
                else if (bName.Contains("LeftArm") || bName.Contains("LeftForeArm"))
                {
                    boneCategory[i] = 1;
                }
                else if (bName.Contains("RightArm") || bName.Contains("RightForeArm"))
                {
                    boneCategory[i] = 2;
                }
                else
                {
                    boneCategory[i] = 0;
                    if (bName.Contains("LeftShoulder")) leftShoulderIndex = i;
                    else if (bName.Contains("RightShoulder")) rightShoulderIndex = i;
                }
            }

            BoneWeight[] weights = floatingMesh.boneWeights;
            int vertexCount = floatingMesh.vertexCount;
            int[] vertexCategory = new int[vertexCount]; // 0 = Body, 1 = Arm, 2 = Hand

            for (int v = 0; v < vertexCount; v++)
            {
                BoneWeight bw = weights[v];
                float armWeight = 0f;
                float handWeight = 0f;
                float bodyWeight = 0f;

                AccumulateWeightCategory(bw.boneIndex0, bw.weight0, boneCategory, ref armWeight, ref handWeight, ref bodyWeight);
                AccumulateWeightCategory(bw.boneIndex1, bw.weight1, boneCategory, ref armWeight, ref handWeight, ref bodyWeight);
                AccumulateWeightCategory(bw.boneIndex2, bw.weight2, boneCategory, ref armWeight, ref handWeight, ref bodyWeight);
                AccumulateWeightCategory(bw.boneIndex3, bw.weight3, boneCategory, ref armWeight, ref handWeight, ref bodyWeight);

                if (handWeight > armWeight)
                {
                    vertexCategory[v] = 2; // Hand
                    CleanHandBoneWeight(ref bw, boneCategory, leftHandIndex, rightHandIndex);
                    weights[v] = bw;
                }
                else if (bodyWeight > armWeight)
                {
                    vertexCategory[v] = 0; // Body
                    CleanBodyBoneWeight(ref bw, boneCategory, leftShoulderIndex, rightShoulderIndex);
                    weights[v] = bw;
                }
                else
                {
                    vertexCategory[v] = 1; // Arm
                }
            }

            Vector3[] origVertices = originalMesh.vertices;
            HashSet<int> leftWristIndices = new HashSet<int>();
            HashSet<int> rightWristIndices = new HashSet<int>();
            HashSet<int> leftShoulderIndices = new HashSet<int>();
            HashSet<int> rightShoulderIndices = new HashSet<int>();

            int subMeshCount = floatingMesh.subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] triangles = floatingMesh.GetTriangles(s);
                List<int> cleanTriangles = new List<int>(triangles.Length);

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int t0 = triangles[i];
                    int t1 = triangles[i + 1];
                    int t2 = triangles[i + 2];

                    int c0 = vertexCategory[t0];
                    int c1 = vertexCategory[t1];
                    int c2 = vertexCategory[t2];

                    // Exclude any triangle with arm vertices
                    if (c0 == 1 || c1 == 1 || c2 == 1)
                    {
                        // Collect boundary vertices for cap placement
                        bool hasHand = c0 == 2 || c1 == 2 || c2 == 2;
                        bool hasBody = c0 == 0 || c1 == 0 || c2 == 0;

                        if (hasHand)
                        {
                            CollectBoundaryHandVertex(t0, c0, weights, boneCategory, leftWristIndices, rightWristIndices);
                            CollectBoundaryHandVertex(t1, c1, weights, boneCategory, leftWristIndices, rightWristIndices);
                            CollectBoundaryHandVertex(t2, c2, weights, boneCategory, leftWristIndices, rightWristIndices);
                        }
                        else if (hasBody)
                        {
                            CollectBoundaryBodyVertex(t0, c0, t0, t1, t2, weights, boneCategory, leftShoulderIndices, rightShoulderIndices, origVertices);
                            CollectBoundaryBodyVertex(t1, c1, t0, t1, t2, weights, boneCategory, leftShoulderIndices, rightShoulderIndices, origVertices);
                            CollectBoundaryBodyVertex(t2, c2, t0, t1, t2, weights, boneCategory, leftShoulderIndices, rightShoulderIndices, origVertices);
                        }
                        continue;
                    }

                    // Only keep triangle if all vertices belong to the same category (all Hand or all Body)
                    if (c0 == c1 && c1 == c2)
                    {
                        cleanTriangles.Add(t0);
                        cleanTriangles.Add(t1);
                        cleanTriangles.Add(t2);
                    }
                }

                floatingMesh.SetTriangles(cleanTriangles.ToArray(), s);
            }

            floatingMesh.boneWeights = weights;
            floatingMesh.RecalculateBounds();

            bodySmr.updateWhenOffscreen = true;
            bodySmr.sharedMesh = floatingMesh;
            floatingMeshInitialized = true;

            CreateVisualCaps(bodySmr, originalMesh, origVertices,
                leftWristIndices, rightWristIndices, leftShoulderIndices, rightShoulderIndices,
                leftHandIndex, rightHandIndex, leftShoulderIndex, rightShoulderIndex);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"FourSquareFusionPlayer: Failed to generate floating hands mesh ({ex.Message}). Preserving default mesh.", this);
        }
    }

    private void CreateVisualCaps(
        SkinnedMeshRenderer bodySmr,
        Mesh originalMesh,
        Vector3[] origVertices,
        HashSet<int> leftWristIndices,
        HashSet<int> rightWristIndices,
        HashSet<int> leftShoulderIndices,
        HashSet<int> rightShoulderIndices,
        int leftHandIndex,
        int rightHandIndex,
        int leftShoulderIndex,
        int rightShoulderIndex)
    {
        Material capMaterial = bodySmr.sharedMaterial;
        int capLayer = stickmanTransform.gameObject.layer;
        Matrix4x4[] bindposes = originalMesh.bindposes;

        List<Renderer> newRenderers = new List<Renderer>();
        if (avatarRenderers != null)
        {
            newRenderers.AddRange(avatarRenderers);
        }

        // 1. Left Wrist Cap (attached to leftHandBone)
        if (leftHandBone != null)
        {
            Matrix4x4 bindPose = (leftHandIndex >= 0 && bindposes != null && leftHandIndex < bindposes.Length)
                ? bindposes[leftHandIndex] : Matrix4x4.identity;
            ComputeOpeningBounds(leftWristIndices, origVertices, bindPose, out Vector3 centroid, out Vector3 extents);
            GameObject cap = CreateCapObject("LeftWristCap", leftHandBone, centroid, extents, capMaterial, capLayer);
            if (cap != null) newRenderers.Add(cap.GetComponent<Renderer>());
        }

        // 2. Right Wrist Cap (attached to rightHandBone)
        if (rightHandBone != null)
        {
            Matrix4x4 bindPose = (rightHandIndex >= 0 && bindposes != null && rightHandIndex < bindposes.Length)
                ? bindposes[rightHandIndex] : Matrix4x4.identity;
            ComputeOpeningBounds(rightWristIndices, origVertices, bindPose, out Vector3 centroid, out Vector3 extents);
            GameObject cap = CreateCapObject("RightWristCap", rightHandBone, centroid, extents, capMaterial, capLayer);
            if (cap != null) newRenderers.Add(cap.GetComponent<Renderer>());
        }

        // 3. Left Shoulder Cap (attached to leftShoulderBone)
        Transform leftShoulderBone = FindChild(stickmanTransform, "mixamorig:LeftShoulder")
            ?? FindChild(stickmanTransform, "LeftShoulder");
        if (leftShoulderBone != null)
        {
            Matrix4x4 bindPose = (leftShoulderIndex >= 0 && bindposes != null && leftShoulderIndex < bindposes.Length)
                ? bindposes[leftShoulderIndex] : Matrix4x4.identity;
            ComputeOpeningBounds(leftShoulderIndices, origVertices, bindPose, out Vector3 centroid, out Vector3 extents);
            GameObject cap = CreateCapObject("LeftShoulderCap", leftShoulderBone, centroid, extents, capMaterial, capLayer);
            if (cap != null) newRenderers.Add(cap.GetComponent<Renderer>());
        }

        // 4. Right Shoulder Cap (attached to rightShoulderBone)
        Transform rightShoulderBone = FindChild(stickmanTransform, "mixamorig:RightShoulder")
            ?? FindChild(stickmanTransform, "RightShoulder");
        if (rightShoulderBone != null)
        {
            Matrix4x4 bindPose = (rightShoulderIndex >= 0 && bindposes != null && rightShoulderIndex < bindposes.Length)
                ? bindposes[rightShoulderIndex] : Matrix4x4.identity;
            ComputeOpeningBounds(rightShoulderIndices, origVertices, bindPose, out Vector3 centroid, out Vector3 extents);
            GameObject cap = CreateCapObject("RightShoulderCap", rightShoulderBone, centroid, extents, capMaterial, capLayer);
            if (cap != null) newRenderers.Add(cap.GetComponent<Renderer>());
        }

        avatarRenderers = newRenderers.ToArray();
    }

    private static GameObject CreateCapObject(
        string name,
        Transform parentBone,
        Vector3 localPosition,
        Vector3 extents,
        Material material,
        int layer)
    {
        if (parentBone == null) return null;

        GameObject capObj = new GameObject(name);
        capObj.transform.SetParent(parentBone, false);
        capObj.transform.localPosition = localPosition;
        capObj.transform.localRotation = Quaternion.identity;
        capObj.transform.localScale = Vector3.one;
        capObj.layer = layer;

        MeshFilter mf = capObj.AddComponent<MeshFilter>();
        mf.sharedMesh = GenerateEllipsoidMesh(extents.x, extents.y, extents.z);

        MeshRenderer mr = capObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;

        return capObj;
    }

    private static Mesh GenerateEllipsoidMesh(float rx, float ry, float rz, int latSegments = 8, int lonSegments = 12)
    {
        Mesh mesh = new Mesh();
        mesh.name = "CapMesh";

        int vertCount = (latSegments + 1) * (lonSegments + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        List<int> triangles = new List<int>(latSegments * lonSegments * 12);

        float invLat = 1f / latSegments;
        float invLon = 1f / lonSegments;

        int vi = 0;
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = lat * Mathf.PI * invLat;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = lon * 2f * Mathf.PI * invLon;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                float x = rx * sinTheta * cosPhi;
                float y = ry * cosTheta;
                float z = rz * sinTheta * sinPhi;

                vertices[vi] = new Vector3(x, y, z);

                Vector3 normal = new Vector3(
                    rx > 0.0001f ? x / (rx * rx) : 0f,
                    ry > 0.0001f ? y / (ry * ry) : 0f,
                    rz > 0.0001f ? z / (rz * rz) : 0f).normalized;
                normals[vi] = normal;

                uvs[vi] = new Vector2(lon * invLon, lat * invLat);
                vi++;
            }
        }

        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int current = lat * (lonSegments + 1) + lon;
                int next = current + lonSegments + 1;

                // Outward-facing front triangles
                triangles.Add(current);
                triangles.Add(current + 1);
                triangles.Add(next);

                triangles.Add(next);
                triangles.Add(current + 1);
                triangles.Add(next + 1);

                // Inward-facing back triangles (double-sided so interior is never empty)
                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + 1);

                triangles.Add(next);
                triangles.Add(next + 1);
                triangles.Add(current + 1);
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static void ComputeOpeningBounds(
        HashSet<int> vertIndices,
        Vector3[] vertices,
        Matrix4x4 bindPose,
        out Vector3 localCentroid,
        out Vector3 extents)
    {
        if (vertIndices == null || vertIndices.Count == 0)
        {
            localCentroid = Vector3.zero;
            extents = new Vector3(0.045f, 0.045f, 0.045f);
            return;
        }

        Vector3 sum = Vector3.zero;
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (int v in vertIndices)
        {
            Vector3 boneLocal = bindPose.MultiplyPoint3x4(vertices[v]);
            sum += boneLocal;
            min = Vector3.Min(min, boneLocal);
            max = Vector3.Max(max, boneLocal);
        }

        localCentroid = sum / vertIndices.Count;
        Vector3 size = (max - min) * 0.5f;

        float minThickness = 0.02f;
        float rx = Mathf.Max(minThickness, size.x * 1.20f);
        float ry = Mathf.Max(minThickness, size.y * 1.20f);
        float rz = Mathf.Max(minThickness, size.z * 1.20f);
        extents = new Vector3(rx, ry, rz);
    }

    private static void CollectBoundaryHandVertex(
        int vertIndex,
        int cat,
        BoneWeight[] weights,
        int[] boneCategory,
        HashSet<int> leftSet,
        HashSet<int> rightSet)
    {
        if (cat != 2) return;
        BoneWeight bw = weights[vertIndex];
        bool isLeft = false;
        bool isRight = false;
        CheckHandSide(bw.boneIndex0, boneCategory, ref isLeft, ref isRight);
        CheckHandSide(bw.boneIndex1, boneCategory, ref isLeft, ref isRight);
        CheckHandSide(bw.boneIndex2, boneCategory, ref isLeft, ref isRight);
        CheckHandSide(bw.boneIndex3, boneCategory, ref isLeft, ref isRight);
        if (isRight) rightSet.Add(vertIndex);
        else leftSet.Add(vertIndex);
    }

    private static void CollectBoundaryBodyVertex(
        int vertIndex,
        int cat,
        int t0, int t1, int t2,
        BoneWeight[] weights,
        int[] boneCategory,
        HashSet<int> leftShoulderSet,
        HashSet<int> rightShoulderSet,
        Vector3[] origVerts)
    {
        if (cat != 0) return;

        bool isLeft = false;
        bool isRight = false;
        CheckArmSideInTriangle(t0, weights, boneCategory, ref isLeft, ref isRight);
        CheckArmSideInTriangle(t1, weights, boneCategory, ref isLeft, ref isRight);
        CheckArmSideInTriangle(t2, weights, boneCategory, ref isLeft, ref isRight);

        if (!isLeft && !isRight)
        {
            if (origVerts[vertIndex].x < 0f) isRight = true;
            else isLeft = true;
        }

        if (isRight) rightShoulderSet.Add(vertIndex);
        else leftShoulderSet.Add(vertIndex);
    }

    private static void CheckArmSideInTriangle(int vertIndex, BoneWeight[] weights, int[] boneCategory, ref bool isLeft, ref bool isRight)
    {
        BoneWeight bw = weights[vertIndex];
        CheckArmBoneSide(bw.boneIndex0, bw.weight0, boneCategory, ref isLeft, ref isRight);
        CheckArmBoneSide(bw.boneIndex1, bw.weight1, boneCategory, ref isLeft, ref isRight);
        CheckArmBoneSide(bw.boneIndex2, bw.weight2, boneCategory, ref isLeft, ref isRight);
        CheckArmBoneSide(bw.boneIndex3, bw.weight3, boneCategory, ref isLeft, ref isRight);
    }

    private static void CheckArmBoneSide(int boneIndex, float weight, int[] boneCategory, ref bool isLeft, ref bool isRight)
    {
        if (weight <= 0.001f || boneIndex < 0 || boneIndex >= boneCategory.Length) return;
        int cat = boneCategory[boneIndex];
        if (cat == 1) isLeft = true;
        else if (cat == 2) isRight = true;
    }

    private static void AccumulateWeightCategory(int boneIndex, float weight, int[] boneCategory, ref float armWeight, ref float handWeight, ref float bodyWeight)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= boneCategory.Length) return;

        int cat = boneCategory[boneIndex];
        if (cat == 1 || cat == 2) armWeight += weight;
        else if (cat == 3 || cat == 4) handWeight += weight;
        else bodyWeight += weight;
    }

    private static void CleanHandBoneWeight(ref BoneWeight bw, int[] boneCategory, int fallbackLeftHand, int fallbackRightHand)
    {
        bool isLeft = false;
        bool isRight = false;
        CheckHandSide(bw.boneIndex0, boneCategory, ref isLeft, ref isRight);
        CheckHandSide(bw.boneIndex1, boneCategory, ref isLeft, ref isRight);
        CheckHandSide(bw.boneIndex2, boneCategory, ref isLeft, ref isRight);
        CheckHandSide(bw.boneIndex3, boneCategory, ref isLeft, ref isRight);

        // Zero out arm influences
        if (IsArmBone(bw.boneIndex0, boneCategory)) bw.weight0 = 0f;
        if (IsArmBone(bw.boneIndex1, boneCategory)) bw.weight1 = 0f;
        if (IsArmBone(bw.boneIndex2, boneCategory)) bw.weight2 = 0f;
        if (IsArmBone(bw.boneIndex3, boneCategory)) bw.weight3 = 0f;

        float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
        if (total > 0.0001f)
        {
            float inv = 1f / total;
            bw.weight0 *= inv;
            bw.weight1 *= inv;
            bw.weight2 *= inv;
            bw.weight3 *= inv;
        }
        else
        {
            int fallback = isRight ? fallbackRightHand : fallbackLeftHand;
            if (fallback < 0) fallback = fallbackLeftHand >= 0 ? fallbackLeftHand : fallbackRightHand;
            if (fallback >= 0)
            {
                bw.boneIndex0 = fallback;
                bw.weight0 = 1f;
            }
        }
    }

    private static void CleanBodyBoneWeight(ref BoneWeight bw, int[] boneCategory, int fallbackLeftShoulder, int fallbackRightShoulder)
    {
        // Zero out arm influences
        if (IsArmBone(bw.boneIndex0, boneCategory)) bw.weight0 = 0f;
        if (IsArmBone(bw.boneIndex1, boneCategory)) bw.weight1 = 0f;
        if (IsArmBone(bw.boneIndex2, boneCategory)) bw.weight2 = 0f;
        if (IsArmBone(bw.boneIndex3, boneCategory)) bw.weight3 = 0f;

        float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
        if (total > 0.0001f)
        {
            float inv = 1f / total;
            bw.weight0 *= inv;
            bw.weight1 *= inv;
            bw.weight2 *= inv;
            bw.weight3 *= inv;
        }
        else
        {
            int fallback = fallbackLeftShoulder >= 0 ? fallbackLeftShoulder : (fallbackRightShoulder >= 0 ? fallbackRightShoulder : 0);
            bw.boneIndex0 = fallback;
            bw.weight0 = 1f;
        }
    }

    private static void CheckHandSide(int boneIndex, int[] boneCategory, ref bool isLeft, ref bool isRight)
    {
        if (boneIndex < 0 || boneIndex >= boneCategory.Length) return;
        int cat = boneCategory[boneIndex];
        if (cat == 3) isLeft = true;
        else if (cat == 4) isRight = true;
    }

    private static bool IsArmBone(int boneIndex, int[] boneCategory)
    {
        if (boneIndex < 0 || boneIndex >= boneCategory.Length) return false;
        int cat = boneCategory[boneIndex];
        return cat == 1 || cat == 2;
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
        bool isFusionRunning = Runner != null && Runner.IsRunning;
        bool isLocal = isFusionRunning
            ? (Object != null && Object.IsValid && Object.HasInputAuthority)
            : (Object == null || !Object.IsValid || Object.HasInputAuthority);
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
        bool isFusionRunning = Runner != null && Runner.IsRunning;
        bool isLocal = isFusionRunning
            ? (Object != null && Object.IsValid && Object.HasInputAuthority)
            : (Object == null || !Object.IsValid || Object.HasInputAuthority);

        if (isLocal)
        {
            ApplyLocalHandTargets();
            if (!isFusionRunning)
            {
                if (head != null)
                {
                    UpdateBodyPositionAndRotation(head.position, head.rotation);
                }
                UpdateLegLocomotion();
            }
        }
        else
        {
            ApplyRemoteHandTargets();
        }

        if (!isLocal) return;

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
        var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Controller | UnityEngine.XR.InputDeviceCharacteristics.TrackedDevice,
            devices);
        if (devices.Count > 0) return true;

        if (UnityEngine.XR.XRSettings.isDeviceActive)
            return true;

#if UNITY_EDITOR
        XRInteractionSimulator interactionSimulator =
            FindAnyObjectByType<XRInteractionSimulator>();
        if (interactionSimulator != null && interactionSimulator.isActiveAndEnabled)
            return true;

        XRDeviceSimulator simulator =
            FindAnyObjectByType<XRDeviceSimulator>();
        if (simulator != null && simulator.isActiveAndEnabled)
            return true;

        return true;
#else
        return true;
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