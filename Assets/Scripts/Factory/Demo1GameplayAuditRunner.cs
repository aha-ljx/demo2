#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class Demo1GameplayAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    FactoryProduction system;
    readonly List<FactoryStation> stations = new List<FactoryStation>();
    GameObject probe;
    readonly Dictionary<Vector2Int, bool> blocked = new Dictionary<Vector2Int, bool>();
    readonly HashSet<Vector2Int> railCells = new HashSet<Vector2Int>();
    bool failed, finished, prebuffer, automaticStorage, turnAudit, omitConveyor, conveyorExtended;
    bool bufferAudit, bufferResumed;
    int expectedProducts = 3;
    float started, maxPoseError;
    int compared, maxBusy;
    FactoryArm arm;
    readonly HashSet<string> screenshots = new HashSet<string>();
    readonly HashSet<Demo1ReplaySession> weldedByCarrier = new HashSet<Demo1ReplaySession>();
    readonly HashSet<Demo1ReplaySession> placedByCarrier = new HashSet<Demo1ReplaySession>();
    readonly Dictionary<Demo1ReplaySession, Vector3> weldParking = new Dictionary<Demo1ReplaySession, Vector3>();
    float maximumWeldDrift, fastestRailSpeed;
    string maximumPoseContext;
    int planningPoseSamples;
    float maximumPlanningDrift;
    int stationaryWristChecksSkipped;
    readonly HashSet<Demo1ReplaySession> reusedFinishedPickupPlans = new HashSet<Demo1ReplaySession>();
    Vector3 previousTrackPosition;
    bool sampledTrack;
    int parkedFrames;
    readonly Dictionary<LoadingProcessController.LoadingState, int> stateFrames = new Dictionary<LoadingProcessController.LoadingState, int>();
    void OnEnable() { Application.logMessageReceived += Log; started = Time.realtimeSinceStartup; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Update() { if (!finished && (failed || Time.realtimeSinceStartup - started > 600)) Finish(false); }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); if (bufferAudit) Debug.Log("BUFFER WORKFLOW " + message); }
    Vector3 Point(Vector2Int cell) => new Vector3(cell.x * 3 + 1.5f, 0, cell.y * 3 + 1.5f);
    Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 3), Mathf.FloorToInt(p.z / 3));
    bool IsBlocked(Vector2Int cell)
    {
        if (Mathf.Abs(cell.x) > 35 || Mathf.Abs(cell.y) > 35) return true;
        probe.GetComponent<EquipmentGrid>().Snap(Point(cell));
        if (omitConveyor)
        {
            Bounds footprint = probe.GetComponent<EquipmentGrid>().Footprint;
            Vector3 dock = stations[2].Ports.ConveyorDockPosition;
            if (dock.x >= footprint.min.x - .02f && dock.x <= footprint.max.x + .02f &&
                dock.z >= footprint.min.z - .02f && dock.z <= footprint.max.z + .02f) return true;
        }
        if (blocked.TryGetValue(cell, out bool value)) return value;
        value = stations.Exists(s => FactoryProduction.HardwareOverlap(probe, s.gameObject)) ||
            (system.FinishedArea != null && FactoryProduction.HardwareOverlap(probe, system.FinishedArea.gameObject));
        blocked[cell] = value; return value;
    }
    List<Vector2Int> Route(Vector2Int from, Vector2Int to)
    {
        Check(!IsBlocked(from) && !IsBlocked(to), "rail terminals fit actual hardware at " + from + " / " + to);
        var open = new List<Vector2Int> { from }; var cost = new Dictionary<Vector2Int, int> { [from] = 0 };
        var previous = new Dictionary<Vector2Int, Vector2Int>();
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        for (int iteration = 0; open.Count > 0 && iteration < 5000; iteration++)
        {
            int best = 0;
            for (int i = 1; i < open.Count; i++)
                if (cost[open[i]] + Distance(open[i], to) < cost[open[best]] + Distance(open[best], to)) best = i;
            var cell = open[best]; open.RemoveAt(best);
            if (cell == to)
            {
                var route = new List<Vector2Int> { cell };
                while (previous.TryGetValue(cell, out var p)) { cell = p; route.Add(cell); }
                route.Reverse(); return route;
            }
            foreach (var direction in directions)
            {
                var next = cell + direction;
                if (IsBlocked(next)) continue;
                int nextCost = cost[cell] + 1;
                if (cost.TryGetValue(next, out int old) && old <= nextCost) continue;
                cost[next] = nextCost; previous[next] = cell;
                if (!open.Contains(next)) open.Add(next);
            }
        }
        throw new Exception("No legal rail route between real docking positions");
    }
    int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    void LateUpdate()
    {
        foreach (var station in stations)
            if (station.Mirror != null) ComparePose(station.Mirror, station.name + " " + station.Status);
        if (arm != null && arm.Mirror != null) ComparePose(arm.Mirror, "Arm " + arm.Status);
        maxBusy = Mathf.Max(maxBusy, stations.FindAll(s => s.Busy).Count);
        if (arm != null)
        {
            if (sampledTrack) fastestRailSpeed = Mathf.Max(fastestRailSpeed,
                // Rail routing is measured in XZ; floor/rail grounding adjusts
                // the vertical support height independently during handoffs.
                Vector3.ProjectOnPlane(arm.TrackPosition - previousTrackPosition, Vector3.up).magnitude / Time.deltaTime);
            previousTrackPosition = arm.TrackPosition; sampledTrack = true;
            foreach (var replay in FindObjectsOfType<Demo1ReplaySession>())
            {
                if (replay.Source != null)
                {
                    stationaryWristChecksSkipped = Mathf.Max(stationaryWristChecksSkipped, replay.Source.ReplaySkippedStationaryWristChecks);
                    if (replay.Source.ReplayFinishedPickupPlanUses == 3) reusedFinishedPickupPlans.Add(replay);
                }
                if (replay.Source == null || !replay.Source.ReplayUseCarrierForWeld) continue;
                if (replay.Source.ReplayWeldParked && arm.WaitingForWeld)
                {
                    if (!weldParking.ContainsKey(replay)) weldParking[replay] = arm.TrackPosition;
                    maximumWeldDrift = Mathf.Max(maximumWeldDrift, Vector3.Distance(weldParking[replay], arm.TrackPosition));
                    parkedFrames++;
                    CheckCarrier(arm.Busy && !arm.Holding && replay.Holder == arm && !replay.Source.ReplayArmReturning);
                }
                if (replay.Source.ReplayCheckpoint == "WeldProductLifted") weldedByCarrier.Add(replay);
                if (replay.Source.ReplayCheckpoint == "ConveyorPlaced") placedByCarrier.Add(replay);
            }
        }
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) CaptureAnimationFrames();
    }
    void ComparePose(Demo1MeshMirror mirror, string context)
    {
        float error = mirror.MaximumError(); compared++;
        if (mirror.IsPlanning) { planningPoseSamples++; maximumPlanningDrift = Mathf.Max(maximumPlanningDrift, error); }
        if (error <= maxPoseError) return;
        maxPoseError = error; maximumPoseContext = context + " planning=" + mirror.IsPlanning;
    }
    void CheckCarrier(bool ok) { if (!ok) throw new Exception("welding must reserve the same stationary carrier without holding the workpiece"); }
    void CaptureAnimationFrames()
    {
        foreach (var session in FindObjectsOfType<Demo1ReplaySession>())
        {
            if (!session.enabled || session.Source == null || session.Source.ReplayCheckpoint != null) continue;
            var state = session.Source.CurrentState;
            bool wanted = state == LoadingProcessController.LoadingState.MoveToTransferPoint ||
                state == LoadingProcessController.LoadingState.PunchClampAcquire ||
                state == LoadingProcessController.LoadingState.ArmSuctionPickup ||
                state == LoadingProcessController.LoadingState.CloseBendingDie ||
                state == LoadingProcessController.LoadingState.PlaceBentPartInHClamps ||
                state == LoadingProcessController.LoadingState.WeldingSeam ||
                state == LoadingProcessController.LoadingState.TransferArmPickup ||
                state == LoadingProcessController.LoadingState.ConveyorRunning;
            if (!wanted || screenshots.Contains(state.ToString())) continue;
            stateFrames.TryGetValue(state, out int frames); stateFrames[state] = frames + 1;
            if (frames < 12) continue;
            int kind = state <= LoadingProcessController.LoadingState.PunchClampAcquire ? 0 :
                state == LoadingProcessController.LoadingState.CloseBendingDie ? 1 :
                state == LoadingProcessController.LoadingState.ArmSuctionPickup ? 0 : 2;
            Bounds bounds = EquipmentGrid.VisualBounds(stations[kind].gameObject);
            if (arm != null && (state == LoadingProcessController.LoadingState.CloseBendingDie || state == LoadingProcessController.LoadingState.ArmSuctionPickup || state == LoadingProcessController.LoadingState.PlaceBentPartInHClamps)) bounds.Encapsulate(EquipmentGrid.VisualBounds(arm.gameObject));
            bounds.Encapsulate(FactoryWorkpiece.GeometryBounds(session.transform));
            Snapshot(bounds, state.ToString()); screenshots.Add(state.ToString());
        }
    }
    void Snapshot(Bounds bounds, string name)
    {
        var cameraObject = new GameObject("Audit Camera"); var camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        float radius = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        camera.transform.position = bounds.center + new Vector3(0.9f, 0.75f, 1.2f) * radius;
        camera.transform.LookAt(bounds.center); camera.nearClipPlane = 0.05f; camera.farClipPlane = 300;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
        var lightObject = new GameObject("Audit Light"); var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 0.95f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var rt = new RenderTexture(1440, 1000, 24); camera.targetTexture = rt; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
        Directory.CreateDirectory("Logs/demo1-frames"); File.WriteAllBytes("Logs/demo1-frames/" + name + ".png", texture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        Destroy(rt); Destroy(texture); Destroy(cameraObject); Destroy(lightObject);
    }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 60;
        bufferAudit = SessionState.GetBool("FactoryGameplayBuffers", false); SessionState.SetBool("FactoryGameplayBuffers", false);
        expectedProducts = bufferAudit ? 6 : 3;
        prebuffer = SessionState.GetBool("FactoryGameplayBuffered", false);
        turnAudit = SessionState.GetBool("FactoryGameplayTurns", false);
        SessionState.SetBool("FactoryGameplayTurns", false); omitConveyor = turnAudit;
        automaticStorage = SessionState.GetBool("FactoryGameplayStorage", false);
        SessionState.SetBool("FactoryGameplayStorage", false);
        SessionState.SetBool("FactoryGameplayBuffered", false);
        system = new GameObject("Production").AddComponent<FactoryProduction>();
        Vector3[] docks = { new Vector3(-28.5f, 0, -28.5f), new Vector3(-10.5f, 0, -28.5f), new Vector3(10.5f, 0, -10.5f) };
        float[] yaw = { 0, 0, 90 };
        for (int kind = 0; kind < 3; kind++)
        {
            var obj = FactoryProductionAuditRunner.Model(kind, new Vector3(kind * 40, 0, 40), yaw[kind]);
            Vector3 current = obj.GetComponent<FactoryPorts>().DockPosition;
            obj.transform.position += new Vector3(docks[kind].x - current.x, 0, docks[kind].z - current.z);
            obj.GetComponent<EquipmentGrid>().Snap(obj.GetComponent<EquipmentGrid>().Footprint.center);
            system.Register(obj, kind); stations.Add(obj.GetComponent<FactoryStation>());
            report.AppendLine("Dock " + kind + ": " + obj.GetComponent<FactoryPorts>().DockPosition.ToString("F4"));
        }
        if (automaticStorage) system.CreateFinishedArea(new Vector3(31.5f, 0, -28.5f), 2.1224f);
        if (prebuffer)
        {
            system.AddRaw(stations[0], 3);
            float bufferLimit = Time.time + 420;
            while (stations[0].Completed < 3 && Time.time < bufferLimit && !failed) yield return null;
            Check(stations[0].OutputCount == 3, "three finished punch plates are stacked before any arm is installed");
        }
        probe = FactoryProductionAuditRunner.Model(4, new Vector3(200, 0, 200), 0);
        var startCell = Cell(stations[0].Ports.DockPosition);
        foreach (var station in stations)
            foreach (var cell in Route(startCell, Cell(station.Ports.DockPosition))) railCells.Add(cell);
        if (!turnAudit) foreach (var cell in Route(startCell, Cell(stations[2].Ports.ConveyorDockPosition))) railCells.Add(cell);
        if (automaticStorage)
        {
            foreach (var cell in Route(startCell, Cell(stations[2].Ports.FinishedPickupDock))) railCells.Add(cell);
            foreach (var cell in Route(startCell, Cell(system.FinishedArea.transform.position))) railCells.Add(cell);
        }
        FactoryRail first = null;
        foreach (var cell in railCells)
        {
            var railObject = CreateRail(cell); system.Register(railObject, 4);
            if (cell == startCell) first = railObject.GetComponent<FactoryRail>();
        }
        if (!turnAudit) { Destroy(probe); probe = null; }
        var armObject = FactoryProductionAuditRunner.Model(3, Point(startCell), 0);
        arm = armObject.AddComponent<FactoryArm>(); arm.Initialize(first);
        foreach (var station in stations)
            Check(arm.CanReach(station.Ports.DockPosition, out _), "rail reaches original docking point of " + station.name);
        Check(turnAudit ? !arm.CanReach(stations[2].Ports.ConveyorDockPosition, out _) : arm.CanReach(stations[2].Ports.ConveyorDockPosition, out _),
            turnAudit ? "welding input is connected before the conveyor extension is built" : "rail reaches the new conveyor placement dock");
        if (automaticStorage)
        {
            Check(arm.CanReach(stations[2].Ports.FinishedPickupDock, out _), "rail reaches the belt-end pickup dock");
            Check(arm.CanReach(system.FinishedArea.DockPosition(stations[2].Ports.FinishedPickupDock.y), out _), "bent connected rails reach the final storage area");
        }
        var extra = FactoryProductionAuditRunner.Model(3, Point(startCell), 0);
        Check(!system.FindRail(extra, null, out _), "second arm on connected track is rejected"); Destroy(extra);
        if (!prebuffer)
        {
            system.AddRaw(stations[0], expectedProducts);
            Check(stations[0].Input.Count == expectedProducts, "raw sheets can be queued: " + expectedProducts);
        }
        float limit = Time.time + 1400;
        int collected = 0;
        while ((automaticStorage ? system.Stored < expectedProducts : system.Finished < expectedProducts) && Time.time < limit && !failed)
        {
            if (turnAudit && arm.WaitingForConveyor && !conveyorExtended)
            {
                Vector3 parked = arm.TrackPosition;
                for (int wait = 0; wait < 60; wait++) yield return null;
                Check(arm.WaitingForConveyor && arm.Busy && !arm.Holding && stations[2].Busy &&
                    Vector3.Distance(parked, arm.TrackPosition) < .001f,
                    "part reaches welding across rotated rails before downstream rails exist, then waits safely on the H platform");
                omitConveyor = false;
                foreach (var cell in Route(Cell(stations[2].Ports.DockPosition), Cell(stations[2].Ports.ConveyorDockPosition)))
                    if (railCells.Add(cell)) system.Register(CreateRail(cell), 4);
                Check(arm.CanReach(stations[2].Ports.ConveyorDockPosition, out _), "adding the missing rotated-rail extension reconnects the conveyor");
                conveyorExtended = true; Destroy(probe); probe = null;
            }
            if (bufferAudit && !bufferResumed && stations[2].OutputCount == 5 && stations[1].Output != null && !arm.Busy)
            {
                Check(!stations[2].CanAccept && arm.Holding, "five conveyor products stop the next inbound carrier task");
                CheckConveyorGeometry();
                int complete = system.Finished; Vector3 parked = arm.TrackPosition;
                for (int wait = 0; wait < 90; wait++) yield return null;
                Check(system.Finished == complete && stations[2].OutputCount == 5 && !arm.Busy && Vector3.Distance(parked, arm.TrackPosition) < .001f,
                    "full conveyor neither overflows nor starts the sixth inbound transport");
                var removed = stations[2].TakeOutput(); removed.transform.SetParent(system.Products, true); removed.transform.position += Vector3.up * 10;
                Check(stations[2].OutputCount == 4, "taking the FIFO front opens one conveyor slot"); bufferResumed = true;
            }
            if (!bufferAudit && !automaticStorage && stations[2].Output != null)
            {
                // Simulate Demo1's player collecting the previous belt output.
                var product = stations[2].TakeOutput(); product.transform.SetParent(system.Products, true);
                product.transform.position += Vector3.right * (6 + collected * 2); collected++;
            }
            yield return null;
        }
        if (automaticStorage)
        {
            // Drop credits storage before the empty carrier has returned home.
            // Its return remains part of the task and must finish before asserting
            // the arm is idle or replacing it with the dedicated storage arm.
            float returnLimit = Time.time + 90;
            while (arm.Busy && Time.time < returnLimit && !failed) yield return null;
        }
        Check(system.Finished == expectedProducts, "all products finish through original Demo1 animations and autonomous rail transport: " + expectedProducts);
        if (turnAudit) Check(conveyorExtended, "waiting arm resumes automatically after the player extends the rails");
        Check(compared > 1000 && maxPoseError < 0.003f, "visible production mesh poses match Demo1 throughout gameplay; samples=" + compared + " max error=" + maxPoseError.ToString("G8") + " context=" + maximumPoseContext);
        Check(planningPoseSamples > 0 && maximumPlanningDrift < .003f,
            "hidden planning holds the displayed pose; samples=" + planningPoseSamples + " drift=" + maximumPlanningDrift.ToString("G8"));
        Check(stationaryWristChecksSkipped >= 36,
            "already-level bending wrists omit duplicate swept checks; skipped samples=" + stationaryWristChecksSkipped);
        if (!prebuffer) Check(maxBusy >= 2, "modules process concurrently with separate workpiece sessions");
        else Check(stations[0].OutputCount == 0, "arm drains the complete stack, including elevated top plates");
        Check(!arm.Holding && !arm.Busy, "arm releases the last workpiece and finishes its original retraction");
        Check(weldedByCarrier.Count == expectedProducts && placedByCarrier.Count == expectedProducts,
            "the same player carrier picks every welded product and places it on the conveyor");
        Check(parkedFrames > 100 && maximumWeldDrift < 0.001f,
            "carrier stays reserved at welding dock throughout welding; parked frames=" + parkedFrames + " drift=" + maximumWeldDrift);
        Check(fastestRailSpeed > arm.RailSpeed * 0.95f && fastestRailSpeed < arm.RailSpeed * 1.02f,
            "actual rail travel uses the faster configured speed; speed=" + fastestRailSpeed);
        if (automaticStorage)
        {
            Check(reusedFinishedPickupPlans.Count >= expectedProducts,
                "finished pickup reuses all three validated paths after docking; products=" + reusedFinishedPickupPlans.Count);
            Check(system.Stored == 3 && arm.FinishedDeliveries == 3 && stations[2].Output == null,
                "three complete products reach final storage without manual collection or shared-arm deadlock");
            Destroy(arm.gameObject);
            foreach (var rail in new List<FactoryRail>(system.Rails)) Destroy(rail.gameObject);
            yield return null;
            probe = FactoryProductionAuditRunner.Model(4, new Vector3(200, 0, 200), 0);
            var storageStart = Cell(stations[2].Ports.FinishedPickupDock);
            FactoryRail storageRail = null;
            foreach (var cell in Route(storageStart, Cell(system.FinishedArea.transform.position)))
            {
                var railObject = FactoryProductionAuditRunner.Model(4, Point(cell), 0); system.Register(railObject, 4);
                if (cell == storageStart) storageRail = railObject.GetComponent<FactoryRail>();
            }
            Destroy(probe); probe = null;
            var storageArm = FactoryProductionAuditRunner.Model(3, Point(storageStart), 0);
            arm = storageArm.AddComponent<FactoryArm>(); arm.Initialize(storageRail); sampledTrack = false;
            Check(!arm.CanReach(stations[0].Ports.DockPosition, out _) &&
                arm.CanReach(stations[2].Ports.FinishedPickupDock, out _) &&
                arm.CanReach(system.FinishedArea.DockPosition(stations[2].Ports.FinishedPickupDock.y), out _),
                "a dedicated belt-to-storage track works independently of production rails");
            var template = system.FinishedArea.Products.GetChild(0).GetComponent<FactoryWorkpiece>();
            for (int i = 3; i < 20 && !failed; i++)
            {
                var part = CopyFinished(template);
                Bounds geometry = FactoryWorkpiece.GeometryBounds(part.transform);
                part.transform.position += stations[2].Ports.OutputPort.position - new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
                stations[2].ReturnOutput(part);
                float storageLimit = Time.time + 90;
                while (system.Stored <= i && Time.time < storageLimit && !failed) yield return null;
                Check(system.Stored == i + 1, "all landing directions and layers accept real carried product " + (i + 1));
            }
            Check(system.Stored == 20 && system.FinishedArea.Products.childCount == 20,
                "all twenty products remain visible and counted in the final area");
            Check(!system.FinishedArea.HasSpace && system.FinishedArea.Reserve() < 0, "full storage does not reserve another product");
            foreach (Transform a in system.FinishedArea.Products)
                foreach (Transform b in system.FinishedArea.Products)
                    if (a != b)
                    {
                        Bounds bounds = FactoryWorkpiece.GeometryBounds(a); bounds.Expand(-0.003f);
                        Check(!bounds.Intersects(FactoryWorkpiece.GeometryBounds(b)), "stored products do not overlap: " + a.GetSiblingIndex() + "/" + b.GetSiblingIndex() + " " + bounds + " / " + FactoryWorkpiece.GeometryBounds(b));
                    }
        }
        if (bufferAudit)
        {
            Check(bufferResumed && stations[2].OutputCount == 5, "carrier automatically resumes after removal and refills only the free slot");
            CheckConveyorGeometry();
            yield return DrainConveyorBuffer();
        }
        Finish(true);
    }
    void CheckConveyorGeometry()
    {
        var queue = (List<FactoryWorkpiece>)typeof(FactoryStation).GetField("conveyorOutputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(stations[2]);
        Debug.Log("BUFFER GEOMETRY size=" + FactoryWorkpiece.GeometryBounds(queue[0].transform).size);
        foreach (var a in queue) foreach (var b in queue)
            if (a != b)
            {
                Bounds bounds = FactoryWorkpiece.GeometryBounds(a.transform); bounds.Expand(-.003f);
                Check(!bounds.Intersects(FactoryWorkpiece.GeometryBounds(b.transform)), "five conveyor products remain physically separate");
            }
        var extra = CopyFinished(queue[0]);
        Check(!stations[2].ReturnOutput(extra) && stations[2].OutputCount == 5, "sixth conveyor output is rejected without replacing buffered products"); Destroy(extra.gameObject);
    }
    IEnumerator DrainConveyorBuffer()
    {
        system.CreateFinishedArea(new Vector3(200, 0, 200), 2.1224f);
        var area = system.FinishedArea;
        var railObject = FactoryProductionAuditRunner.Model(4, area.transform.position, 0); var rail = railObject.GetComponent<FactoryRail>();
        Bounds a = area.DockArea.Footprint, b = railObject.GetComponent<EquipmentGrid>().Footprint;
        Vector3 center = a.center + Vector3.Scale(Vector3.left, a.extents + b.extents);
        railObject.transform.position += new Vector3(center.x - b.center.x, 0, center.z - b.center.z);
        rail.Measure(); system.Rails.Add(rail); system.Graph.Build(system.Rails);
        var belt = stations[2]; belt.transform.position += new Vector3(center.x - belt.Ports.FinishedPickupDock.x, 0, center.z - belt.Ports.FinishedPickupDock.z);
        var carrierObject = FactoryProductionAuditRunner.Model(3, center, 0); var carrier = carrierObject.AddComponent<FactoryArm>(); carrier.Initialize(rail);
        float deadline = Time.time + 300;
        while ((system.Stored < 5 || carrier.Busy) && Time.time < deadline && !failed) yield return null;
        Check(system.Stored == 5 && belt.OutputCount == 0 && carrier.FinishedDeliveries == 5, "outbound arm drains all five FIFO products into final storage and returns home");
    }
    FactoryWorkpiece CopyFinished(FactoryWorkpiece template)
    {
        var root = new GameObject("成品回归样件", typeof(FactoryVisual));
        var part = root.AddComponent<FactoryWorkpiece>(); part.SetStage(3);
        foreach (var source in template.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            mesh.transform.SetParent(root.transform, false); mesh.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            part.PaintRenderer(mesh.GetComponent<MeshRenderer>());
            Demo1MeshMirror.SetPose(mesh.transform, source.transform.localToWorldMatrix);
        }
        return part;
    }
    GameObject CreateRail(Vector2Int cell)
    {
        int preferred = turnAudit ? Mathf.Abs(cell.x + cell.y) % 4 : 0;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float yaw = ((preferred + attempt) % 4) * 90;
            var model = FactoryProductionAuditRunner.Model(4, Point(cell), yaw);
            if (!turnAudit || !stations.Exists(s => FactoryProduction.HardwareOverlap(model, s.gameObject)))
            {
                if (turnAudit) report.AppendLine("rotated rail " + cell + " yaw=" + yaw);
                return model;
            }
            Destroy(model);
        }
        throw new Exception("No hardware-safe rotated rail placement at " + cell);
    }
    void Finish(bool ok)
    {
        finished = true; enabled = false; Directory.CreateDirectory("Logs");
        if (!ok)
        {
            foreach (var station in stations) report.AppendLine(station.name + " busy=" + station.Busy + " queued=" + station.Input.Count + " output=" + (station.Output != null) + " state=" + station.Status);
            if (arm != null) report.AppendLine("Arm " + arm.Status + " holding=" + arm.Holding);
        }
        File.WriteAllText(bufferAudit ? "Logs/factory-conveyor-buffer-audit.txt" : turnAudit ? "Logs/factory-weld-turns-audit.txt" : automaticStorage ? "Logs/factory-final-storage-audit.txt" : prebuffer ? "Logs/demo1-buffered-transport-audit.txt" : "Logs/demo1-gameplay-audit.txt",
            report.ToString() + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else { Time.captureDeltaTime = 0; EditorApplication.isPlaying = false; }
    }
}
#endif
