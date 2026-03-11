using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

public static class TpsPlayerSetupGenerator
{
    private const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string AnimatorControllerPath = "Assets/Animations/PlayerTPS.controller";
    private const string AutoRunSessionKey = "TpsPlayerSetupGenerator.AutoRun";

    [InitializeOnLoadMethod]
    private static void AutoRunInEditor()
    {
        if (Application.isBatchMode || SessionState.GetBool(AutoRunSessionKey, false) || !NeedsSetup())
        {
            return;
        }

        SessionState.SetBool(AutoRunSessionKey, true);
        EditorApplication.delayCall += TryAutoRun;
    }

    [MenuItem("Tools/TPS/Setup Player Rig")]
    public static void Run()
    {
        EnsureFolder("Assets/Animations");

        AnimatorController controller = CreateAnimatorController();
        ConfigurePlayerPrefab(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("TPS player setup completed.");
    }

    public static void RunBatch()
    {
        Run();
        EditorApplication.Exit(0);
    }

    private static void TryAutoRun()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoRun;
            return;
        }

        try
        {
            if (NeedsSetup())
            {
                Run();
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static bool NeedsSetup()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
        if (controller == null)
        {
            return true;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            return false;
        }

        Animator animator = prefab.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController != controller || animator.applyRootMotion)
        {
            return true;
        }

        return prefab.GetComponent<CharacterController>() == null
            || prefab.GetComponent<PlayerInput>() == null
            || prefab.GetComponent<PlayerController>() == null
            || prefab.transform.Find("CameraRoot") == null;
    }

