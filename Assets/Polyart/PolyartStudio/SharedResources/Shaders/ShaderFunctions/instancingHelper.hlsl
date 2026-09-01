#ifndef POLYART_INSTANCING
#define POLYART_INSTANCING

struct GrassData
{
    float3 position;
    float3 normal;
    float width, height;
};

StructuredBuffer<GrassData> positionBuffer;

void instancingSetup() {
	
}

void GetInstanceID_float(out float Out){
	Out = 0;
	#ifndef SHADERGRAPH_PREVIEW
	#if UNITY_ANY_INSTANCING_ENABLED
	Out = unity_InstanceID;
	#endif
	#endif
}

float Hash(float seed)
{
    return frac(sin(seed * 127.1) * 43758.5453);
}

float2x2 RotateY(float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2x2(c, -s, s, c);
}

void Instancing_float(float3 Position, float3 Normals, out float3 OutPosition, out float3 OutNormals, out float3 OutPivot) {
    OutPosition = Position;
    OutNormals = Normals;
    OutPivot = 0;
    
#ifndef SHADERGRAPH_PREVIEW
    float id;
    GetInstanceID_float(id);
    
    // Retrieve instance data
    GrassData instanceData = positionBuffer[id];

    // Apply scaling
    Position.xz *= instanceData.width;
    Position.y *= instanceData.height;

    // Generate a random rotation angle in radians
    float randomAngle = Hash(id) * 6.2831853; // 2 * PI (full circle)
    
    // Apply Y-axis rotation to the local position
    Position.xz = mul(RotateY(randomAngle), Position.xz);
    Normals.xz = mul(RotateY(randomAngle), Normals.xz);
	
    float3 instanceNormal = normalize(instanceData.normal); // Ensure it's normalized

    // Define the original up direction (local Y-axis)
    float3 localUp = float3(0, 1, 0);

    // If the normal is already aligned with the up axis, use an alternative basis
    float3 tangent = abs(instanceNormal.y) < 0.99 ? cross(localUp, instanceNormal) : float3(1, 0, 0);
    tangent = normalize(tangent);

    // Compute the binormal (right vector)
    float3 binormal = normalize(cross(instanceNormal, tangent));

    // Construct rotation matrix (column-major for HLSL)
    float3x3 rotationMatrix = float3x3(
        binormal.x, instanceNormal.x, tangent.x,
        binormal.y, instanceNormal.y, tangent.y,
        binormal.z, instanceNormal.z, tangent.z
    );

    // Rotate the local position and apply instance position
    OutPosition = mul(rotationMatrix, Position) + instanceData.position;
    OutNormals = mul(rotationMatrix, Normals);
    OutPivot = positionBuffer[id].position;
#endif

}


#endif