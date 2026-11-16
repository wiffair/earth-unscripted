using UnityEngine;

namespace RoadRush
{
    public sealed partial class AutomaticWorldHost
    {
        Vector3? relocationTarget;
        float qaNextRelocationDelay;
        double qaRelocationReadyAt;
        public bool Relocating => relocationTarget.HasValue;
        public void BeginRelocation(Vector3 target)
        {
            if(Relocating)throw new System.InvalidOperationException("An automatic relocation is already loading");
            relocationTarget=target;
            qaRelocationReadyAt=Time.realtimeSinceStartupAsDouble+qaNextRelocationDelay;qaNextRelocationDelay=0;
        }
        public bool RelocationReady
        {
            get
            {
                if(!Ready || !relocationTarget.HasValue)return false;
                if(Time.realtimeSinceStartupAsDouble<qaRelocationReadyAt)return false;
                var target=relocationTarget.Value;
                // The car must fit across a chunk corner, not merely have one
                // centre point in a newly constructed mesh.
                foreach(float x in new[]{-6f,6f})foreach(float z in new[]{-6f,6f})
                    if(!Renderer.HasGeometryAt(target+new Vector3(x,0,z)))return false;
                return true;
            }
        }
        public void EndRelocation(){relocationTarget=null;}
        public void DelayNextRelocationForQa(float seconds)
        {
            if(Application.identifier!="games.blackwood.roadrush.qa.automaticworld" || seconds<0 || seconds>10)
                throw new System.InvalidOperationException("Bounded isolated QA relocation fixture required");
            qaNextRelocationDelay=seconds;
        }
    }
}
