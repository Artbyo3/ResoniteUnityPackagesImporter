#if MODEL_UNIT_TESTS
using Assimp;
using UnityPackageImporter.Models;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
Vector3D Point(Matrix4x4 m, Vector3D v) => new(m.A1*v.X+m.A2*v.Y+m.A3*v.Z+m.A4,
    m.B1*v.X+m.B2*v.Y+m.B3*v.Z+m.B4, m.C1*v.X+m.C2*v.Y+m.C3*v.Z+m.C4);
void Near(Vector3D actual, Vector3D expected, string reason) => Check((actual-expected).Length() < 0.002f * Math.Max(1, expected.Length()), reason + $" actual={actual}, expected={expected}");
Matrix4x4 Translation(float x, float y, float z) => Matrix4x4.FromTranslation(new Vector3D(x,y,z));
Matrix4x4 Scale(float x, float y, float z) => Matrix4x4.FromScaling(new Vector3D(x,y,z));
// Use an explicit column-vector product as an independent oracle. AssimpNet's
// operator composition order is reversed relative to this notation.
Matrix4x4 Product(params Matrix4x4[] matrices)
{
    float[,] Read(Matrix4x4 m) => new[,] {{m.A1,m.A2,m.A3,m.A4},{m.B1,m.B2,m.B3,m.B4},{m.C1,m.C2,m.C3,m.C4},{m.D1,m.D2,m.D3,m.D4}};
    var result=Read(Matrix4x4.Identity);
    foreach(var matrix in matrices)
    {
        var next=Read(matrix);var product=new float[4,4];
        for(int r=0;r<4;r++)for(int c=0;c<4;c++)for(int k=0;k<4;k++)product[r,c]+=result[r,k]*next[k,c];
        result=product;
    }
    return new Matrix4x4(result[0,0],result[0,1],result[0,2],result[0,3],result[1,0],result[1,1],result[1,2],result[1,3],result[2,0],result[2,1],result[2,2],result[2,3],result[3,0],result[3,1],result[3,2],result[3,3]);
}
Matrix4x4 Convert(Matrix4x4 matrix, float units) => Product(Scale(units,units,units), matrix, Scale(1/units,1/units,1/units));

