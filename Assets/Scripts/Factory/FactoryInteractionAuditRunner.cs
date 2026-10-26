#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FactoryInteractionAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    bool failed;
    void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.AppendLine("PASS " + message);
    }
    void OnEnable() { Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error)
        { failed = true; report.AppendLine(message + "\n" + stack); }
    }
    IEnumerator Start()
    {
        yield return null;
        try { Run(); } catch (Exception e) { failed = true; report.AppendLine(e.ToString()); }
        if (!failed)
        {
            DontDestroyOnLoad(gameObject);
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/GameScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            try
            {
                var supply = FindObjectOfType<RawMaterialSupply>();
                var placement = FindObjectOfType<PlacementSystem>();
                Check(placement != null && supply != null && supply.Remaining == 20,
                    "actual GameScene spawns the material stack on startup");
                Check(placement.playerCollider.detectCollisions && supply.NearStock,
                    "actual milk egg has collision enabled and can approach the starting stack");
                Check(supply.TryPickUp() && supply.Remaining == 19,
                    "actual GameScene supports pickup from the startup material stack");
                supply.UpdateHeld(placement.worldCamera);
            }
            catch (Exception e) { failed = true; report.AppendLine(e.ToString()); }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/factory-interaction-audit.txt", (failed ? "FAIL\n" : "PASS\n") + report);
        Debug.Log("Factory interaction audit " + (failed ? "FAIL" : "PASS"));
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else EditorApplication.isPlaying = false;
    }
    void Run()
    {
        var system = new GameObject("Production").AddComponent<FactoryProduction>();
        system.enabled = false;
        var actor = new GameObject("Milk egg").AddComponent<CharacterController>();
        actor.height = 2.5f; actor.radius = 0.9f; actor.transform.position = new Vector3(0, 1.25f, 0);
        var supply = new GameObject("Stock").AddComponent<RawMaterialSupply>();
        supply.Initialize(actor.transform, actor, system, 1.69792f);
        Physics.SyncTransforms();
        Check(supply.Remaining == 20 && supply.GetComponentsInChildren<FactoryWorkpiece>().Length == 20, "startup has 20 actual stacked plates");
        actor.transform.position = new Vector3(-20, 1.25f, -20); Physics.SyncTransforms();
        Check(!supply.TryPickUp() && supply.Remaining == 20, "distant pickup cannot consume stock");
        actor.transform.position = supply.transform.position + new Vector3(-2, 1.25f, 0); Physics.SyncTransforms();
        Check(supply.TryPickUp() && supply.Remaining == 19, "nearby pickup removes exactly one plate");
        var identity = supply.Held;
        Check(!supply.TryPickUp() && supply.Held == identity && supply.Remaining == 19, "holding a plate prevents a second pickup");
        Check(!supply.TryDrop(supply.transform.position, new List<GameObject>()) && supply.Held == identity, "occupied placement retains held material");
        Vector3 drop = actor.transform.position + Vector3.left * 2.5f; drop.y = 0;
        Check(supply.TryDrop(drop, new List<GameObject>()) && supply.Held == null, "left-click operation can drop onto clear ground");
        Check(identity.GetComponent<BoxCollider>().enabled, "ground plate has physical collision");
        Check(supply.TryPickUp() && supply.Held == identity && supply.Remaining == 19, "ground plate can be picked up again without consuming stock");
        Check(!identity.GetComponent<BoxCollider>().enabled, "held plate cannot obstruct the player");

        FactoryStation punch = null;
        bool collisionChecked = false;
        for (int kind = 0; kind < 5; kind++)
        {
            var model = FactoryProductionAuditRunner.Model(kind, new Vector3(50 + kind * 45, 0, 40), kind * 90);
            system.Register(model, kind);
            var station = model.GetComponent<FactoryStation>();
            if (station != null) station.enabled = false;
            if (kind == 0) punch = station;
            int colliders = 0;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.name == "GridBasePlate" || renderer.GetComponentInParent<FactoryVisual>() != null) continue;
                var collider = renderer.GetComponent<MeshCollider>();
                Check(collider != null && collider.enabled && !collider.isTrigger &&
                    collider.sharedMesh == renderer.GetComponent<MeshFilter>().sharedMesh, "mesh collision matches " + kind + "/" + renderer.name);
                colliders++;
                if (!collisionChecked) collisionChecked = CheckCharacterCollision(collider);
            }
            Check(colliders > 0, "equipment type " + kind + " has solid hardware");
            model.transform.Rotate(Vector3.up, 90); Physics.SyncTransforms();
            foreach (var collider in model.GetComponentsInChildren<MeshCollider>())
                Check((collider.bounds.center - collider.GetComponent<Renderer>().bounds.center).magnitude < 0.01f,
                    "collision follows moved and rotated hardware");
        }
        Check(collisionChecked, "CharacterController movement is physically blocked by real machine geometry");
        Check(!supply.TryFeed(punch) && supply.Held == identity, "distant station cannot take held plate");
        actor.transform.position = punch.Ports.InputPort.position + Vector3.up; Physics.SyncTransforms();
        Check(supply.TryFeed(punch) && supply.Held == null && punch.Input.Peek() == identity,
            "feeding transfers the exact picked plate into the punch queue");
        actor.transform.position = supply.transform.position + new Vector3(-2, 1.25f, 0); Physics.SyncTransforms();
        Check(supply.TryPickUp(), "next stock plate can be picked");
        var next = supply.Held;
        actor.transform.position = punch.Ports.InputPort.position + Vector3.up; Physics.SyncTransforms();
        punch.Incoming = 31;
        Check(!supply.TryFeed(punch) && supply.Held == next, "full queue preserves held plate");
        punch.Incoming = 0;
        Check(supply.TryFeed(punch) && punch.Input.ToArray()[1] == next, "multiple plates keep FIFO queue order");
        for (int i = 0; i < 18; i++)
        {
            actor.transform.position = supply.transform.position + new Vector3(-2, 1.25f, 0); Physics.SyncTransforms();
            Check(supply.TryPickUp(), "remaining stock can be picked once " + i);
            actor.transform.position = punch.Ports.InputPort.position + Vector3.up; Physics.SyncTransforms();
            Check(supply.TryFeed(punch), "remaining stock enters queue " + i);
        }
        Check(supply.Remaining == 0 && punch.Input.Count == 20 && supply.Held == null,
            "all 20 original plates are conserved after transfer");
        actor.transform.position = supply.transform.position + Vector3.up; Physics.SyncTransforms();
        Check(!supply.TryPickUp(), "empty stock cannot generate additional materials");
    }
    bool CheckCharacterCollision(MeshCollider mesh)
    {
        Bounds b = mesh.bounds;
        if (b.size.y < 0.6f) return false;
        Physics.SyncTransforms();
        foreach (Vector3 direction in new[] { Vector3.right, Vector3.forward })
        {
            Vector3 from = b.center - direction * (b.extents.magnitude + 2);
            if (!mesh.Raycast(new Ray(from, direction), out var hit, b.size.magnitude + 4) || Mathf.Abs(hit.normal.y) > 0.2f) continue;
            var probe = new GameObject("Collision probe").AddComponent<CharacterController>();
            probe.height = 0.5f; probe.radius = 0.18f; probe.stepOffset = 0.01f;
            probe.transform.position = hit.point + hit.normal * 0.8f; Physics.SyncTransforms();
            bool blocked = false;
            for (int i = 0; i < 40; i++)
                blocked |= (probe.Move(-hit.normal * 0.05f) & CollisionFlags.Sides) != 0;
            bool outside = Vector3.Dot(probe.transform.position - hit.point, hit.normal) > -0.2f;
            Destroy(probe.gameObject);
            if (blocked && outside) return true;
        }
        return false;
    }
}
#endif
