using System.Collections.Generic;
using UnityEngine;

// Follows the exact validated polyline, carrying unused distance over corners.
// No curve cutting: the base must remain on the player's connected rails.
public sealed class FactoryTravelPath
{
    readonly List<Vector3> points;
    readonly float height;
    int next;
    public Vector3 Position { get; private set; }
    public bool Complete => next >= points.Count;

    public FactoryTravelPath(List<Vector3> points, Vector3 start, float height)
    {
        this.points = points;
        this.height = height;
        Position = start;
    }

    public Vector3 Advance(float distance)
    {
        distance = Mathf.Max(0, distance);
        while (!Complete)
        {
            Vector3 target = points[next]; target.y = height;
            float length = Vector3.Distance(Position, target);
            if (length > distance && length > .000001f)
            {
                Position = Vector3.MoveTowards(Position, target, distance);
                break;
            }
            Position = target;
            distance = Mathf.Max(0, distance - length);
            next++;
        }
        return Position;
    }
}
