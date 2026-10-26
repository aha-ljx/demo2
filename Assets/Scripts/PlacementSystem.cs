using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

// 在相机的 LateUpdate 之后计算鼠标落点，保证平移、缩放和切换视角时预览一致。
[DefaultExecutionOrder(100)]
public partial class PlacementSystem : MonoBehaviour
{
    [Header("场景对象")]
    public Transform player;
    public CharacterController playerCollider;
    public Transform headAnchor;
    public Camera worldCamera;

    [Header("按 1～5 的顺序填写")]
    public GameObject[] prefabs = new GameObject[5];
    public Image[] slotImages = new Image[5];

    [Header("距离")]
    public float minimumPlaceDistance = 4f;
    public float takeBackDistance = 3f;

    [Header("鸟瞰操作")]
    public float dragHoldTime = 0.3f;

    [Header("产线机械臂速度倍率（加工与传送带速度独立）")]
    [Min(0.1f)] public float armTravelMultiplier = 3f;
    [Min(0.1f)] public float armPickupMultiplier = 3f;
    [Min(0.1f)] public float armRotationMultiplier = 2f;

    private readonly KeyCode[] numberKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5
    };

    private readonly KeyCode[] keypadKeys =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3,
        KeyCode.Keypad4, KeyCode.Keypad5
    };

    private readonly List<GameObject> placed = new List<GameObject>();
    private readonly Dictionary<GameObject, int> placedTypes = new Dictionary<GameObject, int>();
    private readonly string[] equipmentNames =
    {
        "冲床", "折弯", "焊接", "机械臂", "长滑槽"
    };
    private static readonly int[] equipmentPrices = { 3000, 1000, 1500, 500, 50 };

    // Only confirmed placements count; previews and moving existing equipment are free.
    public long CurrentCost
    {
        get
        {
            long total = 0;
            foreach (var entry in placedTypes)
                if (entry.Key != null) total += equipmentPrices[entry.Value];
            return total;
        }
    }

    private int selected = -1;
    private GameObject headPreview;
    private GameObject groundPreview;
    private Material previewMaterial;
    private bool canPlace;
    private string placementBlockReason;
    private GameController cameraController;
    private GameObject nearbyPanel;
    private TextMeshProUGUI nearbyText;
    private GameObject infoPanel;
    private TextMeshProUGUI infoText;
    private GameObject inspectedItem;
    private GameObject pressedItem;
    private float pressedAt;
    private Vector3 dragOffset;
    private bool dragging;
    private bool wasBirdMode;
    private float rotateBlockedUntil;
    private float equipmentScale = 1f;
    private FactoryProduction production;
    private RawMaterialSupply rawSupply;
    private TextMeshProUGUI productionText;
    private TextMeshProUGUI costText;
    private FactoryWorkpiece carriedProduct;
    private Quaternion productPlacementRotation;
    private bool supplyPressed, supplyDragging;
    private float supplyPressedAt;
    private Vector3 supplyPressPoint, supplyDragOffset;

    private void Start()
    {
        // 奶蛋保持原尺寸，以可见身高标定整套设备的统一缩放比例。
        if (prefabs.Length > 3 && prefabs[3] != null)
        {
            GameObject reference = Instantiate(prefabs[3]);
            float armHeight = GetVisualBounds(reference).size.y;
            GameController controller = worldCamera.GetComponent<GameController>();
            float playerHeight = controller != null && controller.model != null
                ? GetVisualBounds(controller.model.gameObject).size.y
                : playerCollider.height;
            equipmentScale = Mathf.Max(1f, playerHeight * 1.5f / Mathf.Max(0.01f, armHeight));
            reference.SetActive(false);
            Destroy(reference);
        }
        rawSupply = new GameObject("开局原料板堆").AddComponent<RawMaterialSupply>();
        int level = MainMenuController.ConsumeLevelRequest();
        rawSupply.Initialize(player, playerCollider, production, equipmentScale * 0.8f, level == 1 ? FactoryLevelSession.RawCount : 20);
        gameObject.AddComponent<FactoryLevelLayout>().Initialize(player, playerCollider, prefabs, equipmentScale, rawSupply, production);
        Canvas guideCanvas = slotImages.Length > 0 && slotImages[0] != null ? slotImages[0].GetComponentInParent<Canvas>() : FindObjectOfType<Canvas>();
        if (guideCanvas != null) rawSupply.InitializeFeeding(guideCanvas);
        if (guideCanvas != null) production.Statistics.InitializeInterface(guideCanvas, rawSupply);
        if (guideCanvas != null) gameObject.AddComponent<FactoryTutorial>().Initialize(guideCanvas, MainMenuController.ConsumeTutorialRequest());
        if (level == 1 && guideCanvas != null)
            gameObject.AddComponent<FactoryLevelSession>().Initialize(guideCanvas, this, production, rawSupply);
    }

    private GameObject CreateEquipment(int index)
    {
        GameObject item = Instantiate(prefabs[index], Vector3.zero, Quaternion.identity);
        item.AddComponent<EquipmentGrid>().Initialize(equipmentScale);
        if (index < 3)
        {
            TMP_Text label = slotImages.Length > 0 && slotImages[0] != null
                ? slotImages[0].GetComponentInChildren<TMP_Text>() : null;
            item.AddComponent<FactoryPorts>().Initialize(index, label != null ? label.font : TMP_Settings.defaultFontAsset);
        }
        else if (index == 4) item.AddComponent<FactoryRail>().Measure();
        return item;
    }

    private void Awake()
    {
        cameraController = worldCamera.GetComponent<GameController>();
        production = gameObject.AddComponent<FactoryProduction>();
        production.armTravelMultiplier = armTravelMultiplier;
        production.armPickupMultiplier = armPickupMultiplier;
        production.armRotationMultiplier = armRotationMultiplier;

        // 为地面虚影建立半透明材质，不修改原模型材质。
        previewMaterial = new Material(Shader.Find("Standard"));
        previewMaterial.SetFloat("_Mode", 2f); // Fade
        previewMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        previewMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        previewMaterial.SetInt("_ZWrite", 0);
        previewMaterial.DisableKeyword("_ALPHATEST_ON");
        previewMaterial.EnableKeyword("_ALPHABLEND_ON");
        previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        previewMaterial.renderQueue = (int)RenderQueue.Transparent;

        CreateInteractionPanels();
        InitializeSlotLabels();
        RefreshSlotColors();
    }

    private void InitializeSlotLabels()
    {
        for (int i = 0; i < slotImages.Length && i < equipmentNames.Length; i++)
        {
            if (slotImages[i] == null) continue;
            var label = slotImages[i].GetComponentInChildren<TMP_Text>(true);
            if (label == null) continue;
            label.font = FactoryFonts.Ensure(label.font); label.color = FactoryGuideUI.Ink;
            label.text = (i + 1) + "\n" + equipmentNames[i];
            label.enableAutoSizing = true;
            label.fontSizeMin = 16;
            label.fontSizeMax = 26;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.rectTransform.offsetMin = new Vector2(6, 26);
            label.rectTransform.offsetMax = new Vector2(-6, -4);
            label.ForceMeshUpdate();

            var priceObject = new GameObject("Price", typeof(RectTransform), typeof(TextMeshProUGUI));
            priceObject.transform.SetParent(slotImages[i].transform, false);
            var price = priceObject.GetComponent<TextMeshProUGUI>();
            price.font = label.font;
            price.text = equipmentPrices[i].ToString();
            price.fontSize = 16;
            price.color = FactoryGuideUI.Muted;
            price.alignment = TextAlignmentOptions.Center;
            price.enableWordWrapping = false;
            price.raycastTarget = false;
            var priceRect = price.rectTransform;
            priceRect.anchorMin = Vector2.zero;
            priceRect.anchorMax = Vector2.right;
            priceRect.pivot = new Vector2(0.5f, 0f);
            priceRect.anchoredPosition = new Vector2(0, 3);
            priceRect.sizeDelta = new Vector2(-12, 26);
        }
    }

    private void LateUpdate()
    {
        if (FactoryTutorial.BlocksGameplay || FactoryLevelSession.BlocksInput || RawMaterialSupply.BlocksGameplay || FactoryStatistics.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay) return;
        bool birdMode = cameraController != null
            ? cameraController.IsBirdMode
            : worldCamera.orthographic;

        if (birdMode != wasBirdMode)
        {
            pressedItem = null;
            dragging = false;
            supplyPressed = supplyDragging = false;
            if (!birdMode)
                inspectedItem = null;
            wasBirdMode = birdMode;
        }

        for (int i = 0; i < numberKeys.Length; i++)
        {
            if (Input.GetKeyDown(numberKeys[i]) || Input.GetKeyDown(keypadKeys[i]))
                SelectItem(i);
        }

        if (!birdMode && rawSupply.Held == null && carriedProduct == null &&
            Input.GetMouseButtonDown(0) && !IsPointerOverInterface())
        {
            var ray = worldCamera.ScreenPointToRay(Cursor.lockState == CursorLockMode.Locked
                ? new Vector3(worldCamera.pixelWidth * .5f, worldCamera.pixelHeight * .5f) : Input.mousePosition);
            if (rawSupply.HitsStock(ray))
            {
                CancelSelection(); pressedItem = null; dragging = false;
                rawSupply.InspectStock(); UpdateInteractionPanels(birdMode); return;
            }
        }

        rawSupply.UpdateHeld(worldCamera, birdMode);
        if (Input.GetKeyDown(KeyCode.F) && !IsPointerOverInterface())
        {
            var target = inspectedItem != null ? inspectedItem : PickPlacedEquipment();
            var station = target != null ? target.GetComponent<FactoryStation>() : null;
            if (rawSupply.Held != null) production.Message("手持原料板：鼠标左键放入冲床输入口或附近空地");
            else if (carriedProduct != null) PlaceFinishedProduct(birdMode);
            else if (rawSupply.NearStock) { CancelSelection(); rawSupply.InspectStock(); }
            else if (rawSupply.TryPickUp()) CancelSelection();
            else if (!TryPickUpFinishedProduct(FindNearbyFinishedStation(station)))
                production.Message("请靠近传送带成品按 F 拾取，或靠近原料堆拾取原料");
        }
        if (carriedProduct != null)
        {
            if (Input.GetMouseButtonDown(0) && !IsPointerOverInterface()) TryStoreCarriedProduct();
            UpdateInteractionPanels(birdMode);
            return;
        }
        if (birdMode && rawSupply.Held == null && carriedProduct == null)
        {
            bool overInterface = IsPointerOverInterface();
            Vector3 point = Vector3.zero;
            bool hasPoint = !overInterface && TryGetMouseGroundPoint(out point);
            if (HandleSupplyPointer(worldCamera.ScreenPointToRay(Input.mousePosition), point, hasPoint,
                Input.GetMouseButtonDown(0) && !overInterface, Input.GetMouseButton(0), Input.GetMouseButtonUp(0), Input.GetMouseButtonDown(1)))
            { UpdateInteractionPanels(birdMode); return; }
        }
        if (rawSupply.Held != null)
        {
            supplyPressed = supplyDragging = false;
            if (Input.GetMouseButtonDown(1))
            { rawSupply.CancelPickUp(); UpdateInteractionPanels(birdMode); return; }
            if (Input.GetMouseButtonDown(0) && !IsPointerOverInterface())
            {
                Ray feedRay = worldCamera.ScreenPointToRay(birdMode ? Input.mousePosition
                    : new Vector3(worldCamera.pixelWidth * .5f, worldCamera.pixelHeight * .5f));
                if (!FeedHeldFromRay(feedRay, birdMode))
                {
                    Vector3 point;
                    if (birdMode)
                    {
                        if (TryGetMouseGroundPoint(out point)) rawSupply.TryDrop(point, placed);
                    }
                    else
                    {
                        Vector3 forward = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up).normalized;
                        point = player.position + forward * (playerCollider.radius + equipmentScale * 0.8f + 0.5f);
                        point.y = 0;
                        rawSupply.TryDrop(point, placed);
                    }
                }
            }
            UpdateInteractionPanels(birdMode);
            return;
        }

        if (headPreview != null)
            headPreview.SetActive(!birdMode);

        // 先旋转预览，再吸附、检查占地和处理点击，同帧放置也使用新朝向。
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (selected >= 0 && selected != 3 && groundPreview != null)
            {
                groundPreview.transform.Rotate(Vector3.up, 90f, Space.World);
                if (headPreview != null && headPreview.activeInHierarchy)
                    headPreview.transform.RotateAround(
                        GetVisualBounds(headPreview).center, Vector3.up, 90f);
            }
            else
                RotateInspectedEquipment();
        }

        if (groundPreview != null)
        {
            Vector3 target = Vector3.zero;
            bool hasTarget = birdMode
                ? !IsPointerOverInterface() && TryGetMouseGroundPoint(out target)
                : true;
            if (!birdMode)
                target = GetTargetPosition(false);
            else if (!hasTarget)
                target = Vector3.zero;

            groundPreview.SetActive(hasTarget);
            if (hasTarget)
                AlignVisualToGround(groundPreview, target);

            canPlace = hasTarget && CanPlaceHere(groundPreview, null);
            if (selected == 3 && hasTarget)
                production.Message(canPlace ? "左键放置机械臂" : placementBlockReason);
            previewMaterial.color = canPlace
                ? new Color(0.1f, 1f, 0.2f, 0.4f)
                : new Color(1f, 0.15f, 0.15f, 0.4f);
        }

        if (Input.GetKeyDown(KeyCode.B))
            TakeBackNearest();

        if (birdMode)
            HandleBirdInput();
        else
            HandleThirdPersonInput();

        UpdateInteractionPanels(birdMode);
    }

    private void HandleThirdPersonInput()
    {
        if (Input.GetMouseButtonDown(1))
        {
            if (selected >= 0)
                CancelSelection();
            else
                inspectedItem = null;
        }

        if (Input.GetMouseButtonDown(0) &&
            Cursor.lockState == CursorLockMode.Locked)
        {
            if (selected >= 0)
            {
                var ray = worldCamera.ScreenPointToRay(new Vector3(worldCamera.pixelWidth * .5f, worldCamera.pixelHeight * .5f));
                var clicked = PickPlacedEquipment(ray);
                if (clicked != null && production.Statistics.Find(clicked) != null)
                    OpenEquipmentFromRay(ray);
                else if (canPlace)
                    PlaceItem();
            }
            else
            {
                var ray = worldCamera.ScreenPointToRay(new Vector3(worldCamera.pixelWidth * .5f, worldCamera.pixelHeight * .5f));
                inspectedItem = PickPlacedEquipment(ray);
                if (inspectedItem != null && production.Statistics.Find(inspectedItem) != null)
                    OpenEquipmentFromRay(ray);
            }
        }
    }

    // The pile is informational; feeding starts directly from a punch click.
    internal bool HandleSupplyPointer(Ray ray, Vector3 point, bool hasPoint, bool pressed, bool held, bool released, bool cancel)
    {
        if (cancel && supplyPressed) { supplyPressed = supplyDragging = false; return true; }
        if (pressed && hasPoint && rawSupply.HitsStock(ray))
        {
            CancelSelection(); pressedItem = null; dragging = false; inspectedItem = null;
            supplyPressed = true; supplyDragging = false; supplyPressedAt = Time.unscaledTime;
            supplyPressPoint = point; supplyDragOffset = rawSupply.transform.position - point;
            return true;
        }
        if (!supplyPressed) return false;
        if (held && hasPoint && (supplyDragging || Time.unscaledTime - supplyPressedAt >= dragHoldTime ||
            Vector3.Distance(point, supplyPressPoint) > .2f))
        {
            supplyDragging = true;
            if (rawSupply.IsFixed)
                production.Message("原料堆固定；直接点击冲床选择上料数量");
            else if (rawSupply.TryMove(point + supplyDragOffset, placed, production.FinishedArea))
                production.Message("正在移动原料堆，松开左键完成");
            else production.Message("原料堆不能与角色、设备或存放区域重叠");
        }
        if (released)
        {
            if (!supplyDragging && hasPoint && rawSupply.HitsStock(ray)) rawSupply.InspectStock();
            else if (supplyDragging) production.Message("原料堆位置固定；直接点击冲床上料");
            supplyPressed = supplyDragging = false;
        }
        return true;
    }

    private void HandleBirdInput()
    {
        // 选模后进入放置操作，避免点击被已有设备的查看、拖动或撤回抢占。
        if (selected >= 0)
        {
            pressedItem = null;
            dragging = false;

            if (IsPointerOverInterface())
                return;

            if (Input.GetMouseButtonDown(1))
                CancelSelection();
            else if (Input.GetMouseButtonDown(0))
            {
                var ray = worldCamera.ScreenPointToRay(Input.mousePosition);
                var clicked = PickPlacedEquipment(ray);
                if (clicked != null && production.Statistics.Find(clicked) != null)
                    OpenEquipmentFromRay(ray);
                else if (canPlace) PlaceItem();
            }

            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            GameObject target = IsPointerOverInterface()
                ? null : PickPlacedEquipment();

            if (target != null)
                RemovePlaced(target);
            else if (selected >= 0 && !IsPointerOverInterface())
                CancelSelection();
        }

        if (Input.GetMouseButtonDown(0) && !IsPointerOverInterface())
        {
            GameObject target = PickPlacedEquipment();
            inspectedItem = target;
            rotateBlockedUntil = 0f;

            if (target != null)
            {
                pressedItem = target;
                pressedAt = Time.unscaledTime;
                dragging = false;

                if (TryGetMouseGroundPoint(out Vector3 point))
                {
                    Vector3 center = target.GetComponent<EquipmentGrid>().Footprint.center;
                    dragOffset = new Vector3(
                        center.x - point.x, 0f, center.z - point.z);
                }
                else
                    dragOffset = Vector3.zero;
            }
        }

        if (pressedItem != null && Input.GetMouseButton(0) &&
            Time.unscaledTime - pressedAt >= dragHoldTime)
        {
            if (!production.CanEdit(pressedItem)) return;
            // An arm is seated on its rail. Reposition it by withdrawing and placing again.
            if (pressedItem.GetComponent<FactoryArm>() != null) return;
            dragging = true;
            if (TryGetMouseGroundPoint(out Vector3 point))
            {
                Vector3 oldPosition = pressedItem.transform.position;
                AlignVisualToGround(pressedItem, point + dragOffset);

                if (!CanPlaceHere(pressedItem, pressedItem))
                    pressedItem.transform.position = oldPosition;
                else production.Rebuild();
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            bool equipmentClick = pressedItem != null && !dragging && !IsPointerOverInterface() &&
                PickPlacedEquipment() == pressedItem && production.Statistics.Find(pressedItem) != null;
            pressedItem = null;
            dragging = false;
            if (equipmentClick) OpenEquipmentFromRay(worldCamera.ScreenPointToRay(Input.mousePosition));
        }
    }

    private bool IsPointerOverInterface()
    {
        return RawMaterialSupply.BlocksGameplay || FactoryStatistics.BlocksGameplay || FactoryPlanLibrary.BlocksGameplay || FactoryLocalAssistant.BlocksGameplay || FactoryLocalAssistant.PointerOverButton(Input.mousePosition) || FactoryPlanLibrary.PointerOverButton(Input.mousePosition) || FactoryStatistics.PointerOverStatisticsButton(Input.mousePosition) || FactoryLevelSession.PointerOverHUD(Input.mousePosition) ||
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool TryGetMouseGroundPoint(out Vector3 point)
    {
        if (!worldCamera.pixelRect.Contains(Input.mousePosition))
        {
            point = Vector3.zero;
            return false;
        }

        Ray ray = worldCamera.ScreenPointToRay(Input.mousePosition);
        Plane floor = new Plane(Vector3.up, Vector3.zero);

        if (floor.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    private GameObject PickPlacedEquipment()
    {
        return PickPlacedEquipment(worldCamera.ScreenPointToRay(Input.mousePosition));
    }
    private GameObject PickPlacedEquipment(Ray ray)
    {
        GameObject nearest = null;
        float nearestHit = float.PositiveInfinity;

        foreach (GameObject item in placed)
        {
            if (item == null)
                continue;

            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>())
            {
                if (renderer.enabled && renderer.GetComponentInParent<FactoryVisual>() == null &&
                    renderer.bounds.IntersectRay(ray, out float hitDistance) &&
                    hitDistance < nearestHit)
                {
                    nearestHit = hitDistance;
                    nearest = item;
                }
            }
        }

        return nearest;
    }

    private bool CanRotateEquipment(GameObject item)
    {
        if (item == null || !placedTypes.TryGetValue(item, out int index))
            return false;

        // 顺序为冲床、折弯、焊接、机械臂、滑槽；机械臂不参与旋转。
        return index == 0 || index == 1 || index == 2 || index == 4;
    }

    private void RotateInspectedEquipment()
    {
        if (dragging || !CanRotateEquipment(inspectedItem))
            return;

        GameObject item = inspectedItem;
        if (!production.CanEdit(item)) return;
        Vector3 oldPosition = item.transform.position;
        Quaternion oldRotation = item.transform.rotation;
        Vector3 visualCenter = item.GetComponent<EquipmentGrid>().Footprint.center;

        // 绕可见模型的中心旋转，避免预制体根节点偏移造成原地打圈。
        item.transform.RotateAround(visualCenter, Vector3.up, 90f);
        item.GetComponent<EquipmentGrid>().Snap(visualCenter);

        if (!CanPlaceHere(item, item))
        {
            item.transform.SetPositionAndRotation(oldPosition, oldRotation);
            rotateBlockedUntil = Time.unscaledTime + 1.5f;
        }
        else
        {
            rotateBlockedUntil = 0f;
            production.Rebuild();
        }
    }

    private void SelectItem(int index)
    {
        if (carriedProduct != null)
        { production.Message("手持成品：靠近最终产物区后点击左键放置"); return; }
        if (rawSupply != null && rawSupply.Held != null)
        { production.Message("请先用鼠标左键放下手中的原料板"); return; }
        if (index >= prefabs.Length || prefabs[index] == null ||
            index == selected)
            return;

        if (headPreview != null)
            Destroy(headPreview);

        if (groundPreview != null)
            Destroy(groundPreview);

        selected = index;
        canPlace = false;
        pressedItem = null;
        dragging = false;
        inspectedItem = null;
        rotateBlockedUntil = 0f;

        // 头顶显示正常材质的缩小模型。
        headPreview = Instantiate(prefabs[index], headAnchor);
        headPreview.transform.localPosition = Vector3.zero;
        headPreview.transform.localRotation = Quaternion.identity;
        DisablePreviewComponents(headPreview);

        Bounds headBounds = GetVisualBounds(headPreview);
        float longestSide = Mathf.Max(
            headBounds.size.x, headBounds.size.y, headBounds.size.z
        );

        headPreview.transform.localScale *=
            0.9f / Mathf.Max(0.01f, longestSide);

        headBounds = GetVisualBounds(headPreview);
        headPreview.transform.position +=
            headAnchor.position - headBounds.center;

        // 地面显示绿色或红色的半透明模型。
        groundPreview = CreateEquipment(index);
        DisablePreviewComponents(groundPreview);
        ApplyPreviewMaterial(groundPreview);

        RefreshSlotColors();
    }

    private void CancelSelection()
    {
        if (headPreview != null)
        {
            Destroy(headPreview);
            headPreview = null;
        }

        if (groundPreview != null)
        {
            Destroy(groundPreview);
            groundPreview = null;
        }

        selected = -1;
        canPlace = false;
        RefreshSlotColors();
    }

    private Vector3 GetTargetPosition(bool birdMode)
    {
        if (birdMode)
        {
            // 鸟瞰模式的虚影跟随鼠标。
            if (TryGetMouseGroundPoint(out Vector3 point))
                return point;

            Vector3 cameraPosition = worldCamera.transform.position;
            return new Vector3(cameraPosition.x, 0f, cameraPosition.z);
        }

        // 第三人称：奶蛋前方。大型机器会自动放远一些。
        Vector3 forward = Vector3.ProjectOnPlane(
            worldCamera.transform.forward, Vector3.up
        ).normalized;

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        Bounds bounds = GetVisualBounds(groundPreview);
        float machineRadius = Mathf.Max(
            bounds.extents.x, bounds.extents.z
        );

        float distance = Mathf.Max(
            minimumPlaceDistance,
            machineRadius + playerCollider.radius + 1f
        );

        return player.position + forward * distance;
    }

    private void AlignVisualToGround(GameObject obj, Vector3 target)
    {
        EquipmentGrid grid = obj.GetComponent<EquipmentGrid>();
        if (grid != null)
        {
            grid.Snap(target);
            if (obj == groundPreview && selected == 3 && production.FindRailAt(target, null, out FactoryRail rail))
                FactoryArm.AlignToRail(obj.transform, rail, target);
            return;
        }

        obj.transform.position = new Vector3(target.x, 0f, target.z);

        Bounds bounds = GetVisualBounds(obj);

        // 使可见模型的底部位于地板 Y=0，并使模型中心对准目标。
        obj.transform.position += new Vector3(
            target.x - bounds.center.x,
            -bounds.min.y,
            target.z - bounds.center.z
        );
    }

    private bool CanPlaceHere(GameObject candidateObject, GameObject ignoredItem)
    {
        placementBlockReason = "此处不能放置机械臂";
        int kind = selected;
        if (ignoredItem != null && placedTypes.TryGetValue(ignoredItem, out int placedKind)) kind = placedKind;
        if (kind == 3 && !production.FindRail(candidateObject, ignoredItem, out _))
            return RejectPlacement(production.FindRailAt(FactoryArm.BaseCenter(candidateObject.transform), ignoredItem, out _, true)
                ? "这条连通滑槽已有机械臂，请选择独立滑槽" : "请将机械臂底座放在滑槽上");
        if (kind == 4 && !production.ValidRailChange(candidateObject)) return false;
        Bounds candidate = candidateObject.GetComponent<EquipmentGrid>().Footprint;
        Bounds visual = GetVisualBounds(candidateObject);
        candidate = new Bounds(new Vector3(candidate.center.x, visual.center.y, candidate.center.z),
            new Vector3(candidate.size.x - 0.002f, visual.size.y, candidate.size.z - 0.002f));

        if (candidate.Intersects(playerCollider.bounds))
            return RejectPlacement("此处与角色重叠，请选择滑槽上的空位");
        if (rawSupply != null && rawSupply.Overlaps(candidate)) return RejectPlacement("此处已有原料板，请选择滑槽上的空位");
        if (production.FinishedArea != null)
        {
            if (kind < 3 && FootprintsOverlap(candidate, production.FinishedArea.Footprint)) return false;
            if (kind >= 3 && FactoryProduction.HardwareOverlap(candidateObject, production.FinishedArea.gameObject))
                return RejectPlacement("存放台实体阻挡，请选择通道上的滑槽");
        }

        foreach (GameObject item in placed)
        {
            if (item != null && placedTypes.TryGetValue(item, out int otherKind) &&
                ((kind == 3 && otherKind == 4) || (kind == 4 && otherKind == 3))) continue;
            if (item != null && item != ignoredItem && placedTypes.TryGetValue(item, out otherKind) &&
                ((kind >= 3 && otherKind < 3) || (kind < 3 && otherKind >= 3)))
            {
                // Demo1's rail runs through empty service space on the large
                // decorative floor tile. Check actual hardware, not that floor.
                if (FactoryProduction.HardwareOverlap(candidateObject, item))
                    return RejectPlacement("与" + GetEquipmentName(item) + "实体重叠，请选择滑槽上的空位");
                continue;
            }
            if (item != null && item != ignoredItem &&
                FootprintsOverlap(candidate, item.GetComponent<EquipmentGrid>().Footprint))
                return RejectPlacement("此处已有设备，请选择滑槽上的空位");
        }

        return true;
    }

    private bool RejectPlacement(string reason) { placementBlockReason = reason; return false; }

    private static bool FootprintsOverlap(Bounds a, Bounds b)
    {
        return a.min.x < b.max.x && a.max.x > b.min.x &&
            a.min.z < b.max.z && a.max.z > b.min.z;
    }

    private void PlaceItem()
    {
        GameObject item = CreateEquipment(selected);
        item.transform.SetPositionAndRotation(groundPreview.transform.position,
            groundPreview.transform.rotation);
        item.name = "Placed_" + prefabs[selected].name;
        placed.Add(item);
        placedTypes[item] = selected;
        production.Register(item, selected);
        RefreshCostDisplay();
    }

    private void TakeBackNearest()
    {
        RemovePlaced(FindNearestEquipment());
    }

    private GameObject FindNearestEquipment()
    {
        GameObject nearest = null;
        float nearestDistance = takeBackDistance;

        foreach (GameObject item in placed)
        {
            if (item == null)
                continue;

            Vector3 closest = GetVisualBounds(item).ClosestPoint(
                player.position
            );

            // “靠近”按地面上的水平距离计算。
            closest.y = player.position.y;
            float distance = Vector3.Distance(player.position, closest);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = item;
            }
        }

        return nearest;
    }

    private void RemovePlaced(GameObject item)
    {
        if (item == null || !placedTypes.ContainsKey(item))
            return;
        if (FactoryLevelSession.Active != null && (FactoryLevelSession.Active.IsRunning || FactoryLevelSession.Active.IsEnded))
        { production.Message("生产开始后至结算前不能撤回已放置设备"); return; }
        if (!production.CanEdit(item)) return;

        int returnedRaw = rawSupply.RestoreQueuedRaw(item.GetComponent<FactoryStation>());

        if (inspectedItem == item)
            inspectedItem = null;

        if (pressedItem == item)
        {
            pressedItem = null;
            dragging = false;
        }

        placed.Remove(item);
        placedTypes.Remove(item);
        production.Statistics.Withdraw(item);
        Destroy(item);
        RefreshCostDisplay();
        if (returnedRaw > 0) production.Message("冲床已撤回，" + returnedRaw + " 块待加工原料已归还原料堆；剩余原料 " + rawSupply.Remaining + " 块");
    }

    private void CreateInteractionPanels()
    {
        Canvas canvas = slotImages.Length > 0 && slotImages[0] != null
            ? slotImages[0].GetComponentInParent<Canvas>()
            : FindObjectOfType<Canvas>();

        if (canvas == null)
            return;

        TMP_Text slotLabel = slotImages.Length > 0 && slotImages[0] != null
            ? slotImages[0].GetComponentInChildren<TMP_Text>()
            : null;
        TMP_FontAsset font = slotLabel != null
            ? slotLabel.font : TMP_Settings.defaultFontAsset;
        font = FactoryFonts.Ensure(font);
        gameObject.AddComponent<FactoryPlaybackControl>().Initialize(canvas, font);

        nearbyPanel = CreatePanel(
            canvas.transform, "TakeBackHint",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 132f), new Vector2(560f, 54f),
            font, 25f, out nearbyText);

        infoPanel = CreatePanel(
            canvas.transform, "EquipmentInfo",
            Vector2.one, Vector2.one,
            new Vector2(-24f, -112f), new Vector2(440f, 250f),
            font, 22f, out infoText);

        nearbyPanel.SetActive(false);
        infoPanel.SetActive(false);
        CreatePanel(canvas.transform, "ProductionStatus", new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(850, 108),
            font, 21, out productionText);
        CreatePanel(canvas.transform, "EquipmentCost", Vector2.up, Vector2.up,
            new Vector2(24, -16), new Vector2(360, 90), font, 23, out costText);
        RefreshCostDisplay();
    }

    private GameObject CreatePanel(
        Transform parent, string panelName,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
        TMP_FontAsset font, float fontSize, out TextMeshProUGUI label)
    {
        GameObject panel = new GameObject(
            panelName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image background = panel.GetComponent<Image>();
        background.color = FactoryGuideUI.Background;
        background.raycastTarget = false;

        GameObject textObject = new GameObject(
            "Label", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panel.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 8f);
        textRect.offsetMax = new Vector2(-12f, -8f);

        label = textObject.GetComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = fontSize; label.color = FactoryGuideUI.Ink;
        label.color = FactoryGuideUI.Ink;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.raycastTarget = false;

        return panel;
    }

    private void UpdateInteractionPanels(bool birdMode)
    {
        RefreshCostDisplay();
        if (productionText != null)
            productionText.text = "产线：原料 → 冲压 → 折弯 → 焊接 → 成品（" + production.Finished + "）  已入库：" + production.Stored + "  原料：" +
                (rawSupply != null ? rawSupply.Remaining : 0) + "  收益：" +
                (production.FinishedArea != null ? production.FinishedArea.Revenue.ToString("N0") : "0") + " 元\n" + production.Hint;
        if (nearbyPanel != null)
        {
            GameObject nearest = FindNearestEquipment();
            nearbyPanel.SetActive(nearest != null && (FactoryLevelSession.Active == null || !FactoryLevelSession.Active.IsRunning));
            if (nearest != null)
                nearbyText.text = FactoryLevelSession.Active != null && FactoryLevelSession.Active.IsRunning
                    ? "" : "B 撤回 · " + GetEquipmentName(nearest);
            if (rawSupply != null && rawSupply.NearStock && rawSupply.Held == null && carriedProduct == null)
            { nearbyPanel.SetActive(false); }
            if (carriedProduct != null)
            {
                nearbyPanel.SetActive(true);
                nearbyText.text = production.FinishedArea != null && production.FinishedArea.IsNear(player.position, takeBackDistance)
                    ? "点击左键放入最终产物区 · 每件收益 " + FinishedGoodsArea.PricePerProduct + " 元" : "手持成品：靠近最终产物区后点击左键放置";
            }
            else if (FindNearbyFinishedStation(null) != null)
            { nearbyPanel.SetActive(true); nearbyText.text = "传送带成品 · 按 F 拾取"; }
        }

        if (infoPanel != null)
        {
            bool showInfo = inspectedItem != null && (dragging || Time.unscaledTime < rotateBlockedUntil);
            infoPanel.SetActive(showInfo);

            if (showInfo)
            {
                Vector3 center = GetVisualBounds(inspectedItem).center;
                string operation;

                if (dragging)
                    operation = "\n正在移动设备";
                else if (Time.unscaledTime < rotateBlockedUntil)
                    operation = "\n位置不足，无法转动";
                else if (birdMode)
                    operation = "\n左击查看，按住可移动" +
                        (CanRotateEquipment(inspectedItem)
                            ? "\n按 R 转 90 度" : "") +
                        (FactoryLevelSession.Active != null && FactoryLevelSession.Active.IsRunning ? "\n生产期间不能撤回" : "\n右击撤回");
                else
                    operation = CanRotateEquipment(inspectedItem)
                        ? "\n按 R 转 90 度" : "";

                infoText.text = GetEquipmentName(inspectedItem) +
                    "  占地 " + inspectedItem.GetComponent<EquipmentGrid>().Cells.x +
                    "×" + inspectedItem.GetComponent<EquipmentGrid>().Cells.y + " 格" +
                    "\n位置  X：" + center.x.ToString("F1") +
                    "   Z：" + center.z.ToString("F1") +
                    operation;
                infoText.text = GetEquipmentName(inspectedItem) + (dragging ? " · 移动中" : " · 无法旋转");
                
                
            }
        }
    }

    private void RefreshCostDisplay()
    {
        if (costText == null) return;
        long total = CurrentCost;
        costText.text = "当前成本：" + total.ToString("N0") + " 元";
        if (selected >= 0 && selected < equipmentPrices.Length)
            costText.text += "\n<size=18>再放一件 +" + equipmentPrices[selected].ToString("N0") +
                " 元 · 合计 " + (total + equipmentPrices[selected]).ToString("N0") + " 元</size>";
    }

    private string GetEquipmentName(GameObject item)
    {
        var module = production.Statistics.Find(item);
        if (module != null) return module.Name;
        if (placedTypes.TryGetValue(item, out int index) &&
            index >= 0 && index < equipmentNames.Length)
            return equipmentNames[index];

        return item.name.Replace("Placed_", "")
            .Replace("Placeables_", "").Replace("Placeable_", "");
    }

    internal bool OpenEquipmentFromRay(Ray ray)
    {
        var clicked = PickPlacedEquipment(ray);
        if (!production.Statistics.OpenDetails(clicked)) return false;
        inspectedItem = clicked; CancelSelection(); pressedItem = null; dragging = false;
        return true;
    }

    internal bool OpenFeedFromRay(Ray ray)
    {
        var clicked = PickPlacedEquipment(ray);
        var target = clicked != null ? clicked.GetComponent<FactoryStation>() : PickRawInput(ray);
        if (target == null || target.Kind != 0)
        { production.Message("请点击需要上料的冲床区域，右键取消"); return false; }
        if (!rawSupply.OpenFeedDialog(target)) return false;
        CancelSelection(); pressedItem = null; dragging = false;
        return true;
    }

    internal bool FeedHeldFromRay(Ray ray, bool birdMode)
    {
        GameObject clicked = birdMode ? PickPlacedEquipment(ray) : null;
        FactoryStation target = clicked != null ? clicked.GetComponent<FactoryStation>() : PickRawInput(ray);
        if (target == null || target.Kind != 0) return false;
        rawSupply.TryFeed(target, birdMode); return true;
    }

    private FactoryStation PickRawInput(Ray ray)
    {
        FactoryStation best = null; float distance = float.PositiveInfinity;
        foreach (var station in production.Stations)
        {
            if (station == null || station.Kind != 0) continue;
            Vector3 port = station.Ports.InputPort.position;
            Plane plane = new Plane(Vector3.up, port);
            if (plane.Raycast(ray, out float t) && t < distance &&
                Vector3.Distance(ray.GetPoint(t), port) < station.Ports.PartWidth * 1.7f)
            { best = station; distance = t; }
        }
        if (best == null)
        {
            GameObject item = PickPlacedEquipment(ray);
            if (item != null) best = item.GetComponent<FactoryStation>();
        }
        return best;
    }

    private FactoryStation FindNearbyFinishedStation(FactoryStation preferred)
    {
        if (preferred != null && preferred.Kind == 2 && IsNearFinishedProduct(preferred)) return preferred;
        foreach (var station in production.Stations)
            if (station != null && station.Kind == 2 && IsNearFinishedProduct(station)) return station;
        return null;
    }

    private bool IsNearFinishedProduct(FactoryStation station)
    {
        if (station.Output == null) return false;
        Vector3 closest = FactoryWorkpiece.GeometryBounds(station.Output.transform).ClosestPoint(player.position);
        closest.y = player.position.y;
        return Vector3.Distance(closest, player.position) <= takeBackDistance;
    }

    internal bool TryPickUpFinishedProduct(FactoryStation station)
    {
        if (carriedProduct != null || station == null || station.Kind != 2 || !IsNearFinishedProduct(station)) return false;
        if (station.ReservedBy != null || station.OutputChanging)
        { production.Message("机械臂正在交接或传送带正在补位，请稍候"); return true; }
        var part = station.TakeOutput();
        if (part == null) return false;
        CancelSelection();
        carriedProduct = part;
        part.SetGroundCollision(false);
        productPlacementRotation = part.transform.rotation;
        part.transform.SetParent(worldCamera.transform, true);
        part.transform.localPosition = new Vector3(.4f, -.45f, 1.2f);
        part.transform.localRotation = Quaternion.identity;
        production.Message("已拾取成品；靠近最终产物区后点击左键放置，每件收益 " + FinishedGoodsArea.PricePerProduct + " 元");
        return true;
    }

    internal bool TryStoreCarriedProduct()
    {
        if (carriedProduct == null) return false;
        var destination = production.FinishedArea;
        if (destination == null || !destination.IsNear(player.position, takeBackDistance))
        { production.Message("请先靠近最终产物区，再点击左键放置成品"); return false; }
        Quaternion handRotation = carriedProduct.transform.rotation;
        carriedProduct.transform.rotation = productPlacementRotation;
        if (!destination.BeginManualDrop(carriedProduct))
        {
            carriedProduct.transform.rotation = handRotation;
            production.Message("最终产物区已满，请保留手中成品"); return false;
        }
        carriedProduct = null;
        production.Message("成品正在落入最终产物区，落定后获得 " + FinishedGoodsArea.PricePerProduct + " 元");
        return true;
    }

    private void PlaceFinishedProduct(bool birdMode)
    {
        Vector3 target;
        if (birdMode)
        {
            if (!TryGetMouseGroundPoint(out target)) return;
        }
        else
        {
            Vector3 forward = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up).normalized;
            target = player.position + forward * 3;
        }
        Quaternion carryingRotation = carriedProduct.transform.rotation;
        carriedProduct.transform.rotation = productPlacementRotation;
        Bounds geometry = FactoryWorkpiece.GeometryBounds(carriedProduct.transform);
        Bounds area = new Bounds(target, new Vector3(geometry.size.x, 0.1f, geometry.size.z));
        if (rawSupply != null && rawSupply.Overlaps(area))
        { carriedProduct.transform.rotation = carryingRotation; production.Message("放置区域已有原料板，请选择另一处空地"); return; }
        foreach (var item in placed)
            if (FootprintsOverlap(area, item.GetComponent<EquipmentGrid>().Footprint))
            { carriedProduct.transform.rotation = carryingRotation; production.Message("放置区域被设备占用，请看向空地再按 F"); return; }
        foreach (Transform product in production.Products)
            if (FootprintsOverlap(area, FactoryWorkpiece.GeometryBounds(product)))
            { carriedProduct.transform.rotation = carryingRotation; production.Message("放置区域已有成品，请选择另一处空地"); return; }
        carriedProduct.transform.SetParent(production.Products, true);
        geometry = FactoryWorkpiece.GeometryBounds(carriedProduct.transform);
        carriedProduct.transform.position += target - new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
        geometry = FactoryWorkpiece.GeometryBounds(carriedProduct.transform);
        var solid = carriedProduct.GetComponent<BoxCollider>();
        if (solid == null) solid = carriedProduct.gameObject.AddComponent<BoxCollider>();
        Bounds localGeometry = new Bounds(carriedProduct.transform.InverseTransformPoint(geometry.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            localGeometry.Encapsulate(carriedProduct.transform.InverseTransformPoint(geometry.center + Vector3.Scale(geometry.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
        solid.center = localGeometry.center;
        solid.size = localGeometry.size;
        solid.enabled = true;
        carriedProduct = null; production.Message("成品已放置；可继续在冲床输入口放入原料");
    }

    private Bounds GetVisualBounds(GameObject obj)
    {
        return EquipmentGrid.VisualBounds(obj);
    }

    private void DisablePreviewComponents(GameObject obj)
    {
        foreach (Collider collider
                 in obj.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        foreach (Animator animator
                 in obj.GetComponentsInChildren<Animator>())
            animator.enabled = false;

        foreach (Camera camera
                 in obj.GetComponentsInChildren<Camera>())
            camera.enabled = false;

        foreach (Light light
                 in obj.GetComponentsInChildren<Light>())
            light.enabled = false;

        foreach (Renderer renderer
                 in obj.GetComponentsInChildren<Renderer>())
            renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void ApplyPreviewMaterial(GameObject obj)
    {
        foreach (Renderer renderer
                 in obj.GetComponentsInChildren<Renderer>())
        {
            if (renderer.GetComponentInParent<FactoryVisual>() != null) continue;
            Material[] materials =
                new Material[renderer.sharedMaterials.Length];

            for (int i = 0; i < materials.Length; i++)
                materials[i] = previewMaterial;

            renderer.sharedMaterials = materials;
        }
    }

    private void RefreshSlotColors()
    {
        RefreshCostDisplay();
        for (int i = 0; i < slotImages.Length; i++)
        {
            if (slotImages[i] == null)
                continue;

            slotImages[i].color = i == selected
                ? FactoryGuideUI.Mint
                : FactoryGuideUI.PanelColor;
            var frame = slotImages[i].transform.parent.GetComponent<Image>();
            if (frame != null) frame.color = FactoryGuideUI.Background;
        }
    }

    private void OnDestroy()
    {
        if (rawSupply != null) Destroy(rawSupply.gameObject);
        if (previewMaterial != null)
            Destroy(previewMaterial);
    }
}

