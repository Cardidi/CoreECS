namespace CoreECS.Test.Analyzers
{
    internal static class AnalyzerTestSource
    {
        public const string Stubs = @"
using System;
using CoreECS;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    public interface IComponent<T> where T : struct, IComponent<T> { }

    public readonly struct ComponentRef<T> where T : struct, IComponent<T>
    {
        private static T s_value;
        public ref readonly T RO => ref s_value;
        public ref T RW => ref s_value;
    }
}

namespace CoreECS.Structures
{
    public sealed class Structure
    {
        public ReadOnlySpan<T> GetReadOnlyDenseColumn<T>() where T : struct, IComponent<T> => default;
        public Span<T> GetReadWriteDenseColumn<T>() where T : struct, IComponent<T> => default;
    }
}

namespace CoreECS
{
    public sealed class Entity
    {
        public ComponentRef<T> GetComponent<T>() where T : struct, IComponent<T> => default;
        public ComponentRef<T> CreateComponent<T>() where T : struct, IComponent<T> => default;
        public ComponentRef<T> CreateComponent<T>(T value) where T : struct, IComponent<T> => default;
        public void DestroyComponent<T>() where T : struct, IComponent<T> { }
        public void SetMask(ulong mask) { }
    }

    public sealed class World
    {
        public Entity CreateEntity(ulong mask = ulong.MaxValue) => default;
        public void DestroyEntity(ulong entityId) { }
        public void DestroyEntity(Entity entity) { }
    }

    public sealed class CommandBuffer
    {
        public void Playback() { }
    }
}

public struct PositionComponent : IComponent<PositionComponent>
{
    public float X;
    public float Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X;
    public float Y;
}

public static class Helpers
{
    public static void Mutate(ref PositionComponent value) { }
}

public static class TestWorld
{
    public static World Instance;
}
";

        public static string Wrap(string body) => WrapWith(string.Empty, body);

        public static string WrapWith(string declarations, string body) => Stubs + declarations + @"

public static class Scenario
{
    public static void Run(Entity entity, World world, CommandBuffer commands, Structure structure)
    {
" + body + @"
    }
}
";
    }
}
