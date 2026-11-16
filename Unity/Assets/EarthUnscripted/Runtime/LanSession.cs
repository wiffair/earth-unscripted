using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed class LanSession : MonoBehaviour
    {
        [Serializable] sealed class Packet
        {
            public bool parkingBrake,parkingPending;public int parkingCommand;public string vehicleRecipe;public int protocol;public PeerUpdateInterest[] interests;public string type,map,epoch,key,token;public DataFileManifest offer;public float steer,throttle,brake,handbrake,echo,hour,rain,wetness,clouds,wind,fog,hostStamp,hostEcho;
            public State host,guest;public bool paused,dataBudget=true;public OwnedCar build;
        }
        [Serializable] sealed class State {public int model;public RaceSetup raceSetup;public RaceTyreState raceTyres;public Vector3 position,velocity;public Quaternion rotation;public float[] wheelY;public float steer,damage,inputSteer;public bool reverse,braking,parkingApplied,parkingHolding;public int parkingAcknowledged;public int revision;public System.Collections.Generic.List<SavedDent> dents;}
        public const int Protocol=12;
        public GameSession Session;
        public bool Hosting {get;private set;} public bool Active=>Hosting||peer!=null||connecting!=null;
        public bool Joined {get;private set;} public string Status {get;private set;}="Offline";
        public int GameplayQueuedBytes=>peer?.QueuedBytes??0;
        public int ReceivedInputs {get;private set;}
        public int RttSamples {get;private set;} public int ReceivedStates {get;private set;} public float RoundTripMs {get;private set;}
        public VehicleController Other {get;private set;} public const int Port=28591;
        TcpListener listener;Socket connecting;IAsyncResult connectResult;LanWire peer;float connectAt,lastPacket,nextSend,echo,lastHostEcho,lastGuestEcho;Packet latest;
        bool previousBackground,syncing,offerSent;string worldEpoch,disconnectNotice,address,expectedHost,requiredEpoch;float nextSync,syncBegan;
        Packet pendingHello;LanMapShare share;string offerFingerprint,offeredEpoch;
        float nextCatalogue,nextUpdateOffer;bool guestDataBudget=true,interestsSent;PeerUpdateInterest[] peerInterests;DataFileManifest pendingManifest;
        readonly Dictionary<string,HashSet<long>> receivedUpdates=new Dictionary<string,HashSet<long>>();int updateCursor;string dataToken,pendingUpdate;
        readonly System.Collections.Generic.HashSet<string> stagedUpdates=new System.Collections.Generic.HashSet<string>();
        public string DataUpdateStatus {get;private set;}="";
        sealed class Rejoin {public string address,fingerprint,epoch;}
        static Rejoin pendingRejoin;
        public static bool HasPendingRejoin=>pendingRejoin!=null;
        public static long LastMapBytesReceived;
        long completedSharedBytes;
        public long SharedBytesSent=>completedSharedBytes+(share?.BytesSent??0);
        public bool MapNetworkingBusy=>syncing||share!=null&&(share.Receiving||share.ReceivingUpdate);
        public long SharedBytesReceived=>share?.BytesReceived??0;
        public string HostFingerprint {get;private set;}="";
        System.Collections.IEnumerator Start()
        {
            if(pendingRejoin==null)yield break;while(!Session||!Session.BootReady||LocationLoading.Busy)yield return null;
            var plan=pendingRejoin;pendingRejoin=null;Join(plan.address);expectedHost=plan.fingerprint;requiredEpoch=plan.epoch;
            if(!WorldEpoch.Matches(Epoch,requiredEpoch)){Stop();Session.SetMenu(true);Status="Loaded host map did not match · reconnect manually";}
        }
        string Map=>Session.World.Route.Region==null?"blackwood":WorldSnapshotCodec.NetworkId(Session.World.Route.Region);
        string Epoch=>worldEpoch??(worldEpoch=WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,Map,Session.World.Route.Region==null?"blackwood-seed128-v1":WorldSnapshotCodec.CanonicalJson(Session.World.Route.Region)));
        public void Host()
        {
            if(SinglePlayerBeta.Enabled){Status="LAN is unavailable in this single-player beta";return;}
            if(Session&&Session.IsRenting){Status="Return the hired car before joining LAN";return;}if(Session&&Session.ImportedCircuit!=null){Status="Imported circuit preview is offline until shared layout/rules validation is available";return;}
            if(Session&&Session.World.GeographicWorld&&(Session.World.GeographicWorld.BuildingWindowBusy||Session.World.GeographicWorld.BuildingWindowIdentity!=null))
            {Status="Use default scenery in the pack library before LAN · local preview is not shared yet";return;}
            if (Session && Session.IsScopedReaderSession800) { Status = "Scoped reader QA cannot host a world"; return; }
            Stop();try{var region=Session.World.Route.Region;if(region!=null&&(region.railways?.Length??0)>0&&!region.railwayProfilesPrepared)throw new InvalidOperationException("Prepare railway profiles before hosting");worldEpoch=null;var epoch=Epoch;share=new LanMapShare();share.Host(Session.World.Route.Region==null?LanMapShare.Seed:WorldSnapshotCodec.CanonicalJson(Session.World.Route.Region),Map,Epoch);listener=new TcpListener(IPAddress.Any,Port);listener.Start(1);Hosting=true;Status="Hosting on port "+Port+" · waiting for one player";Begin();}
            catch(SocketException e){Stop();Status="Could not host: "+e.SocketErrorCode;}catch(Exception e){Stop();Status="Could not prepare host map: "+e.Message;}
        }
        public void Join(string address)
        {
            if(SinglePlayerBeta.Enabled){Status="LAN is unavailable in this single-player beta";return;}
            if(Session&&Session.IsRenting){Status="Return the hired car before joining LAN";return;}if(Session&&Session.ImportedCircuit!=null){Status="Imported circuit preview is offline until shared layout/rules validation is available";return;}
            if(Session&&Session.World.GeographicWorld&&(Session.World.GeographicWorld.BuildingWindowBusy||Session.World.GeographicWorld.BuildingWindowIdentity!=null))
            {Status="Use default scenery in the pack library before LAN · local preview is not shared yet";return;}
            if (Session && Session.IsScopedReaderSession800) { Status = "Scoped reader QA cannot join a world"; return; }
            Stop();if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork){Status="Enter the host's local IPv4 address";return;}
            try{this.address=ip.ToString();worldEpoch=null;var epoch=Epoch;share=new LanMapShare();connecting=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);connectResult=connecting.BeginConnect(new IPEndPoint(ip,Port),null,null);connectAt=Time.unscaledTime;Status="Connecting…";Begin();}
            catch(SocketException e){Stop();Status="Could not connect: "+e.SocketErrorCode;}catch(Exception e){Stop();Status="Could not prepare map: "+e.Message;}
        }
        void Begin(){RoundTripMs=lastHostEcho=lastGuestEcho=0;if(!Hosting&&Session.Conditions)Session.Conditions.CancelLocalFetch();if(Session.Racing)Session.Racing.Cancel();previousBackground=Application.runInBackground;Application.runInBackground=true;Session.SetMenu(!Hosting);}
        VehicleController MakeCar(bool replica,OwnedCar build)
        {
            using var populationFactory = Session.BeginActualVehicleFactory(this, "lan/" + Guid.NewGuid().ToString("N"), VehiclePopulation.ActualVehicleRosterDomain.Role.LanPeer);
            var g=new GameObject("LAN second driver");g.layer=8;
            var box=g.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
            var validated=new GarageState{cars=new System.Collections.Generic.List<OwnedCar>{build??new OwnedCar()}};GarageRules.Validate(validated);var owned=validated.Active;
            var visual=g.AddComponent<CarVisual>();visual.Build(CarColours.Paint[owned.paint],true,owned.model,owned.wheels,owned.bodyKit,owned.finish);VehicleGeometry.ApplyCollider(box,owned.model);
            var car=g.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=true;car.ApplyTuning(owned);car.Replica=replica;car.Body.isKinematic=replica;
            car.Body.position=Session.World.Route.At(25,Session.World.Route.LaneOffset(25))+Vector3.up*.7f;car.Body.rotation=Session.World.Route.Rotation(25);car.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);if(Session.World.GeographicWorld)Session.World.GeographicWorld.SecondaryObserver=car.transform;
            populationFactory.Commit();
            return car;
        }
        void Update()
        {
            if(!Session||!Session.BootReady)return;
            try
            {
                if(connecting!=null)
                {
                    if(connectResult.IsCompleted){connecting.EndConnect(connectResult);peer=new LanWire(connecting);connecting=null;lastPacket=Time.unscaledTime;peer.Send(Encode(new Packet{type="hello",map=Map,epoch=Epoch,build=PublicBuild(),parkingBrake=Session.Input.ParkingBrakeCommand!=Session.Car.ParkingBrakeCommandAck?Session.Input.ParkingBrakeRequest:Session.Car.ParkingBrakeApplied,parkingCommand=Session.Input.ParkingBrakeCommand,parkingPending=Session.Input.ParkingBrakeCommand!=Session.Car.ParkingBrakeCommandAck}));Status="Checking map and protocol…";}
                    else if(Time.unscaledTime-connectAt>8){Stop();Status="Connection timed out";}
                }
                if(Hosting&&peer==null&&listener.Pending())
                {var accepted=listener.AcceptSocket();share.BindPeer(((IPEndPoint)accepted.RemoteEndPoint).Address.ToString());peer=new LanWire(accepted);lastPacket=Time.unscaledTime;Status="Player connecting…";}
                if(peer==null)return;
                peer.Pump(Receive);if(peer==null)return;
                if(!peer.Open&&Hosting&&!Joined)
                {peer.Dispose();peer=null;pendingHello=null;syncing=offerSent=false;share.BindPeer(null);Status="Map shared · waiting for player to reconnect";return;}
                if(!peer.Open){bool rejected=disconnectNotice!=null;string reason=disconnectNotice??("Disconnected: "+peer.Error);Stop();if(rejected)Session.SetMenu(true);Status=reason;return;}
                bool budget=DataPackageRules.NetworkMayRun(Session.Downloads.Busy||(Session.Updates&&Session.Updates.Busy),Session.Conditions.NetworkingBusy,LocationLoading.Busy,RoundTripMs,GameplayQueuedBytes)&&(!Hosting||!Joined||guestDataBudget&&Time.unscaledTime-lastPacket<.5f);share?.Budget(budget);
                if(syncing)
                {
                    if(Time.unscaledTime-syncBegan>180){Stop();Session.SetMenu(true);Status="Map synchronisation timed out · cached progress retained";return;}
                    if(Time.unscaledTime>=nextSync){nextSync=Time.unscaledTime+1;peer.Send(Encode(new Packet{type="sync-heartbeat"}));}
                    if(share?.Error!=null){string error=share.Error;Stop();Session.SetMenu(true);Status="Map sharing deferred · "+error;return;}
                    if(Hosting&&pendingHello!=null&&share.HostReady)
                    {
                        var hello=pendingHello;pendingHello=null;
                        if(hello.map==Map&&WorldEpoch.Matches(Epoch,hello.epoch))Welcome(hello.build,hello.parkingBrake,hello.parkingCommand,hello.parkingPending);
                        else {peer.Send(Encode(new Packet{type="map-offer",map=Map,epoch=Epoch,key=share.Manifest.signer,token=share.Token,offer=share.Manifest}));offerSent=true;Status="Sharing the host's driving area…";}
                    }
                    if(!Hosting&&share.Received)
                    {
                        LastMapBytesReceived=share.BytesReceived;var region=share.Region;string local=region?.id??"";
                        if(region!=null)RegionCache.Save(region,"Verified connected-host snapshot · "+offeredEpoch+"\n"+region.attribution);
                        pendingRejoin=new Rejoin{address=address,fingerprint=offerFingerprint,epoch=offeredEpoch};
                        peer.Send(Encode(new Packet{type="map-reload"}));peer.Pump(_=>{});Stop();if(!Session.SelectRegion(local)){pendingRejoin=null;Session.SetMenu(true);Status="Could not save the shared location · previous drive retained";}return;
                    }
                    if(!Hosting&&share.Receiving)Status=share.Progress;
                }
                if(Joined)UpdateSharing(budget);
                if(Time.unscaledTime-lastPacket>5){Stop();Status="Peer stopped responding";return;}
                if(Joined&&Time.unscaledTime>=nextSend)
                {
                    nextSend=Time.unscaledTime+1f/30;
                    if(Hosting)peer.Send(Encode(new Packet{type="state",host=Capture(Session.Car),guest=Capture(Other),echo=echo,hour=Session.Conditions.Hour,rain=Session.Conditions.Rain,wetness=Session.Conditions.Wetness,clouds=Session.Conditions.Clouds,wind=Session.Conditions.Wind,fog=Session.Conditions.Fog,hostStamp=Time.unscaledTime,paused=Session.Menu}));
                    else peer.Send(Encode(new Packet{type="input",dataBudget=budget,steer=Session.Menu?0:Session.Input.Steer,throttle=Session.Menu?0:Session.Input.Throttle,brake=Session.Menu?1:Session.Input.Brake,handbrake=Session.Menu?1:Session.Input.Handbrake,parkingBrake=Session.Input.ParkingBrakeRequest,parkingCommand=Session.Input.ParkingBrakeCommand,hostEcho=latest?.hostStamp??0,echo=Time.unscaledTime}));
                }
                if(Joined&&!Hosting&&latest?.guest!=null)
                {Apply(Session.Car,latest.guest);Apply(Other,latest.host);Session.Conditions.NetworkConditions(latest.hour,latest.rain,latest.wetness,latest.clouds,latest.wind,latest.fog);}
                if(Joined&&Other!=null){Other.Visual.WiperRain=Session.Conditions.Rain;Other.Visual.SetAutomaticLights(Session.Conditions.Daylight<.55f||Session.Conditions.Rain>.2f);}
                if(Hosting&&Other!=null&&Time.unscaledTime-lastPacket>.3f){Other.NetworkThrottle=0;Other.NetworkBrake=1;Other.NetworkHandbrake=1;}
                // Pump above already consumed incoming messages. Flush this
                // frame's new state/input/handshake without another receive
                // pass or an additional send-attempt budget.
                peer?.FlushOutgoing();
            }
            catch(SocketException e){Stop();Status="LAN: "+e.SocketErrorCode;}
            catch(Exception e){Debug.LogWarning("LAN session ended: "+e.Message);Stop();Status="Invalid network data";}
        }
        static string Encode(Packet packet){packet.protocol=Protocol;packet.vehicleRecipe=Core.VehicleDimensions.Recipe;return JsonUtility.ToJson(packet);}
        void Receive(string text)
        {
            Packet p;try{p=JsonUtility.FromJson<Packet>(text);}catch{Stop();return;}
            if(p==null||p.protocol!=Protocol||p.vehicleRecipe!=Core.VehicleDimensions.Recipe){disconnectNotice="Incompatible game version";peer.Dispose();return;}lastPacket=Time.unscaledTime;
            if((p.type=="hello"||p.type=="welcome")&&(p.build==null||p.build.model<0||p.build.model>=GarageRules.Catalogue.Length||(p.build.model==3&&!RaceWireRules.Valid(p.build.raceSetup,p.build.raceTyres)))){disconnectNotice="Unsupported vehicle build";peer.Dispose();return;}
            if(!Hosting&&p.type=="sync-heartbeat"&&!Joined&&!syncing){syncing=true;syncBegan=Time.unscaledTime;}
            if(Hosting&&p.type=="hello"&&!Joined)
            {
                if(p.parkingCommand<0){peer.Dispose();return;}
                if(syncing&&pendingHello==null&&offerSent)return;
                pendingHello=p;syncing=true;syncBegan=Time.unscaledTime;Status="Checking the host map…";
            }
            else if(!Hosting&&p.type=="welcome"&&!Joined)
            {string fingerprint=DataPackageRules.Fingerprint(p.key);if(expectedHost!=null&&expectedHost!=fingerprint){disconnectNotice="Host identity changed during map reload";peer.Dispose();return;}HostFingerprint=fingerprint;dataToken=p.token;
                if(dataToken==null||dataToken.Length!=64){disconnectNotice="Invalid host data capability";peer.Dispose();return;}
                if(p.map!=Map||!WorldEpoch.Matches(Epoch,p.epoch)){disconnectNotice="Host has different map data · synchronisation required";peer.Dispose();return;}Joined=true;syncing=false;Session.SetMenu(false);Session.Car.Replica=true;Session.Car.ReplicaRevision=-1;Session.Car.Body.isKinematic=true;Other=MakeCar(true,p.build);Status="LAN · joined host";}
            else if(!Hosting&&p.type=="map-offer"&&!Joined)
            {
                if(share.Receiving||share.Received)return;
                string fingerprint=DataPackageRules.Fingerprint(p.key);
                if(p.offer==null||p.offer.signer!=p.key||p.offer.fromVersion!=p.map||!WorldEpoch.Matches(p.offer.toVersion,p.epoch)||!DataPackageRules.VerifySignature(p.offer,fingerprint)||(expectedHost!=null&&expectedHost!=fingerprint)||p.token==null||p.token.Length!=64){disconnectNotice="Invalid host map offer";peer.Dispose();return;}
                HostFingerprint=offerFingerprint=fingerprint;offeredEpoch=p.epoch;syncing=true;syncBegan=Time.unscaledTime;Session.SetMenu(true);share.Receive(address,p.token,p.offer,fingerprint);Status="Receiving the host's driving area…";
            }
            else if(!Hosting&&Joined&&p.type=="update-offer")ReceiveUpdateOffer(p.offer);
            else if(Hosting&&Joined&&p.type=="update-interest")
            {
                if(p.interests==null||p.interests.Length>8||p.interests.Any(i=>!PeerUpdateRules.ValidInterest(i))||p.interests.Select(i=>i.pack).Distinct().Count()!=p.interests.Length)return;
                peerInterests=p.interests;nextCatalogue=0;
            }
            else if(!Hosting&&p.type=="wrong-map"){disconnectNotice="Select host region first: "+p.map;peer.Dispose();}
            else if(!Hosting&&p.type=="wrong-epoch"){disconnectNotice="Different map data · synchronisation required before joining";peer.Dispose();}
            else if(Hosting&&Joined&&p.type=="input")
            {
                if(p.parkingCommand<Other.ParkingBrakeCommandAck||p.parkingCommand-Other.ParkingBrakeCommandAck>32){peer.Dispose();return;}
                if(!Finite(p.steer)||!Finite(p.throttle)||!Finite(p.brake)||!Finite(p.handbrake)||!Finite(p.echo)||!Finite(p.hostEcho)){peer.Dispose();return;}
                guestDataBudget=p.dataBudget;
                if(p.hostEcho>lastHostEcho&&p.hostEcho<=Time.unscaledTime){RoundTripMs=Mathf.Clamp((Time.unscaledTime-p.hostEcho)*1000,0,5000);lastHostEcho=p.hostEcho;RttSamples++;}
                Other.NetworkParkingBrakeRequest=p.parkingBrake;Other.NetworkParkingBrakeCommand=p.parkingCommand;
                Other.NetworkSteer=Mathf.Clamp(p.steer,-1,1);Other.NetworkThrottle=Mathf.Clamp01(p.throttle);Other.NetworkBrake=Mathf.Clamp01(p.brake);Other.NetworkHandbrake=Mathf.Clamp01(p.handbrake);var route=Session.World.Route;float progress=route.Closest(Other.Body.position,out _,out float separation);bool onRoad=separation<route.Width(progress)*.5f;
                if(route.Region!=null)onRoad=route.Region.IsOnRoad(Other.Body.position);
                var surfaceWeather=Session.Conditions.ApplySurface(Other);Other.Grip=(onRoad?1:.53f)*WeatherSurfaceRules.Grip(surfaceWeather,Other.Metrics.Model==3)*Other.TyreFactor;Other.RoadSurface=onRoad;echo=p.echo;ReceivedInputs++;
            }
            else if(!Hosting&&Joined&&p.type=="state"&&Valid(p.host)&&Valid(p.guest)&&Finite(p.hour)&&Finite(p.rain)&&Finite(p.wetness)&&Finite(p.clouds)&&Finite(p.wind)&&Finite(p.fog)&&Finite(p.hostStamp)&&p.hostStamp>=0)
            {latest=p;Status=p.paused?"LAN · host paused":"LAN · joined host";ReceivedStates++;if(p.echo>lastGuestEcho&&p.echo<=Time.unscaledTime){RoundTripMs=Mathf.Max(0,(Time.unscaledTime-p.echo)*1000);lastGuestEcho=p.echo;RttSamples++;}}
        }
        void Welcome(OwnedCar build,bool initialParking,int initialParkingCommand,bool pendingParking)
        {
            Other=MakeCar(false,build);Other.InitializeNetworkParking(initialParking,initialParkingCommand,pendingParking);Joined=true;syncing=offerSent=false;HostFingerprint=DataPackageRules.Fingerprint(share.Manifest.signer);
            peer.Send(Encode(new Packet{type="welcome",map=Map,epoch=Epoch,key=share.Manifest.signer,token=share.Token,build=PublicBuild()}));Status="LAN · host + one driver";
        }
        void UpdateSharing(bool budget)
        {
            if(Hosting)
            {
                if(Session.Updates&&peerInterests!=null&&Time.unscaledTime>=nextCatalogue)
                {
                    var folders=CountryPacks.Installed().Where(p=>p.Ready&&(bool?)p.Metadata["completeNodes"]==true).GroupBy(p=>p.Id).Select(g=>g.First().Folder).Take(8).ToArray();
                    bool started=share.RefreshUpdates(Session.Updates.Folder,folders,peerInterests);nextCatalogue=Time.unscaledTime+(started?60:.5f);
                }
                if(budget&&Time.unscaledTime>=nextUpdateOffer)
                {
                    nextUpdateOffer=Time.unscaledTime+2;var offers=share.UpdateOffers;
                    if(offers.Length>0){var offer=offers[updateCursor++%offers.Length];peer.Send(Encode(new Packet{type="update-offer",offer=offer}));}
                }
            }
            else
            {
                if(!interestsSent)SendUpdateInterests();
                if(share.CompleteUpdate(out string id,out string error))
                {
                    if(id!=null)
                    {
                        stagedUpdates.Add(pendingUpdate);if(!receivedUpdates.TryGetValue(pendingManifest.pack,out var versions))receivedUpdates[pendingManifest.pack]=versions=new HashSet<long>();versions.Add(long.Parse(pendingManifest.toVersion,System.Globalization.CultureInfo.InvariantCulture));
                        DataUpdateStatus="Host map changes cached · active drive unchanged";SendUpdateInterests();
                    }
                    else DataUpdateStatus="Map sharing deferred · "+error;pendingUpdate=null;pendingManifest=null;
                }
            }
        }
        void SendUpdateInterests()
        {
            var interests=CountryPacks.Installed().Where(p=>p.Ready&&(bool?)p.Metadata["completeNodes"]==true).GroupBy(p=>p.Id).Take(8).Select(g=>
            {
                var pack=g.First();receivedUpdates.TryGetValue(pack.Id,out var versions);return new PeerUpdateInterest{pack=pack.Id,version=PeerUpdateRules.StagedTip(pack.Version,versions??new HashSet<long>())};
            }).ToArray();
            peer.Send(Encode(new Packet{type="update-interest",interests=interests}));interestsSent=true;
        }
        void ReceiveUpdateOffer(DataFileManifest offer)
        {
            if(offer==null||share.ReceivingUpdate||pendingUpdate!=null||!Session.Updates||LocationLoading.Busy||!DataPackageRules.NetworkMayRun(Session.Downloads.Busy,Session.Conditions.NetworkingBusy,LocationLoading.Busy,RoundTripMs,GameplayQueuedBytes))return;
            if(!DataPackageRules.VerifySignature(offer,HostFingerprint))return;
            string identity=offer.id+"/"+offer.sha256;if(stagedUpdates.Contains(identity))return;
            CountryPack match=null;
            foreach(var pack in CountryPacks.Installed())
                if(pack.Ready&&(bool?)pack.Metadata["completeNodes"]==true&&pack.Id==offer.pack&&PeerUpdateRules.Matches(offer,pack.Id,pack.Version,pack.ReplicationSource)){match=pack;break;}
            if(match==null)return;
            pendingUpdate=identity;pendingManifest=offer;share.ReceiveUpdate(address,dataToken,offer,HostFingerprint,match.Folder,Session.Updates.Folder);DataUpdateStatus="Caching host map changes in background…";
        }
        public bool EnvironmentSynchronized=>Hosting||latest!=null&&Session.Conditions.NetworkOverride&&Mathf.Abs(Mathf.DeltaAngle(Session.Conditions.Hour*15,latest.hour*15))<.01f&&Mathf.Abs(Session.Conditions.Rain-latest.rain)<.0001f&&Mathf.Abs(Session.Conditions.Wetness-latest.wetness)<.0001f&&Mathf.Abs(Session.Conditions.Clouds-latest.clouds)<.0001f&&Mathf.Abs(Session.Conditions.Wind-latest.wind)<.0001f&&Mathf.Abs(Session.Conditions.Fog-latest.fog)<.000001f;
        OwnedCar PublicBuild(){Session.SyncActiveCondition();var car=Session.Profile.garage.Active.Clone();car.id="";return car;}
        static bool ValidDents(System.Collections.Generic.List<SavedDent> dents)
        {
            if(dents==null)return true;if(dents.Count>32)return false;
            foreach(var d in dents)if(d==null||!Finite(d.x)||!Finite(d.y)||!Finite(d.z)||!Finite(d.nx)||!Finite(d.ny)||!Finite(d.nz)||!Finite(d.severity)||Mathf.Abs(d.x)>10||Mathf.Abs(d.y)>10||Mathf.Abs(d.z)>10||d.severity<0||d.severity>80)return false;
            return true;
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static bool Valid(State s)=>s!=null&&s.model>=0&&s.model<GarageRules.Catalogue.Length&&(s.model!=3||RaceWireRules.Valid(s.raceSetup,s.raceTyres))&&s.parkingAcknowledged>=0&&(!s.parkingHolding||s.parkingApplied)&&Finite(s.position.x)&&Finite(s.position.y)&&Finite(s.position.z)&&s.position.sqrMagnitude<100000000&&Finite(s.rotation.x)&&Finite(s.rotation.y)&&Finite(s.rotation.z)&&Finite(s.rotation.w)&&s.rotation.x*s.rotation.x+s.rotation.y*s.rotation.y+s.rotation.z*s.rotation.z+s.rotation.w*s.rotation.w>.5f&&Finite(s.velocity.x)&&Finite(s.velocity.y)&&Finite(s.velocity.z)&&Finite(s.damage)&&Finite(s.steer)&&Finite(s.inputSteer)&&s.velocity.sqrMagnitude<250000&&ValidDents(s.dents)&&s.wheelY!=null&&s.wheelY.Length==4&&s.wheelY.All(y=>Finite(y)&&y>=.14f&&y<=.96f);
        static State Capture(VehicleController c)=>new State{model=c.Metrics.Model,raceSetup=c.RaceSetup?.Clone(),raceTyres=c.RaceTyres?.Clone(),position=c.Body.position,rotation=c.Body.rotation,velocity=c.Body.linearVelocity,wheelY=c.Visual.Wheels.Select(w=>w.localPosition.y).ToArray(),steer=c.Steering,damage=c.Damage,inputSteer=c.NetworkDriven?c.NetworkSteer:c.Input.Steer,reverse=c.Reverse,braking=c.Braking,parkingApplied=c.ParkingBrakeApplied,parkingHolding=c.ParkingBrakeHolding,parkingAcknowledged=c.ParkingBrakeCommandAck,revision=c.Visual.DamageRevision,dents=c.Visual.RecordedDents};
        void Apply(VehicleController c,State s)
        {
            if(s.model!=c.Metrics.Model)return;
            float blend=c.ReplicaRevision<0||Vector3.Distance(c.Body.position,s.position)>15?1:1-Mathf.Exp(-Time.unscaledDeltaTime*22);
            if (blend == 1) VehiclePopulation.VehiclePopulationHooks.PoseWillReset(c);
            c.Body.position=Vector3.Lerp(c.Body.position,s.position,blend);c.Body.rotation=Quaternion.Slerp(c.Body.rotation,s.rotation,blend);c.transform.SetPositionAndRotation(c.Body.position,c.Body.rotation);if(s.model==3)c.ReplicaRaceCondition(s.raceSetup,s.raceTyres);c.ReplicaVelocity=s.velocity;c.ReplicaDamage(s.damage);c.ReplicaReversing=s.reverse;c.ReplicaParking(s.parkingApplied,s.parkingHolding,s.parkingAcknowledged);c.Visual.Brake(s.braking);if(c.ReplicaRevision!=s.revision){c.Visual.RestoreDents(s.dents);c.ReplicaRevision=s.revision;}
            for(int i=0;i<4;i++){var p=c.Visual.Wheels[i].localPosition;p.y=Mathf.Lerp(p.y,s.wheelY[i],blend);c.Visual.Wheels[i].localPosition=p;}
            c.Visual.SetSteering(s.inputSteer);c.Visual.AnimateReplica(s.steer,s.velocity.magnitude);
        }
        public void Stop()
        {
            bool restoreBackground=Active;disconnectNotice=null;syncing=offerSent=false;pendingHello=null;expectedHost=requiredEpoch=null;dataToken=pendingUpdate=null;pendingManifest=null;peerInterests=null;interestsSent=false;receivedUpdates.Clear();stagedUpdates.Clear();guestDataBudget=true;nextCatalogue=nextUpdateOffer=0;updateCursor=0;DataUpdateStatus="";if(share!=null)completedSharedBytes+=share.BytesSent;share?.Dispose();share=null;
            peer?.Dispose();peer=null;connecting?.Dispose();connecting=null;listener?.Stop();listener=null;Hosting=Joined=false;latest=null;
            if(Other){Session.RetireActualVehicle(this, Other.gameObject);Destroy(Other.gameObject);}
            Other=null;
            if(Session&&Session.Car&&Session.Car.Replica){Session.Car.Replica=false;Session.Car.Body.isKinematic=false;Session.Car.Recover(Session.Car.transform.position,Session.Car.transform.rotation);}
            if(Session&&Session.Conditions)Session.Conditions.NetworkOverride=false;
            if(Session&&Session.World&&Session.World.GeographicWorld)Session.World.GeographicWorld.SecondaryObserver=null;
            if(restoreBackground)Application.runInBackground=previousBackground;Status="Offline";
        }
        void OnDestroy(){Stop();}
    }
}
