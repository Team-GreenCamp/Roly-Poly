using UnityEngine;
using UnityEditor;
#if UNITY_EDITOR
using UnityEditor.Events;
#endif

public class Level1MapBuilder : EditorWindow
{
    [MenuItem("Tools/Build Level 1 Map")]
    public static void BuildLevel1Map()
    {
        // 1. 현재 열려있는 씬 체크 및 사용자 확인
        if (!EditorUtility.DisplayDialog("Level 1 맵 빌더", 
            "프로젝트의 실제 데모 프리팹들(Player, Door, PressurePlate, Lever, Box 등)을 기반으로\n'1단계 테스트 맵 기믹 세트'를 조립하시겠습니까?\n\n(이미 동일한 이름의 맵 루트 오브젝트가 존재한다면 덮어씁니다.)", 
            "예, 빌드합니다", "아니오"))
        {
            return;
        }

        // 2. 기존의 레벨 루트가 있다면 삭제하고 새로 생성 (초기화)
        GameObject existingRoot = GameObject.Find("Level_1_Root");
        if (existingRoot != null)
        {
            DestroyImmediate(existingRoot);
        }

        GameObject root = new GameObject("Level_1_Root");
        Undo.RegisterCreatedObjectUndo(root, "Build Level 1 Map");

        // 3. 태그 검증 및 추가 ("Box" 태그가 없으면 자동 생성)
        SetupBoxTag();

        // 4. 프리팹 로드하기
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        GameObject pressurePlatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/PressurePlate.prefab");
        GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Door.prefab");
        GameObject buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Button.prefab");
        GameObject leverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/SwitchLever.prefab");
        GameObject lightBoxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/LightObject.prefab");
        GameObject heavyBoxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/HeavyObject.prefab");

        // 프리팹 유효성 검사
        if (playerPrefab == null || pressurePlatePrefab == null || doorPrefab == null || lightBoxPrefab == null)
        {
            EditorUtility.DisplayDialog("빌드 실패", 
                "필요한 프리팹 중 일부를 Assets/Prefab 폴더에서 로드할 수 없습니다.\n프리팹 파일명과 경로를 확인해 주세요.", "확인");
            return;
        }

        // ==========================================
        // 📌 SECTION 0: 바닥 (Floor) & 스폰 & 낙사
        // ==========================================
        GameObject section0 = new GameObject("Section 0 - Start & Floor");
        section0.transform.SetParent(root.transform);

        // 메인 바닥
        GameObject mainFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mainFloor.name = "Start_Floor";
        mainFloor.transform.SetParent(section0.transform);
        mainFloor.transform.position = new Vector3(0, -0.5f, 0);
        mainFloor.transform.localScale = new Vector3(15, 1, 15);
        SetMaterialColor(mainFloor, new Color(0.2f, 0.22f, 0.25f)); // 깔끔한 그레이-블루 URP 컬러

        // 시작 지점 (Spawn Point)
        GameObject spawnPoint = new GameObject("SpawnPoint");
        spawnPoint.transform.SetParent(section0.transform);
        spawnPoint.transform.position = new Vector3(0, 1.0f, -5f);
        spawnPoint.tag = "Respawn";

        // 🌟 실제 플레이어 프리팹 소환
        GameObject playerInst = PrefabUtility.InstantiatePrefab(playerPrefab) as GameObject;
        playerInst.name = "Player";
        playerInst.transform.position = new Vector3(0, 1.5f, -5f);
        Undo.RegisterCreatedObjectUndo(playerInst, "Spawn Player Prefab");

        // 낙사 위험지역 (Respawn Hazard)
        GameObject hazard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hazard.name = "Respawn_Hazard";
        hazard.transform.SetParent(section0.transform);
        hazard.transform.position = new Vector3(20, -10f, 0);
        hazard.transform.localScale = new Vector3(200, 2, 200);
        Collider hazardCollider = hazard.GetComponent<Collider>();
        if (hazardCollider != null) hazardCollider.isTrigger = true;
        
        // 스크립트 추가
        RespawnHazard hazardScript = hazard.AddComponent<RespawnHazard>();
        SetMaterialColor(hazard, new Color(0.8f, 0.1f, 0.1f, 0.25f)); // 살짝 붉은 경고 영역

        // ==========================================
        // 📌 SECTION 1: 클라이밍 및 단차 오르기
        // ==========================================
        GameObject section1 = new GameObject("Section 1 - Climbing & Jump");
        section1.transform.SetParent(root.transform);

        // 오르막 플랫폼 1
        GameObject stair1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stair1.name = "Stair_1";
        stair1.transform.SetParent(section1.transform);
        stair1.transform.position = new Vector3(0, 0.5f, 5f);
        stair1.transform.localScale = new Vector3(4, 1, 3);
        SetMaterialColor(stair1, new Color(0.35f, 0.45f, 0.6f));

        // 오르막 플랫폼 2
        GameObject stair2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stair2.name = "Stair_2";
        stair2.transform.SetParent(section1.transform);
        stair2.transform.position = new Vector3(0, 1.5f, 8f);
        stair2.transform.localScale = new Vector3(4, 1, 3);
        SetMaterialColor(stair2, new Color(0.35f, 0.45f, 0.6f));

        // 수직 기어오르기 벽 (Climbing Wall)
        GameObject climbWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        climbWall.name = "Climbing_Wall";
        climbWall.transform.SetParent(section1.transform);
        climbWall.transform.position = new Vector3(0, 4.5f, 11f);
        climbWall.transform.localScale = new Vector3(6, 5, 1);
        SetMaterialColor(climbWall, new Color(0.8f, 0.45f, 0.2f)); // 따뜻한 클라이밍 가이드 색상

        // 2층 안착장
        GameObject floor2nd = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor2nd.name = "Second_Floor";
        floor2nd.transform.SetParent(section1.transform);
        floor2nd.transform.position = new Vector3(0, 6.5f, 15f);
        floor2nd.transform.localScale = new Vector3(8, 1, 7);
        SetMaterialColor(floor2nd, new Color(0.2f, 0.22f, 0.25f));

        // ==========================================
        // 📌 SECTION 2: 움직이는 발판 (Moving Platform)
        // ==========================================
        GameObject section2 = new GameObject("Section 2 - Moving Platforms");
        section2.transform.SetParent(root.transform);

        GameObject movingPlatform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        movingPlatform.name = "Moving_Platform";
        movingPlatform.transform.SetParent(section2.transform);
        movingPlatform.transform.position = new Vector3(0, 6.5f, 21.5f);
        movingPlatform.transform.localScale = new Vector3(3.5f, 0.6f, 3.5f);
        SetMaterialColor(movingPlatform, new Color(0.2f, 0.75f, 0.55f)); // 상쾌한 민트 URP 컬러

        MovingPlatform mpScript = movingPlatform.AddComponent<MovingPlatform>();
        mpScript.moveOffset = new Vector3(8f, 0f, 4.5f); // 대각선 무빙
        mpScript.moveSpeed = 2.5f;

        // 건너편 안착장
        GameObject landingFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        landingFloor.name = "Landing_Floor";
        landingFloor.transform.SetParent(section2.transform);
        landingFloor.transform.position = new Vector3(8f, 6.5f, 30.5f);
        landingFloor.transform.localScale = new Vector3(8, 1, 8);
        SetMaterialColor(landingFloor, new Color(0.2f, 0.22f, 0.25f));

        // ==========================================
        // 📌 SECTION 3: 🌟 데모 프리팹 기반의 퍼즐 문 (Pressure Plate Puzzle)
        // ==========================================
        GameObject section3 = new GameObject("Section 3 - Prefab Puzzle Room");
        section3.transform.SetParent(root.transform);

        // 1. 🌟 실제 Door 프리팹 소환
        GameObject doorInst = PrefabUtility.InstantiatePrefab(doorPrefab) as GameObject;
        doorInst.name = "Prefab_Door";
        doorInst.transform.SetParent(section3.transform);
        doorInst.transform.position = new Vector3(8f, 7f, 34.5f); // 문 배치 좌표
        
        DoorController doorCtrl = doorInst.GetComponentInChildren<DoorController>();
        if (doorCtrl != null)
        {
            doorCtrl.doorType = DoorController.DoorType.Slide;
            doorCtrl.openOffset = new Vector3(0, 4.5f, 0); // 위로 슬라이드
            doorCtrl.moveSpeed = 3f;
            doorCtrl.canDirectInteract = false; // 압력판 전용
        }

        // 2. 🌟 실제 PressurePlate 프리팹 소환
        GameObject plateInst = PrefabUtility.InstantiatePrefab(pressurePlatePrefab) as GameObject;
        plateInst.name = "Prefab_Pressure_Plate";
        plateInst.transform.SetParent(section3.transform);
        plateInst.transform.position = new Vector3(5f, 7.05f, 29f); // 압력판 위치
        
        PressurePlateGimmick plateScript = plateInst.GetComponentInChildren<PressurePlateGimmick>();

        // 3. 🌟 실제 LightObject (가벼운 상자) 프리팹 소환
        GameObject lightBoxInst = PrefabUtility.InstantiatePrefab(lightBoxPrefab) as GameObject;
        lightBoxInst.name = "Prefab_Light_Box";
        lightBoxInst.transform.SetParent(section3.transform);
        lightBoxInst.transform.position = new Vector3(10f, 8.5f, 29f); // 상자 스폰 포인트
        lightBoxInst.tag = "Box"; // 압력판 감지용 태그 적용

        // 4. ⭐ 에디터를 통한 기믹 간 이벤트 자동 연동
#if UNITY_EDITOR
        if (plateScript != null && doorCtrl != null)
        {
            if (plateScript.onActivate == null) plateScript.onActivate = new UnityEngine.Events.UnityEvent();
            if (plateScript.onDeactivate == null) plateScript.onDeactivate = new UnityEngine.Events.UnityEvent();

            // 리스너 비우기 후 새로 바인딩 (중복 등록 방지)
            while (plateScript.onActivate.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(plateScript.onActivate, 0);
            while (plateScript.onDeactivate.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(plateScript.onDeactivate, 0);

            UnityEventTools.AddPersistentListener(plateScript.onActivate, new UnityEngine.Events.UnityAction(doorCtrl.OpenDoor));
            UnityEventTools.AddPersistentListener(plateScript.onDeactivate, new UnityEngine.Events.UnityAction(doorCtrl.CloseDoor));
        }
#endif

        // ==========================================
        // 📌 SECTION 3.5: 🌟 추가 데모 레버 기믹 (Switch Lever Section)
        // ==========================================
        // 징검다리 전의 두 번째 문을 만들고, 레버를 통해 열 수 있게 하여 데모 맛을 듬뿍 살립니다!
        GameObject entryFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        entryFloor.name = "Bridge_Entry_Floor";
        entryFloor.transform.SetParent(section3.transform);
        entryFloor.transform.position = new Vector3(8f, 6.5f, 41.5f);
        entryFloor.transform.localScale = new Vector3(6, 1, 5);
        SetMaterialColor(entryFloor, new Color(0.2f, 0.22f, 0.25f));

        // 두 번째 문 (Second Door - 레버로 제어)
        GameObject secondDoorInst = PrefabUtility.InstantiatePrefab(doorPrefab) as GameObject;
        secondDoorInst.name = "Prefab_Second_Door";
        secondDoorInst.transform.SetParent(section3.transform);
        secondDoorInst.transform.position = new Vector3(8f, 7f, 44f); // 문 배치 좌표

        DoorController secondDoorCtrl = secondDoorInst.GetComponentInChildren<DoorController>();
        if (secondDoorCtrl != null)
        {
            secondDoorCtrl.doorType = DoorController.DoorType.Slide;
            secondDoorCtrl.openOffset = new Vector3(0, 4.5f, 0);
            secondDoorCtrl.moveSpeed = 3f;
            secondDoorCtrl.canDirectInteract = false; // 레버 전용
        }

        // 🌟 SwitchLever 프리팹 소환
        if (leverPrefab != null)
        {
            GameObject leverInst = PrefabUtility.InstantiatePrefab(leverPrefab) as GameObject;
            leverInst.name = "Prefab_Switch_Lever";
            leverInst.transform.SetParent(section3.transform);
            leverInst.transform.position = new Vector3(8f, 7.05f, 39f); // 다리 진입 판 위의 레버

            LeverGimmick leverScript = leverInst.GetComponentInChildren<LeverGimmick>();
            
#if UNITY_EDITOR
            if (leverScript != null && secondDoorCtrl != null)
            {
                if (leverScript.onToggleOn == null) leverScript.onToggleOn = new UnityEngine.Events.UnityEvent();
                if (leverScript.onToggleOff == null) leverScript.onToggleOff = new UnityEngine.Events.UnityEvent();

                while (leverScript.onToggleOn.GetPersistentEventCount() > 0)
                    UnityEventTools.RemovePersistentListener(leverScript.onToggleOn, 0);
                while (leverScript.onToggleOff.GetPersistentEventCount() > 0)
                    UnityEventTools.RemovePersistentListener(leverScript.onToggleOff, 0);

                UnityEventTools.AddPersistentListener(leverScript.onToggleOn, new UnityEngine.Events.UnityAction(secondDoorCtrl.OpenDoor));
                UnityEventTools.AddPersistentListener(leverScript.onToggleOff, new UnityEngine.Events.UnityAction(secondDoorCtrl.CloseDoor));
            }
#endif
        }

        // ==========================================
        // 📌 SECTION 4: 무너지는 발판 징검다리
        // ==========================================
        GameObject section4 = new GameObject("Section 4 - Falling Platforms");
        section4.transform.SetParent(root.transform);

        float startZ = 49f;
        float spacingZ = 4.5f;
        for (int i = 0; i < 3; i++)
        {
            GameObject fallingPlatform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallingPlatform.name = $"Falling_Platform_{i + 1}";
            fallingPlatform.transform.SetParent(section4.transform);
            fallingPlatform.transform.position = new Vector3(8f, 6.5f, startZ + (i * spacingZ));
            fallingPlatform.transform.localScale = new Vector3(2.5f, 0.5f, 2.5f);
            SetMaterialColor(fallingPlatform, new Color(0.75f, 0.25f, 0.25f)); // 경고성 주황-빨강 URP 컬러

            FallingPlatform fpScript = fallingPlatform.AddComponent<FallingPlatform>();
            fpScript.fallDelay = 0.8f - (i * 0.15f);
            fpScript.respawnDelay = 3.0f;
        }

        // 골인 최종 안착 플랫
        GameObject goalFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        goalFloor.name = "Goal_Floor";
        goalFloor.transform.SetParent(section4.transform);
        goalFloor.transform.position = new Vector3(8f, 6.5f, startZ + (3 * spacingZ) + 2f);
        goalFloor.transform.localScale = new Vector3(10, 1, 10);
        SetMaterialColor(goalFloor, new Color(0.2f, 0.22f, 0.25f));

        // ==========================================
        // 📌 SECTION 5: 결승 지점 및 기둥
        // ==========================================
        GameObject section5 = new GameObject("Section 5 - Goal Flag");
        section5.transform.SetParent(root.transform);

        GameObject goalPost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        goalPost.name = "Goal_Post";
        goalPost.transform.SetParent(section5.transform);
        goalPost.transform.position = new Vector3(8f, 8.5f, startZ + (3 * spacingZ) + 5f);
        goalPost.transform.localScale = new Vector3(0.3f, 3f, 0.3f);
        SetMaterialColor(goalPost, new Color(0.9f, 0.9f, 0.9f));

        GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flag.name = "Goal_Flag";
        flag.transform.SetParent(goalPost.transform);
        flag.transform.localPosition = new Vector3(1.2f, 0.7f, 0);
        flag.transform.localScale = new Vector3(2f, 0.6f, 0.1f);
        SetMaterialColor(flag, new Color(0.15f, 0.65f, 0.25f)); // 완주 기념 그린 깃발

        // 🌟 결승점에 끈적하게 대기 중인 탈것(Vehicle) 추가 배치하여 데모 느낌 충족!
        GameObject vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/RoomContainer.prefab"); // 탈것 또는 컨테이너
        if (vehiclePrefab != null)
        {
            GameObject vehInst = PrefabUtility.InstantiatePrefab(vehiclePrefab) as GameObject;
            vehInst.name = "Prefab_Goal_Decoration";
            vehInst.transform.SetParent(section5.transform);
            vehInst.transform.position = new Vector3(8f, 7f, startZ + (3 * spacingZ) + 9f);
        }

        // 🎉 완성 팝업
        EditorUtility.DisplayDialog("데모 기반 빌드 성공!", 
            "Demo.unity의 실전 프리팹 에셋들을 기반으로 한 1단계 맵 빌드가 완료되었습니다!\n\n[주요 변경점]\n1. 실제 Player 프리팹 스폰 완료\n2. 실제 Door 프리팹 2개 및 압력판 프리팹 1개 배치\n3. 실제 LightObject(상자) 소환 완료\n4. 다리 진입 전 SwitchLever 프리팹과 Second Door 연동 추가\n5. 골인 지역 데모 데코레이션 배치 완료", 
            "신난다!");

        // 더티 플래그 마크
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }

    private static void SetMaterialColor(GameObject obj, Color color)
    {
        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                litShader = Shader.Find("Standard");
            }

            if (litShader != null)
            {
                Material tempMat = new Material(litShader);
                
                if (tempMat.HasProperty("_BaseColor"))
                {
                    tempMat.SetColor("_BaseColor", color);
                }
                else if (tempMat.HasProperty("_Color"))
                {
                    tempMat.SetColor("_Color", color);
                }

                if (color.a < 1.0f)
                {
                    if (tempMat.HasProperty("_Surface"))
                    {
                        tempMat.SetFloat("_Surface", 1);
                        tempMat.SetFloat("_Blend", 0);
                        tempMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        tempMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        tempMat.SetInt("_ZWrite", 0);
                        tempMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        tempMat.renderQueue = 3000;
                    }
                    else
                    {
                        tempMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        tempMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        tempMat.SetInt("_ZWrite", 0);
                        tempMat.DisableKeyword("_ALPHATEST_ON");
                        tempMat.EnableKeyword("_ALPHABLEND_ON");
                        tempMat.renderQueue = 3000;
                    }
                }
                else
                {
                    if (tempMat.HasProperty("_Metallic")) tempMat.SetFloat("_Metallic", 0.1f);
                    if (tempMat.HasProperty("_Smoothness")) tempMat.SetFloat("_Smoothness", 0.4f);
                }

                renderer.sharedMaterial = tempMat;
            }
        }
    }

    private static void SetupBoxTag()
    {
#if UNITY_EDITOR
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        bool hasBoxTag = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
            if (t.stringValue.Equals("Box"))
            {
                hasBoxTag = true;
                break;
            }
        }

        if (!hasBoxTag)
        {
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            SerializedProperty newTag = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
            newTag.stringValue = "Box";
            tagManager.ApplyModifiedProperties();
            Debug.Log("🏷️ Project Settings에 'Box' 태그를 자동으로 추가하였습니다.");
        }
#endif
    }
}
