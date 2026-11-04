using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class KernelSourceAnalysisTestUnit
    {
        private static readonly Assembly Kernel = typeof(World).Assembly;

        [Test]
        public void PublicSurface_ContainsRequiredV2Types()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(EntityId).IsValueType, Is.True);
                Assert.That(typeof(Entity).IsValueType, Is.True);
                Assert.That(typeof(ComponentRef).IsValueType, Is.True);
                Assert.That(typeof(ComponentRef<>).IsValueType, Is.True);
                Assert.That(typeof(IComponent<>).IsInterface, Is.True);
                Assert.That(typeof(ITagComponent<>).IsInterface, Is.True);
                Assert.That(typeof(EntityMatcher).IsSealed, Is.True);
                Assert.That(typeof(EntityQuery).IsSealed, Is.True);
                Assert.That(typeof(EntityCollector).IsSealed, Is.True);
                Assert.That(typeof(World).IsSealed, Is.False);
                Assert.That(typeof(SystemRegistration).IsSealed, Is.True);
                Assert.That(typeof(GroupRegistration).IsSealed, Is.True);
            });
        }

        [Test]
        public void PublicSurface_RemovedLegacyTypesDoNotExist()
        {
            string[] removed =
            {
                "CoreECS.MinimalWorld",
                "CoreECS.EntityGraph",
                "CoreECS.IEntityMatcher",
                "CoreECS.IEntityQuery",
                "CoreECS.IEntityCollector",
                "CoreECS.CommandBuffer",
                "CoreECS.Defines.IComponentRefCore",
                "CoreECS.Defines.IComponentRefLocator",
            };

            foreach (string name in removed)
            {
                Assert.That(Kernel.GetType(name), Is.Null, name);
            }
        }

        [Test]
        public void Entity_ExposesOnlyTheApprovedComponentSurface()
        {
            MethodInfo[] methods = typeof(Entity).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.That(methods.Any(m => m.Name == "SetMask"), Is.False);
            Assert.That(methods.Any(m => m.Name == "GetComponents" && m.IsGenericMethod), Is.False);

            MethodInfo[] plural = methods.Where(m => m.Name == "GetComponents").ToArray();
            Assert.That(plural, Has.Length.EqualTo(1));
            Assert.That(plural[0].ReturnType, Is.EqualTo(typeof(int)));
            Assert.That(plural[0].GetParameters(), Has.Length.EqualTo(1));
            Assert.That(plural[0].GetParameters()[0].ParameterType,
                Is.EqualTo(typeof(ICollection<ComponentRef>)));
        }

        [Test]
        public void World_ExposesQueryAndCollectorFactoriesButNoLegacyQuery()
        {
            Assert.That(typeof(World).GetMethod("Query"), Is.Null);
            Assert.That(typeof(World).GetMethod("CreateQuery"), Is.Not.Null);
            Assert.That(typeof(World).GetMethod("CreateCollector"), Is.Not.Null);
            Assert.That(typeof(World).GetMethod("RegisterSystem", new[] { typeof(Type) }), Is.Not.Null);
            Assert.That(typeof(World).GetMethods().Any(m =>
                m.Name == "RegisterGroup" &&
                m.GetParameters().Length >= 1 &&
                m.GetParameters()[0].ParameterType == typeof(string)), Is.True);
        }

        [Test]
        public void World_HasTheApprovedVirtualHooks()
        {
            foreach (string name in new[] { "OnRegister", "OnSetup", "OnCleanup" })
            {
                MethodInfo? method = typeof(World).GetMethod(name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null, name);
                Assert.That(method!.IsVirtual, Is.True, name);
                Assert.That(method.IsFamily, Is.True, name);
            }
        }

        [Test]
        public void ComponentRef_HasNoRuntimeTypeOrUnsafeConversion()
        {
            Assert.That(typeof(ComponentRef).GetProperty("RuntimeType"), Is.Null);
            Assert.That(typeof(ComponentRef<>).GetProperty("RuntimeType"), Is.Null);

            foreach (MethodInfo method in typeof(ComponentRef).GetMethods())
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(bool)),
                        $"{method.Name} must not expose an unsafe flag.");
                }
            }

            foreach (MethodInfo method in typeof(ComponentRef<>).GetMethods())
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(bool)),
                        $"{method.Name} must not expose an unsafe flag.");
                }
            }
        }

        [Test]
        public void EntityCollector_HasNoChangeAlias()
        {
            Assert.That(typeof(EntityCollector).GetMethod("Change"), Is.Null);
            Assert.That(typeof(EntityCollector).GetMethod("Flush"), Is.Not.Null);
        }

        [Test]
        public void ISystem_RequiresAllLifecycleMethodsAndOnlyTickGroupIsDefaulted()
        {
            MethodInfo create = typeof(ISystem).GetMethod("OnCreate")!;
            MethodInfo tick = typeof(ISystem).GetMethod("OnTick")!;
            MethodInfo destroy = typeof(ISystem).GetMethod("OnDestroy")!;
            PropertyInfo? tickGroup = typeof(ISystem).GetProperty("TickGroup");

            Assert.Multiple(() =>
            {
                Assert.That(create.IsAbstract, Is.True);
                Assert.That(tick.IsAbstract, Is.True);
                Assert.That(destroy.IsAbstract, Is.True);
                Assert.That(tickGroup, Is.Not.Null);
                Assert.That(tickGroup!.GetMethod!.IsAbstract, Is.False);
            });
        }

        [Test]
        public void IWorldManager_KeepsTheFourStageContract()
        {
            foreach (string name in new[]
                     {
                         "OnManagerCreated", "OnWorldStarted",
                         "OnWorldEnded", "OnManagerDestroyed"
                     })
            {
                Assert.That(typeof(IWorldManager).GetMethod(name), Is.Not.Null, name);
            }
        }

        [Test]
        public void KernelSource_HasNoLegacyOrNonGoalReferences()
        {
            string root = FindRepositoryRoot();
            string kernel = Path.Combine(root, "Kernel");
            string[] files = Directory.GetFiles(kernel, "*.cs", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                foreach (string token in new[]
                         {
                             "MinimalWorld", "EntityGraph", "IEntityMatcher",
                             "IEntityQuery", "IEntityCollector", "CommandBuffer",
                             "IComponentRefCore", "IComponentRefLocator",
                             "GetComponents<", "SetMask"
                         })
                {
                    Assert.That(text.Contains(token, StringComparison.Ordinal), Is.False,
                        $"{file} still references {token}.");
                }
            }
        }

        [Test]
        public void KernelProject_KeepsApprovedBuildSettings()
        {
            string root = FindRepositoryRoot();
            string project = File.ReadAllText(Path.Combine(root, "Kernel", "Kernel.csproj"));

            Assert.Multiple(() =>
            {
                Assert.That(project, Does.Contain("<PackageId>CoreECS</PackageId>"));
                Assert.That(project, Does.Contain("<AssemblyName>CoreECS</AssemblyName>"));
                Assert.That(project, Does.Contain("<RootNamespace>CoreECS</RootNamespace>"));
                Assert.That(project, Does.Contain("<LangVersion>9</LangVersion>"));
                Assert.That(project, Does.Contain("<Nullable>enable</Nullable>"));
                Assert.That(project, Does.Contain("net8.0;netstandard2.1"));
                Assert.That(File.Exists(Path.Combine(root, "global.json")), Is.False);
            });
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CoreECS.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("CoreECS.sln was not found above the test output.");
        }
    }
}
