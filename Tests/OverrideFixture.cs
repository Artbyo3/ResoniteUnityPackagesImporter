#nullable disable
#pragma warning disable CS0649 // These fields are intentionally populated by reflection in the tests.
internal class OverrideFixture
{
    public TestBounds m_AABB;
    public List<TestReference> m_Materials = new();
    public List<float> m_BlendShapeWeights;
    public TestVector position;
    public string Name { get; set; } = "before";
    public bool Enabled { get; set; }
    public int[] numbers = new[] { 3, 4 };
}
internal class TestBounds
{
    public Dictionary<string, float> m_Center;
}
internal class TestReference
{
    public string guid { get; set; }
}
internal struct TestVector
{
    public float x;
}
