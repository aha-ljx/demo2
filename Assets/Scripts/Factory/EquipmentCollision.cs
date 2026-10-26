using UnityEngine;

// Only committed equipment receives these colliders. Each collider follows its
// original animated mesh, leaving the empty space around the machine walkable.
public static class EquipmentCollision
{
    public static void Install(GameObject equipment)
    {
        foreach (var renderer in equipment.GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled || renderer.GetComponentInParent<FactoryVisual>() != null ||
                renderer.name == "GridBasePlate") continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var collider = renderer.GetComponent<MeshCollider>();
            if (collider == null) collider = renderer.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.enabled = true;
        }
        Physics.SyncTransforms();
    }
}
