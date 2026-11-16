using System.Collections;
using UnityEngine;
namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // The wrapper is active only in explicitly isolated QA. It records the
        // CPU part of MoveNext, excluding all time spent yielding or waiting.
        new public Coroutine StartCoroutine(IEnumerator routine)=>base.StartCoroutine(RoadRush.WorkTelemetry.WrapCoroutine(routine));
    }
}
