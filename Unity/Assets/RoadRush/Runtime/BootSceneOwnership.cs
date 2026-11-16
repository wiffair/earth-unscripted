using System;
using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    // One boot attempt only. Records roots at allocation, before any fallible build.
    // Also catches RequireComponent additions and partially built host children.
    public sealed class BootSceneOwnership
    {
        readonly GameObject host;readonly HashSet<Component> beforeComponents=new HashSet<Component>();readonly HashSet<Transform> beforeChildren=new HashSet<Transform>();readonly HashSet<GameObject> roots=new HashSet<GameObject>();
        Component[] pendingComponents=Array.Empty<Component>();GameObject[] pendingRoots=Array.Empty<GameObject>();bool finished,quiesced;
        public string CleanupWarnings {get;private set;}="";
        public BootSceneOwnership(GameObject host)
        {
            if(!host)throw new ArgumentNullException(nameof(host));this.host=host;
            foreach(var component in host.GetComponents<Component>())beforeComponents.Add(component);
            foreach(Transform child in host.transform)beforeChildren.Add(child);
        }
        public GameObject OwnRoot(GameObject root)
        {
            if(finished||quiesced)throw new InvalidOperationException("Boot ownership no longer accepts allocations");
            if(!root||root==host||host.transform.IsChildOf(root.transform))throw new ArgumentException("Boot root must not contain the session host");
            roots.Add(root);return root;
        }
        void Attempt(Action action)
        {try{action();}catch(Exception e){if(CleanupWarnings.Length<1200)CleanupWarnings+=(CleanupWarnings.Length==0?"":"; ")+e.GetType().Name+": "+e.Message.Substring(0,Math.Min(e.Message.Length,180));}}
        public void Quiesce()
        {
            if(finished||quiesced)return;quiesced=true;
            if(host)
            {
                var components=new List<Component>();foreach(var component in host.GetComponents<Component>())if(component&&!(component is Transform)&&!beforeComponents.Contains(component))components.Add(component);pendingComponents=components.ToArray();
                foreach(Transform child in host.transform)if(!beforeChildren.Contains(child))roots.Add(child.gameObject);
            }
            pendingRoots=new List<GameObject>(roots).ToArray();
            foreach(var root in pendingRoots)if(root)Attempt(()=>root.SetActive(false));
            foreach(var component in pendingComponents)if(component)
            {
                if(component is Behaviour behaviour)Attempt(()=>behaviour.enabled=false);
                if(component is AudioSource audio)Attempt(()=>{audio.Stop();audio.enabled=false;});
                if(component is MonoBehaviour mono)Attempt(mono.StopAllCoroutines);
            }
        }
        public void Rollback()
        {
            if(finished)return;Quiesce();finished=true;
            foreach(var component in pendingComponents)if(component)Attempt(()=>Release(component));
            foreach(var root in pendingRoots)if(root)Attempt(()=>{if(!Application.isPlaying)Geometry.ReleaseMeshes(root);Release(root);});
            roots.Clear();beforeComponents.Clear();beforeChildren.Clear();pendingComponents=Array.Empty<Component>();pendingRoots=Array.Empty<GameObject>();
        }
        public void Commit()
        {if(finished)return;if(quiesced)throw new InvalidOperationException("Cancelled scene cannot be adopted");finished=true;roots.Clear();beforeComponents.Clear();beforeChildren.Clear();}
        static void Release(UnityEngine.Object owned){if(Application.isPlaying)UnityEngine.Object.Destroy(owned);else UnityEngine.Object.DestroyImmediate(owned);}
    }
}
