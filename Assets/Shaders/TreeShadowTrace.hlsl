#ifndef TREE_SHADOW_TRACE_INCLUDED
#define TREE_SHADOW_TRACE_INCLUDED
struct PineShadowVolume { float4 A, B; };
StructuredBuffer<PineShadowVolume> _PlayerTreeShapes;
StructuredBuffer<int> _PlayerTreeHeads;
StructuredBuffer<int2> _PlayerTreeLinks;
float _PlayerTreeShadowEnabled;

bool TreeVolumeHit(float3 origin, float3 target, PineShadowVolume shape)
{
    float3 ray=target-origin;
    float a=dot(ray,ray);
    if(a<.000001) return false;
    if(shape.B.w>.5)
    {
        float3 axis=shape.B.xyz-shape.A.xyz, w=origin-shape.A.xyz;
        float b=dot(ray,axis), c=max(dot(axis,axis),.000001), d=dot(ray,w), e=dot(axis,w);
        float radius2=shape.A.w*shape.A.w;
        // A receiver already inside its own proxy must not black out its surface.
        float3 fromAxis=w-axis*saturate(e/c);
        if(dot(fromAxis,fromAxis)<=radius2+.000001) return false;
        float denominator=a*c-b*b;
        float s=denominator>.000001 ? saturate((b*e-c*d)/denominator) : 0;
        float t=(b*s+e)/c;
        if(t<0) {t=0;s=saturate(-d/a);}
        else if(t>1) {t=1;s=saturate((b-d)/a);}
        float3 separation=w+ray*s-axis*t;
        return dot(separation,separation)<radius2 && s>.00001;
    }
    float3 invRadius=1/max(shape.B.xyz,float3(.001,.001,.001));
    float3 p=(origin-shape.A.xyz)*invRadius, v=ray*invRadius;
    float cc=dot(p,p)-1;
    if(cc<=.0001) return false;
    float aa=dot(v,v), bb=dot(p,v), discriminant=bb*bb-aa*cc;
    if(discriminant<0 || aa<.000001) return false;
    float root=sqrt(discriminant);
    return (-bb-root)/aa<.99999 && (-bb+root)/aa>.00001;
}

float TreeCellVisibility(int3 cell, int size, float3 origin, float3 target)
{
    if(_PlayerTreeShadowEnabled<.5) return 1;
    int node=_PlayerTreeHeads[(cell.z*(size*2)+cell.y)*size+cell.x];
    [loop] for(int i=0;i<128 && node>=0;i++)
    {
        int2 link=_PlayerTreeLinks[node];
        if(TreeVolumeHit(origin,target,_PlayerTreeShapes[link.x])) return 0;
        node=link.y;
    }
    return node>=0 ? 0 : 1;
}
#endif
