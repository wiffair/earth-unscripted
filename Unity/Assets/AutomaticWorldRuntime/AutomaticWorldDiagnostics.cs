using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        public int RetireGeometryForQa(Vector3 target)
        {
            if(Application.identifier!="com.earthunscripted.qa.automaticworld" || !embedded ||
                Vector3.ProjectOnPlane(target-observer,Vector3.up).magnitude<100)
                throw new System.InvalidOperationException("Isolated distant QA geometry retirement required");
            int retired=0;
            foreach(var key in new System.Collections.Generic.List<Vector2Int>(chunks.Keys))
            {
                if(Mathf.Abs((key.x+.5f)*64-target.x)>96 || Mathf.Abs((key.y+.5f)*64-target.z)>96)continue;
                DestroyChunk(chunks[key]);chunks.Remove(key);report.retiredChunks++;retired++;
            }
            lastReconcilePosition=new Vector3(float.MaxValue,0,0);return retired;
        }
        public string GeometryDiagnosticsJson() => JsonUtility.ToJson(report);
    }
}