    private static AnimatorController CreateAnimatorController()
    {
        AssetDatabase.DeleteAsset(AnimatorControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);
        controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        BlendTree locomotionTree = new BlendTree
        {
            name = "LocomotionTree",
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveY",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(locomotionTree, controller);

        AnimationClip idle = LoadClip("Assets/Assets/Basic Shooter Pack/rifle aiming idle.fbx");
        AnimationClip walkForward = LoadClip("Assets/Assets/Basic Shooter Pack/walking.fbx");
        AnimationClip walkBackward = LoadClip("Assets/Assets/Basic Shooter Pack/walking backwards.fbx");
        AnimationClip strafeLeft = LoadClip("Assets/Assets/Basic Shooter Pack/strafe left.fbx");
        AnimationClip strafeRight = LoadClip("Assets/Assets/Basic Shooter Pack/strafe right.fbx");
        AnimationClip runForward = LoadClip("Assets/Assets/Basic Shooter Pack/rifle run.fbx");
        AnimationClip jump = LoadClip("Assets/Assets/Basic Shooter Pack/rifle jump.fbx");

        AddMotion(locomotionTree, idle, Vector2.zero);
        AddMotion(locomotionTree, walkForward, new Vector2(0f, 0.5f));
        AddMotion(locomotionTree, runForward, new Vector2(0f, 1f));
        AddMotion(locomotionTree, walkBackward, new Vector2(0f, -0.5f));
        AddMotion(locomotionTree, strafeLeft, new Vector2(-0.5f, 0f));
        AddMotion(locomotionTree, strafeRight, new Vector2(0.5f, 0f));

        AnimatorState locomotionState = stateMachine.AddState("Locomotion", new Vector3(300f, 120f, 0f));
        locomotionState.motion = locomotionTree;

        AnimatorState airborneState = stateMachine.AddState("Airborne", new Vector3(580f, 110f, 0f));
        airborneState.motion = jump;

        stateMachine.defaultState = locomotionState;

        AnimatorStateTransition locomotionToAirborne = locomotionState.AddTransition(airborneState);
        ConfigureTransition(locomotionToAirborne);
        locomotionToAirborne.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

        AnimatorStateTransition locomotionToAirborneFallback = locomotionState.AddTransition(airborneState);
        ConfigureTransition(locomotionToAirborneFallback);
        locomotionToAirborneFallback.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
        locomotionToAirborneFallback.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalVelocity");

        AnimatorStateTransition airborneToLocomotion = airborneState.AddTransition(locomotionState);
        ConfigureTransition(airborneToLocomotion);
        airborneToLocomotion.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void ConfigurePlayerPrefab(AnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            CharacterController characterController = GetOrAddComponent<CharacterController>(root);
            PlayerInput playerInput = GetOrAddComponent<PlayerInput>(root);
            PlayerController playerController = GetOrAddComponent<PlayerController>(root);
            Animator animator = root.GetComponent<Animator>();

            if (animator == null)
            {
                throw new System.InvalidOperationException("Player prefab must have an Animator on the root.");
            }

            Bounds bounds = CalculateRendererBounds(root);
            float characterHeight = Mathf.Max(1.6f, bounds.size.y);
            float controllerRadius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.45f, 0.25f, 0.45f);

            characterController.height = characterHeight;
            characterController.radius = controllerRadius;
            characterController.center = new Vector3(0f, characterHeight * 0.5f, 0f);
            characterController.stepOffset = Mathf.Min(0.4f, characterHeight * 0.2f);
            characterController.slopeLimit = 45f;
            characterController.skinWidth = 0.08f;
            characterController.minMoveDistance = 0.001f;

            Transform cameraRoot = root.transform.Find("CameraRoot");
            if (cameraRoot == null)
            {
                GameObject cameraRootObject = new GameObject("CameraRoot");
                cameraRoot = cameraRootObject.transform;
                cameraRoot.SetParent(root.transform, false);
            }

            float cameraHeight = Mathf.Clamp(characterHeight * 0.9f, 1.35f, 1.75f);
            cameraRoot.localPosition = new Vector3(0f, cameraHeight, 0f);
            cameraRoot.localRotation = Quaternion.identity;
            cameraRoot.localScale = Vector3.one;

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
            {
                throw new System.InvalidOperationException("Input action asset could not be loaded.");
            }

            playerInput.actions = actions;
            playerInput.defaultActionMap = "Player";
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            playerInput.neverAutoSwitchControlSchemes = false;
            playerInput.camera = null;

            SerializedObject serializedPlayerController = new SerializedObject(playerController);
            serializedPlayerController.FindProperty("characterController").objectReferenceValue = characterController;
            serializedPlayerController.FindProperty("playerInput").objectReferenceValue = playerInput;
            serializedPlayerController.FindProperty("animator").objectReferenceValue = animator;
            serializedPlayerController.FindProperty("cameraRoot").objectReferenceValue = cameraRoot;
            serializedPlayerController.FindProperty("walkSpeed").floatValue = 3.5f;
            serializedPlayerController.FindProperty("sprintSpeed").floatValue = 6f;
            serializedPlayerController.FindProperty("rotationSpeed").floatValue = 720f;
            serializedPlayerController.FindProperty("jumpHeight").floatValue = 1.2f;
            serializedPlayerController.FindProperty("gravity").floatValue = -25f;
            serializedPlayerController.FindProperty("terminalVelocity").floatValue = -50f;
            serializedPlayerController.FindProperty("lookSensitivity").floatValue = 0.12f;
            serializedPlayerController.FindProperty("minPitch").floatValue = -35f;
            serializedPlayerController.FindProperty("maxPitch").floatValue = 60f;
            serializedPlayerController.FindProperty("groundedOffset").floatValue = 0.1f;
            serializedPlayerController.FindProperty("groundedRadius").floatValue = 0.25f;
            serializedPlayerController.FindProperty("groundLayers").FindPropertyRelative("m_Bits").intValue = -1;
            serializedPlayerController.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Bounds CalculateRendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, new Vector3(1f, 1.8f, 1f));
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
        {
            bounds.Encapsulate(renderer.bounds);
        }

        return bounds;
    }

    private static AnimationClip LoadClip(string path)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__"));

        if (clip == null)
        {
            throw new System.InvalidOperationException($"Animation clip not found: {path}");
        }

        return clip;
    }

    private static void AddMotion(BlendTree tree, Motion motion, Vector2 position)
    {
        if (motion != null)
        {
            tree.AddChild(motion, position);
        }
    }

    private static void ConfigureTransition(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.08f;
        transition.exitTime = 0f;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string[] parts = path.Split('/');
        string current = parts[0];

        for (int index = 1; index < parts.Length; index++)
        {
            string next = $"{current}/{parts[index]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[index]);
            }

            current = next;
        }
    }
}
