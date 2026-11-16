using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoadRush
{
    /// <summary>
    /// Refines exterior soil in plan before shared-edge conformity. Authored
    /// pavement is never changed. The target concerns sampled soil approximation,
    /// not road seams, survey accuracy or a continuous error guarantee.
    /// </summary>
    public sealed class AuthoredTerrainRefinement
    {
        // Five centimetres is a soil rendering target, independent of the road's
        // 5mm seam contract. Bound depth/work to keep malformed fields finite.
        const float SampleErrorMetres=.05f,MinimumPlanSpanMetres=.125f;
        const int MaximumDepth=5,MaximumVisits=150000,MaximumLeaves=110000;
        readonly Func<float,float,float> height;
        int visits,leaves;
        public int Visits=>visits;
        public int Leaves=>leaves;
        public int UnresolvedLeaves { get;private set; }
        public float MaximumUnresolvedError { get;private set; }
        public AuthoredTerrainRefinement(Func<float,float,float> height)
        {this.height=height??throw new ArgumentNullException(nameof(height));}
        float Height(Vector3 p)
        {
            float y=height(p.x,p.z);
            if(float.IsNaN(y)||float.IsInfinity(y))throw new InvalidDataException("Nonfinite authored soil refinement height");
            return y;
        }
        static float Span2(Vector3 a,Vector3 b)
        {float x=a.x-b.x,z=a.z-b.z;return x*x+z*z;}
        public IEnumerable<int> AddMeshSteps(Vector3[] vertices,int[] triangles,
            AuthoredTerrainTopology topology,bool includeFaces=true)
        {
            if(vertices==null||triangles==null||triangles.Length%3!=0||topology==null)
                throw new InvalidDataException("Authored soil refinement mesh");
            var stack=new Stack<(Vector3 a,Vector3 b,Vector3 c,int depth)>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];
                if(ia<0||ib<0||ic<0||ia>=vertices.Length||ib>=vertices.Length||ic>=vertices.Length)
                    throw new InvalidDataException("Authored soil refinement index");
                stack.Push((vertices[ia],vertices[ib],vertices[ic],0));
                while(stack.Count>0)
                {
                    if(++visits>MaximumVisits)throw new InvalidDataException("Authored soil refinement work budget");
                    var f=stack.Pop();var a=f.a;var b=f.b;var c=f.c;
                    var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;var mid=(a+b+c)/3f;
                    float ha=Height(a),hb=Height(b),hc=Height(c);
                    float error=Mathf.Max(Mathf.Abs(Height(ab)-(ha+hb)*.5f),
                        Mathf.Max(Mathf.Abs(Height(bc)-(hb+hc)*.5f),
                        Mathf.Max(Mathf.Abs(Height(ca)-(hc+ha)*.5f),Mathf.Abs(Height(mid)-(ha+hb+hc)/3f))));
                    float span2=Mathf.Max(Span2(a,b),Mathf.Max(Span2(b,c),Span2(c,a)));
                    if(error>SampleErrorMetres&&f.depth<MaximumDepth&&span2>MinimumPlanSpanMetres*MinimumPlanSpanMetres)
                    {
                        // Each child preserves its parent's orientation and plan
                        // coverage. A later conformity pass shares introduced cuts,
                        // also across the virtual neighbouring chunk ring.
                        int depth=f.depth+1;
                        stack.Push((ab,bc,ca,depth));stack.Push((ca,bc,c,depth));
                        stack.Push((ab,b,bc,depth));stack.Push((a,ab,ca,depth));
                    }
                    else
                    {
                        if(++leaves>MaximumLeaves)throw new InvalidDataException("Authored soil refinement leaf budget");
                        if(error>SampleErrorMetres){UnresolvedLeaves++;MaximumUnresolvedError=Mathf.Max(MaximumUnresolvedError,error);}
                        topology.AddMesh(new[]{a,b,c},new[]{0,1,2},includeFaces);
                    }
                    if(visits%32==0)yield return 0;
                }
            }
        }
    }
}