foreach (float factor in new[] { 0.01f, 0.1f, 1f, 100f })
Test("Skin deformation commutes with coordinate conversion: " + factor, () =>
{
    var scene = new Scene { RootNode = new Node("Model") };
    scene.RootNode.Transform = Product(Translation(4,-3,2), Scale(100,100,100));
    var boneNode = new Node("Bone", scene.RootNode) { Transform = Product(Translation(0,0.7f,0), Matrix4x4.FromRotationZ(0.3f)) };
    scene.RootNode.Children.Add(boneNode);
    var meshNode = new Node("Clothing",scene.RootNode) { Transform = Product(Translation(0.1f,0.02f,0), Scale(1,2,0.5f)) };
    scene.RootNode.Children.Add(meshNode);
    var mesh = new Mesh(); mesh.Vertices.Add(new Vector3D(0.2f,0.8f,0.05f));
    var inverseBone = Product(scene.RootNode.Transform, boneNode.Transform); inverseBone.Inverse();
    var bone = new Bone { Name="Bone", OffsetMatrix=Product(inverseBone, scene.RootNode.Transform, meshNode.Transform) };
    mesh.Bones.Add(bone); scene.Meshes.Add(mesh); meshNode.MeshIndices.Add(0);
    var posed = Product(scene.RootNode.Transform, boneNode.Transform, Matrix4x4.FromRotationX(0.4f));
    var expected = Point(Product(posed, bone.OffsetMatrix), mesh.Vertices[0]) * factor;
    var originalBoneLinear = boneNode.Transform.A1;
    ModelUnitConversion.Apply(scene, factor);
    var convertedPose = Product(scene.RootNode.Transform, boneNode.Transform, Matrix4x4.FromRotationX(0.4f));
    Near(Point(Product(convertedPose, bone.OffsetMatrix),mesh.Vertices[0]), expected, "Skin and bind pose no longer use the same unit");
    Check(boneNode.Transform.A1 == originalBoneLinear && scene.RootNode.Transform.A1 == 100, "Authored scale/rotation changed");
});
Test("Authored scene transforms do not reintroduce the FBX unit scale", () =>
{
    const float units=0.01f;
    var sourceBone=Product(Scale(100,100,100), Translation(0,0.7f,0));
    var offset=Translation(0,-0.7f,0);
    var vertex=new Vector3D(0.25f,0.9f,0);
    var authoredBone=Convert(sourceBone, units);
    var expected=Point(Product(sourceBone, offset),vertex)*units;
    var legacy=Point(Product(authoredBone, offset),vertex);
    Check((legacy-expected).Length()>10, "Fixture did not reproduce the mixed-unit failure");
    var scene=new Scene {RootNode=new Node("Model")};
    var mesh=new Mesh();mesh.Vertices.Add(vertex);
    var bone=new Bone {Name="Bone",OffsetMatrix=offset};mesh.Bones.Add(bone);scene.Meshes.Add(mesh);
    ModelUnitConversion.Apply(scene,units);
    Near(Point(Product(authoredBone,bone.OffsetMatrix),mesh.Vertices[0]),expected,"Rebuilt skin remains oversized");
});
Test("Blendshape positions and translation keys convert, directional and dimensionless data survive", () =>
{
    var scene=new Scene {RootNode=new Node("Model")};
    var mesh=new Mesh();mesh.Vertices.Add(new Vector3D(1,2,3));mesh.Normals.Add(new Vector3D(0,1,0));
    mesh.Tangents.Add(new Vector3D(1,0,0));mesh.TextureCoordinateChannels[0].Add(new Vector3D(0.2f,0.3f,0));
    var shape=new MeshAnimationAttachment();shape.Vertices.Add(new Vector3D(2,4,6));shape.Normals.Add(new Vector3D(0,0,1));shape.Weight=0.6f;
    mesh.MeshAnimationAttachments.Add(shape);var bone=new Bone();bone.VertexWeights.Add(new VertexWeight(0,0.75f));mesh.Bones.Add(bone);scene.Meshes.Add(mesh);
    var animation=new Animation();var channel=new NodeAnimationChannel();channel.PositionKeys.Add(new VectorKey(3,new Vector3D(1,2,3)));channel.ScalingKeys.Add(new VectorKey(3,new Vector3D(2,3,4)));
    animation.NodeAnimationChannels.Add(channel);scene.Animations.Add(animation);
    ModelUnitConversion.Apply(scene,0.01f);
    Near(shape.Vertices[0],new Vector3D(0.02f,0.04f,0.06f),"Blendshape position not converted");
    Near(channel.PositionKeys[0].Value,new Vector3D(0.01f,0.02f,0.03f),"Animation translation not converted");
    Near(channel.ScalingKeys[0].Value,new Vector3D(2,3,4),"Animation scale modified");
    Near(mesh.Normals[0],new Vector3D(0,1,0),"Normal modified");
    Near(mesh.Tangents[0],new Vector3D(1,0,0),"Tangent modified");
    Near(mesh.TextureCoordinateChannels[0][0],new Vector3D(0.2f,0.3f,0),"UV modified");
    Check(shape.Weight==0.6f && bone.VertexWeights[0].Weight==0.75f && channel.PositionKeys[0].Time==3,"Weights or animation timing modified");
});
Test("Mirrored and nonuniform transforms preserve linear terms", () =>
{
    var scene=new Scene {RootNode=new Node("Model") {Transform=Product(Translation(12,3,4),Matrix4x4.FromRotationY(0.7f),Scale(-2,3,4))}};
    var old=scene.RootNode.Transform;
    ModelUnitConversion.Apply(scene,0.01f);
    Near(Point(scene.RootNode.Transform,new Vector3D(0.02f,0.03f,0.04f)),Point(old,new Vector3D(2,3,4))*0.01f,"Reflection or nonuniform scale changed");
});
Test("Shared mesh and bone objects convert once", () =>
{
    var scene=new Scene {RootNode=new Node("Model")};
    var mesh=new Mesh();mesh.Vertices.Add(new Vector3D(10,0,0));
    var second=new Mesh();var bone=new Bone {OffsetMatrix=Translation(10,0,0)};
    mesh.Bones.Add(bone);second.Bones.Add(bone);scene.Meshes.Add(mesh);scene.Meshes.Add(mesh);scene.Meshes.Add(second);
    ModelUnitConversion.Apply(scene,0.1f);
    Near(mesh.Vertices[0],new Vector3D(1,0,0),"Shared mesh converted twice");
    Check(Math.Abs(bone.OffsetMatrix.A4-1)<0.0001,"Shared bind pose converted twice");
});
foreach (float invalid in new[] { 0f,-1f,float.NaN,float.PositiveInfinity })
Test("Invalid unit conversion is rejected before mutation: "+invalid,()=>
{
    var scene=new Scene {RootNode=new Node("Model") {Transform=Translation(10,20,30)}};
    try {ModelUnitConversion.Apply(scene,invalid);throw new Exception("Accepted invalid units");}
    catch(ArgumentOutOfRangeException) {Check(scene.RootNode.Transform.A4==10,"Mutated before validating");}
});
int failed=0;
foreach(var test in tests) try {test.Run();Console.WriteLine("PASS "+test.Name);} catch(Exception e) {failed++;Console.WriteLine("FAIL "+test.Name+": "+e.Message);}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");
return failed==0?0:1;
#endif
