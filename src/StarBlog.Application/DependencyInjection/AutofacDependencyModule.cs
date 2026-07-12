using Autofac;
using System.Reflection;
using Module = Autofac.Module;

namespace StarBlog.Application.DependencyInjection;

public sealed class AutofacDependencyModule : Module {
    private readonly Assembly[] _assemblies;

    public AutofacDependencyModule(params Assembly[] assemblies) {
        _assemblies = assemblies
            .Where(a => a != null)
            .Distinct()
            .ToArray();
    }

    protected override void Load(ContainerBuilder builder) {
        builder.RegisterAssemblyTypes(_assemblies)
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<SingletonDependencyAttribute>(false) != null)
            .AsSelf()
            .AsImplementedInterfaces()
            .SingleInstance();

        builder.RegisterAssemblyTypes(_assemblies)
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<ScopedDependencyAttribute>(false) != null)
            .AsSelf()
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        builder.RegisterAssemblyTypes(_assemblies)
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<TransientDependencyAttribute>(false) != null)
            .AsSelf()
            .AsImplementedInterfaces()
            .InstancePerDependency();
    }
}
