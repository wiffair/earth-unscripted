using System;
using UnityEngine;
namespace EarthUnscripted
{
    public static class BootSceneOwnershipVerificationDraft
    {
        // Pure Editor object-lifecycle fixture. No GameSession/Awake, scene load,
        // profile, providers, installed data or active gameplay objects involved.
        public static void Verify(Action<bool,string> check)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Run isolated lifecycle fixture in Editor mode");
            var host=new GameObject("ownership-fixture");var foreign=new GameObject("unrelated-scene-root");var oldChild=new GameObject("pre-existing-host-child");oldChild.transform.SetParent(host.transform);var oldLight=host.AddComponent<Light>();var oldCollider=host.AddComponent<BoxCollider>();
            GameObject world=null,player=null,adopted=null;Mesh mesh=null;
            try
            {
                var owner=new BootSceneOwnership(host);world=owner.OwnRoot(new GameObject("partially-built-world"));
                mesh=new Mesh{name="owned-fixture-mesh"};world.AddComponent<GeneratedMeshOwner>().Mesh=mesh;world.AddComponent<BoxCollider>();
                // Registered before any visual/component initialization failure.
                player=owner.OwnRoot(new GameObject("player-before-Car-assignment"));player.AddComponent<BoxCollider>();
                var newChild=new GameObject("partially-built-pit-or-resident");newChild.transform.SetParent(host.transform);
                var newLight=host.AddComponent<Camera>();var audio=host.AddComponent<AudioSource>();
                owner.Quiesce();check(!world.activeSelf&&!player.activeSelf&&!newChild.activeSelf,"partial world/player/host-child roots quiesce immediately");check(!newLight.enabled&&!audio.enabled,"partial attached behaviours/audio stop before deferred destruction");check(oldLight.enabled&&oldCollider.enabled&&oldChild.activeSelf&&foreign.activeSelf,"pre-existing host components/children and unrelated scene roots remain untouched");
                owner.Rollback();check(!world&&!player&&!newChild,"rollback destroys every registered and newly attached child root");check(!newLight&&!audio,"rollback destroys only components introduced by the boot attempt");check(!mesh,"owned generated mesh releases with failed world root");check(host&&oldLight&&oldCollider&&oldChild&&foreign,"rollback preserves baseline and external objects");check(owner.CleanupWarnings=="","normal rollback has no suppressed cleanup exceptions");owner.Rollback();check(host&&foreign,"rollback is idempotent");
                var completed=new BootSceneOwnership(host);adopted=completed.OwnRoot(new GameObject("completed-scene-root"));completed.Commit();completed.Rollback();check(adopted&&adopted.activeSelf,"adopted scene cannot be removed by later rollback");
                var invalid=new BootSceneOwnership(host);bool rejected=false;try{invalid.OwnRoot(host);}catch(ArgumentException){rejected=true;}check(rejected,"session host cannot accidentally be registered as disposable scene root");invalid.Rollback();
            }
            finally{if(world)UnityEngine.Object.DestroyImmediate(world);if(player)UnityEngine.Object.DestroyImmediate(player);if(adopted)UnityEngine.Object.DestroyImmediate(adopted);if(host)UnityEngine.Object.DestroyImmediate(host);if(foreign)UnityEngine.Object.DestroyImmediate(foreign);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
}
