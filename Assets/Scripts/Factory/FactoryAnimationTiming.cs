// Calibrated at the factory's default 3x travel / 3x pickup / 2x rotation.
// Change only each machine's own actuators. Carrier speed, arm joints, the
// conveyor and the original Demo1 source animation remain unchanged.
public static class FactoryAnimationTiming
{
    public static void Apply(LoadingProcessController source)
    {
        // Keep the calibrated loading/unloading motion, but model the slow
        // turret operation with a separate 30-second dwell (about 58s total).
        source.punchHoldSeconds = 30f;
        const float punchRate = 0.9484f;
        source.suctionMoveSpeed *= punchRate;
        source.suctionVerticalSpeed *= punchRate;
        source.clampMoveSpeed *= punchRate;
        source.feedSpeed *= punchRate;
        source.punchClampMoveSpeed *= punchRate;
        source.punchFeedSpeed *= punchRate;
        source.jawCloseSpeed *= punchRate;

        // Keep the shared arm's approach, indexing and retreat speeds intact.
        // Shorten only the four die strokes and their pressure holds.
        source.bendingPressSpeed *= 4f;
        source.bendingPressHold = .075f;

        // Handoff/arm clearance is unchanged. Speed up the welding gun,
        // seam traversal and turntable to complete the task in 20 seconds.
        const float weldingRate = 1.1083f;
        source.weldingGunMoveSpeed *= weldingRate;
        source.weldingSeamSpeed *= weldingRate;
        source.weldingTurntableSpeed *= weldingRate;
    }
}
