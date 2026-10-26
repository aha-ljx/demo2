using UnityEditor;

public static class FactoryFinishedPickupAudit
{
    [MenuItem("Tools/Factory/Run Buffered Finished Pickup Audit")]
    public static void Run()
    {
        SessionState.SetBool("FactoryFinishedPickupAudit.Pending", true);
        FactoryWorkflowInputAudit.Run();
    }
}
